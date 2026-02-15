import sqlite3 from 'sqlite3';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import 'dotenv/config';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const dbPath = process.env.DB_PATH || path.resolve(__dirname, '../data/danmaku_data.db');
const dbDir = path.dirname(dbPath);
if (!fs.existsSync(dbDir)) {
  fs.mkdirSync(dbDir, { recursive: true });
}
const db = new sqlite3.Database(dbPath);
console.log(`Database path: ${dbPath}`);

// 将 db.run/get/all 包装为 Promise
const dbRun = (sql: string, params: any[] = []) => new Promise<any>((resolve, reject) => {
  db.run(sql, params, function (err) { err ? reject(err) : resolve(this); });
});

export const dbGet = (sql: string, params: any[] = []) => new Promise<any>((resolve, reject) => {
  db.get(sql, params, (err, row) => err ? reject(err) : resolve(row));
});

const dbAll = (sql: string, params: any[] = []) => new Promise<any[]>((resolve, reject) => {
  db.all(sql, params, (err, rows) => err ? reject(err) : resolve(rows));
});

// 初始化表结构
export async function initDb() {
  console.log('Initializing database tables...');
  
  try {
    await dbRun(`
      CREATE TABLE IF NOT EXISTS sessions (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        room_id TEXT,
        title TEXT,
        user_name TEXT,
        start_time INTEGER,
        end_time INTEGER,
        file_path TEXT,
        summary_json TEXT,
        gift_summary_json TEXT,
        created_at DATETIME DEFAULT CURRENT_TIMESTAMP
      )
    `);
    console.log('Table "sessions" check/create passed.');
  } catch (e) {
    console.error('Failed to init table "sessions":', e);
    throw e;
  }

  try {
    await dbRun(`
      CREATE TABLE IF NOT EXISTS song_requests (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        session_id INTEGER,
        room_id TEXT,
        user_name TEXT,
        uid TEXT,
        song_name TEXT,
        singer TEXT,
        created_at INTEGER,
        FOREIGN KEY(session_id) REFERENCES sessions(id)
      )
    `);
    console.log('Table "song_requests" check/create passed.');
  } catch (e) {
    console.error('Failed to init table "song_requests":', e);
    // 这里不 throw，以免影响主服务启动，但会记录严重错误
  }
  
  // 检查是否存在 gift_summary_json 列，如果不存在则添加
  try {
    await dbRun('ALTER TABLE sessions ADD COLUMN gift_summary_json TEXT');
  } catch (e) {
    // 列已存在或其他非关键错误，忽略
  }
  
  console.log('Database initialization sequence completed.');
}

// 导出初始化函数，确保在使用前调用
// initDb().catch(console.error); // 移除自动调用，改为手动控制或在处理函数内检查

export interface DanmakuMessage {
  type: string;
  timestamp: number;
  text?: string;
  price?: number;
  name?: string;
  count?: number;
  sender: {
    uid: string;
    name: string;
  };
}

export interface AnalysisResult {
  totalCount: number;
  userStats: {
    [userName: string]: {
      count: number;
      scCount: number;
      uid: string;
    };
  };
  timeline: [number, number][];
  topKeywords: { word: string; count: number }[];
}

export interface GiftAnalysisResult {
  totalPrice: number;
  userStats: {
    [userName: string]: {
      totalPrice: number;
      giftPrice: number;
      scPrice: number;
      uid: string;
    };
  };
  timeline: [number, number][]; // [timestamp, price]
  topGifts: { name: string; count: number; price: number }[];
}

let isDbInitialized = false;
async function ensureDbInit() {
  if (!isDbInitialized) {
    await initDb();
    isDbInitialized = true;
  }
}

/**
 * 扫描目录并处理所有未入库的 XML 文件
 */
export async function scanDirectory(dirPath: string) {
  await ensureDbInit();
  if (!fs.existsSync(dirPath)) {
    console.warn(`目录不存在，跳过扫描: ${dirPath}`);
    return;
  }

  console.log(`开始扫描目录: ${dirPath}`);
  
  let processedCount = 0;

  const processDir = async (currentDir: string) => {
    const files = fs.readdirSync(currentDir);
    
    for (const file of files) {
      const fullPath = path.join(currentDir, file);
      const stat = fs.statSync(fullPath);
      
      if (stat.isDirectory()) {
        await processDir(fullPath);
      } else if (file.endsWith('.xml')) {
        const result = await processDanmakuFile(fullPath);
        if (result) processedCount++;
      }
    }
  };

  await processDir(dirPath);
  
  console.log(`扫描完成。新增处理文件数: ${processedCount}`);
  return processedCount;
}

/**
 * 分析弹幕 JSON 文件并存入数据库
 */
export async function processDanmakuFile(filePath: string) {
  try {
    await ensureDbInit();
    if (!fs.existsSync(filePath)) {
      console.error(`文件不存在: ${filePath}`);
      return;
    }

    const content = fs.readFileSync(filePath, 'utf-8');
    let messages: DanmakuMessage[] = [];
    let meta: any = {};

    if (!filePath.endsWith('.xml')) {
      console.warn(`跳过非 XML 文件: ${filePath}`);
      return;
    }

    // 解析 XML 格式
    console.log(`正在处理 XML: ${path.basename(filePath)}`);
    
    // 提取元数据
    const titleMatch = content.match(/<room_title>(.*?)<\/room_title>/);
    const userMatch = content.match(/<user_name>(.*?)<\/user_name>/);
    const roomMatch = content.match(/<room_id>(.*?)<\/room_id>/);
    const startMatch = content.match(/<video_start_time>(.*?)<\/video_start_time>/);
    
    meta = {
      title: titleMatch ? titleMatch[1] : '未知直播',
      user_name: userMatch ? userMatch[1] : '未知主播',
      room_id: roomMatch ? roomMatch[1] : '',
      recordStartTimestamp: startMatch ? parseInt(startMatch[1]) : Date.now()
    };

    const songRequests: {
      user_name: string;
      uid: string;
      song_name: string;
      singer: string;
      created_at: number;
    }[] = [];

    // 提取弹幕 <d p="...">内容</d>
    const danmakuRegex = /<d p="([^"]+)" user="([^"]+)" uid="([^"]+)" timestamp="([^"]+)"[^>]*>(.*?)<\/d>/g;
    let match;
    while ((match = danmakuRegex.exec(content)) !== null) {
      const text = match[5];
      const timestamp = parseInt(match[4]);
      const senderName = match[2];
      const senderUid = match[3];

      messages.push({
        type: 'comment',
        text: text,
        timestamp: timestamp,
        sender: {
          name: senderName,
          uid: senderUid
        }
      });

      // 识别点歌
      const trimmedText = text.trim();
      if (trimmedText.startsWith('点歌')) {
        // 截取"点歌"之后的所有内容作为歌名，去除首尾空格
        const songName = trimmedText.substring(2).trim();
        if (songName) {
           songRequests.push({
             user_name: senderName,
             uid: senderUid,
             song_name: songName,
             singer: '', // 暂时留空，不做自动识别
             created_at: timestamp
           });
        }
      }
    }

    // 提取礼物 <gift ...>
    // 用户反馈：新版 gift 中 price 多了三个 0，需要除以 1000
    const giftRegex = /<gift ts="[^"]+" giftname="([^"]+)" giftcount="([^"]+)" price="([^"]+)" user="([^"]+)" uid="([^"]+)" timestamp="([^"]+)"/g;
    let totalGiftPrice = 0;
    while ((match = giftRegex.exec(content)) !== null) {
      // 礼物价格统一除以 1000
      const price = (parseInt(match[3]) || 0) / 1000;
      const count = parseInt(match[2]) || 1;
      
      messages.push({
        type: 'give_gift',
        name: match[1],
        count: count,
        price: price,
        timestamp: parseInt(match[6]),
        sender: {
          name: match[4],
          uid: match[5]
        }
      });
    }

    // 提取 SC <sc ...>
    // 格式: <sc ts="..." price="..." user="..." uid="..." ...>Content</sc>
    // 用户反馈：
    // 旧版 SC: 包含 ts="..."，价格多了三个 0，需要除以 1000
    // 新版 SC: 不含 ts="..."，价格是真实的
    const scRegex = /<sc [^>]*price="([^"]+)"[^>]*user="([^"]+)"[^>]*uid="([^"]+)"[^>]*timestamp="([^"]+)"[^>]*>(.*?)<\/sc>/g;
    while ((match = scRegex.exec(content)) !== null) {
      let price = parseFloat(match[1]) || 0; 
      const fullTag = match[0];
      
      // 通过是否存在 ts="xxx" 来区分新旧版
      if (fullTag.includes(' ts="')) {
        price = price / 1000;
      }
      
      messages.push({
        type: 'super_chat',
        text: match[5],
        price: price,
        timestamp: parseInt(match[4]),
        sender: {
          name: match[2],
          uid: match[3]
        }
      });
    }
    
    // 排序消息
    messages.sort((a, b) => a.timestamp - b.timestamp);

    const analysis: AnalysisResult = {
      totalCount: messages.length,
      userStats: {},
      timeline: [],
      topKeywords: []
    };

    const giftAnalysis: GiftAnalysisResult = {
      totalPrice: 0,
      userStats: {},
      timeline: [],
      topGifts: []
    };

    const timelineMap = new Map<number, number>();
    const giftTimelineMap = new Map<number, number>();
    const keywordMap = new Map<string, number>();
    const giftCountMap = new Map<string, { count: number; price: number }>();

    // 辅助函数：保留一位小数，解决浮点数精度问题
    const roundPrice = (p: number) => Math.round(p * 10) / 10;

    messages.forEach((msg) => {
      // 1. 用户统计 (Chat)
      const userName = msg.sender.name;
      if (!analysis.userStats[userName]) {
        analysis.userStats[userName] = {
          count: 0,
          scCount: 0,
          uid: msg.sender.uid
        };
      }
      analysis.userStats[userName].count++;
      if (msg.type === 'super_chat') {
        analysis.userStats[userName].scCount++;
      }

      // 2. 礼物/SC 统计 (Gift)
      if (msg.type === 'give_gift' || msg.type === 'super_chat') {
        const price = msg.price || 0;
        giftAnalysis.totalPrice += price;
        
        if (!giftAnalysis.userStats[userName]) {
          giftAnalysis.userStats[userName] = {
            totalPrice: 0,
            giftPrice: 0,
            scPrice: 0,
            uid: msg.sender.uid
          };
        }
        
        giftAnalysis.userStats[userName].totalPrice += price;
        if (msg.type === 'give_gift') {
          giftAnalysis.userStats[userName].giftPrice += price;
          // 统计热门礼物
          const giftName = msg.name || 'Unknown';
          if (!giftCountMap.has(giftName)) {
            giftCountMap.set(giftName, { count: 0, price: 0 });
          }
          const g = giftCountMap.get(giftName)!;
          g.count += (msg.count || 1);
          g.price += price;
        } else if (msg.type === 'super_chat') {
          giftAnalysis.userStats[userName].scPrice += price;
        }

        // 礼物时间轴
        const ts = msg.timestamp;
        const bucketTime = Math.floor(ts / 60000) * 60000;
        giftTimelineMap.set(bucketTime, (giftTimelineMap.get(bucketTime) || 0) + price);
      }

      // 3. 时间轴 (每分钟)
      const ts = msg.timestamp;
      const bucketTime = Math.floor(ts / 60000) * 60000;
      timelineMap.set(bucketTime, (timelineMap.get(bucketTime) || 0) + 1);

      // 4. 关键词
      if (msg.type === 'comment' && msg.text && msg.text.length > 1) {
        const words = msg.text.split(/\s+/);
        words.forEach(w => {
          if (w.length > 1) {
            keywordMap.set(w, (keywordMap.get(w) || 0) + 1);
          }
        });
      }
    });

    // 格式化数据并应用舍入逻辑
    giftAnalysis.totalPrice = roundPrice(giftAnalysis.totalPrice);
    
    // 格式化用户统计
    Object.keys(giftAnalysis.userStats).forEach(user => {
      const stats = giftAnalysis.userStats[user];
      stats.totalPrice = roundPrice(stats.totalPrice);
      stats.giftPrice = roundPrice(stats.giftPrice);
      stats.scPrice = roundPrice(stats.scPrice);
    });

    // 格式化时间轴
    analysis.timeline = Array.from(timelineMap.entries())
      .sort((a, b) => a[0] - b[0]);
    
    giftAnalysis.timeline = Array.from(giftTimelineMap.entries())
      .map(([ts, price]) => [ts, roundPrice(price)] as [number, number])
      .sort((a, b) => a[0] - b[0]);

    // 格式化热门礼物
    giftAnalysis.topGifts = Array.from(giftCountMap.entries())
      .map(([name, stats]) => ({ name, count: stats.count, price: roundPrice(stats.price) }))
      .sort((a, b) => b.price - a.price)
      .slice(0, 20);

    // 格式化关键词
    analysis.topKeywords = Array.from(keywordMap.entries())
      .sort((a, b) => b[1] - a[1])
      .slice(0, 20)
      .map(([word, count]) => ({ word, count }));

    // 使用相对于数据目录的相对路径存储
    const danmakuDir = process.env.DANMAKU_DIR 
        ? path.resolve(process.env.DANMAKU_DIR)
        : path.resolve(__dirname, '../data/danmaku');
    let relativeFilePath = path.relative(danmakuDir, filePath);
    // Normalize to POSIX style for DB storage compatibility across platforms
    relativeFilePath = relativeFilePath.split(path.sep).join('/');

    // 尝试通过 room_id 和 start_time 查重 (最可靠的方式)
    const existing = await dbGet(
      'SELECT id, file_path FROM sessions WHERE room_id = ? AND start_time = ?', 
      [meta.room_id, meta.recordStartTimestamp]
    );
    
    if (existing) {
      // 检查是否有点歌记录，如果没有且当前文件有点歌记录，则补充插入
      const songRequestCount = await dbGet('SELECT COUNT(*) as count FROM song_requests WHERE session_id = ?', [existing.id]);
      if (songRequestCount.count === 0 && songRequests.length > 0) {
        console.log(`补充插入 ${songRequests.length} 条点歌记录 (Session ID: ${existing.id})...`);
        const stmt = db.prepare('INSERT INTO song_requests (session_id, room_id, user_name, uid, song_name, singer, created_at) VALUES (?, ?, ?, ?, ?, ?, ?)');

        const runStmt = (req: any) => new Promise<void>((resolve, reject) => {
          stmt.run(existing.id, meta.room_id || '', req.user_name, req.uid, req.song_name, req.singer, req.created_at, (err: Error | null) => err ? reject(err) : resolve());
        });

        for (const req of songRequests) {
          await runStmt(req);
        }

        await new Promise<void>((resolve, reject) => {
          stmt.finalize((err: Error | null) => err ? reject(err) : resolve());
        });
      }

      // 如果路径变了（例如从根目录移动到了子目录），更新数据库
      if (existing.file_path !== relativeFilePath) {
         await dbRun('UPDATE sessions SET file_path = ? WHERE id = ?', [relativeFilePath, existing.id]);
         console.log(`更新文件路径: ${existing.id} -> ${relativeFilePath}`);
      } else {
         console.log(`文件已处理过，跳过: ${relativeFilePath}`);
      }
      return;
    }

    // 存入数据库
    const endTime = messages.length > 0 ? messages[messages.length - 1].timestamp : Date.now();
    
    const result = await dbRun(
      `INSERT INTO sessions (room_id, title, user_name, start_time, end_time, file_path, summary_json, gift_summary_json)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
      [
        meta.room_id || '',
        meta.title || '未知直播',
        meta.user_name || '未知主播',
        meta.recordStartTimestamp || Date.now(),
        endTime,
        relativeFilePath,
        JSON.stringify(analysis),
        JSON.stringify(giftAnalysis)
      ]
    );

    const sessionId = result.lastID;

    // 插入点歌记录
    if (songRequests.length > 0 && sessionId) {
      console.log(`正在插入 ${songRequests.length} 条点歌记录...`);
      const stmt = db.prepare('INSERT INTO song_requests (session_id, room_id, user_name, uid, song_name, singer, created_at) VALUES (?, ?, ?, ?, ?, ?, ?)');

      const runStmt = (req: any) => new Promise<void>((resolve, reject) => {
        stmt.run(sessionId, meta.room_id || '', req.user_name, req.uid, req.song_name, req.singer, req.created_at, (err: Error | null) => err ? reject(err) : resolve());
      });

      for (const req of songRequests) {
        await runStmt(req);
      }

      await new Promise<void>((resolve, reject) => {
        stmt.finalize((err: Error | null) => err ? reject(err) : resolve());
      });
    }

    console.log(`分析完成并存入数据库: ${meta.title}`);
    return analysis;
  } catch (error) {
    console.error('处理文件失败:', error);
  }
}

/**
 * 获取所有会话列表，支持筛选
 */
export async function getSessions(filters: { userName?: string; startTime?: number; endTime?: number } = {}) {
  await ensureDbInit();
  let sql = 'SELECT id, room_id, title, user_name, start_time, end_time, summary_json, gift_summary_json FROM sessions';
  const params: any[] = [];
  const whereClauses: string[] = [];

  if (filters.userName) {
    whereClauses.push('user_name = ?');
    params.push(filters.userName);
  }
  if (filters.startTime) {
    whereClauses.push('start_time >= ?');
    params.push(filters.startTime);
  }
  if (filters.endTime) {
    whereClauses.push('end_time <= ?');
    params.push(filters.endTime);
  }

  if (whereClauses.length > 0) {
    sql += ' WHERE ' + whereClauses.join(' AND ');
  }

  sql += ' ORDER BY start_time DESC';
  return dbAll(sql, params);
}

/**
 * 获取所有唯一主播列表
 * 优先获取非空的 room_id
 */
export async function getStreamers() {
  await ensureDbInit();
  // 使用 GROUP BY 确保每个主播只返回一条记录
  // MAX(room_id) 是为了在有多个 room_id 记录时（例如空和非空），优先取有值的（字符串比较中非空通常大于空，或者至少能取到一个值）
  const rows = await dbAll('SELECT user_name, MAX(room_id) as room_id FROM sessions WHERE user_name IS NOT NULL AND user_name != "" GROUP BY user_name ORDER BY user_name ASC');
  return rows;
}

/**
 * 获取特定直播的点歌记录
 */
export async function getSongRequests(sessionId: number) {
  await ensureDbInit();
  return dbAll(
    'SELECT id, user_name, uid, song_name, singer, created_at FROM song_requests WHERE session_id = ? ORDER BY created_at ASC',
    [sessionId]
  );
}

/**
 * 获取特定主播的所有点歌记录
 */
export async function getSongRequestsByRoomId(roomId: string) {
  await ensureDbInit();
  return dbAll(
    `SELECT sr.id, sr.user_name, sr.uid, sr.song_name, sr.singer, sr.created_at, s.title as session_title, s.start_time as session_start_time
     FROM song_requests sr
     LEFT JOIN sessions s ON sr.session_id = s.id
     WHERE sr.room_id = ?
     ORDER BY sr.created_at DESC`,
    [roomId]
  );
}

/**
 * 分页获取特定直播的弹幕内容
 * @param sessionId 数据库中的 session ID
 * @param page 页码 (从 1 开始)
 * @param pageSize 每页条数
 */
export async function getSessionDanmakuPaged(sessionId: number, page: number = 1, pageSize: number = 200) {
  await ensureDbInit();
  
  // 1. 获取文件路径和开播时间
  const session = await dbGet('SELECT file_path, start_time, room_id FROM sessions WHERE id = ?', [sessionId]);
  if (!session || !session.file_path) {
    throw new Error('未找到该直播记录');
  }

  // 获取数据目录
  const danmakuDir = process.env.DANMAKU_DIR 
    ? path.resolve(process.env.DANMAKU_DIR)
    : path.resolve(__dirname, '../data/danmaku');

  let fullPath = session.file_path;
  let resolvedPath = path.resolve(danmakuDir, fullPath);

  if (!fs.existsSync(resolvedPath)) {
    const basename = path.basename(fullPath);
    
    // 1. 尝试根目录 (旧逻辑)
    const rootPath = path.resolve(danmakuDir, basename);
    if (fs.existsSync(rootPath)) {
        resolvedPath = rootPath;
    } else if (session.room_id) {
        // 2. 尝试房间子目录 (如果我们移动了文件但没更新数据库)
        const roomPath = path.resolve(danmakuDir, String(session.room_id), basename);
        if (fs.existsSync(roomPath)) {
            resolvedPath = roomPath;
        }
    }
  }

  if (!fs.existsSync(resolvedPath)) {
    throw new Error(`原始弹幕文件已丢失: ${fullPath} (尝试路径: ${resolvedPath})`);
  }

  const content = fs.readFileSync(resolvedPath, 'utf-8');
  const messages: any[] = [];
  const liveStartTimeTs = session.start_time;

  // 2. 提取弹幕和 SC (采用更稳健的正则匹配)
  // 匹配所有 <d> 或 <sc> 标签
  const tagRegex = /<(d|sc)\b([^>]*)>(.*?)<\/\1>/gs;
  
  let match;
  while ((match = tagRegex.exec(content)) !== null) {
    const tagName = match[1];
    const attrsRaw = match[2];
    const text = match[3];

    // 提取属性函数
    const getAttr = (name: string) => {
      const res = new RegExp(`${name}="([^"]*)"`).exec(attrsRaw);
      return res ? res[1] : '';
    };

    const user = getAttr('user') || '匿名用户';
    let uid = getAttr('uid') || getAttr('userid') || getAttr('user_id');
    const timestampStr = getAttr('timestamp');
    let timestamp = parseInt(timestampStr || '0');
    
    // 如果没有显式的 timestamp 或 uid，尝试从 p 属性解析 (B站标准格式)
    const pAttr = getAttr('p');
    if (pAttr && (!timestamp || !uid)) {
      const pParts = pAttr.split(',');
      if (pParts.length >= 8) {
        // p 属性第 5 位是秒级时间戳，第 7 位通常是用户 ID 或 Hash
        if (!timestamp) timestamp = parseInt(pParts[4]) * 1000;
        if (!uid) uid = pParts[6];
      }
    }
    
    if (!user && !text) continue;

    const relativeSeconds = Math.max(0, Math.floor((timestamp - liveStartTimeTs) / 1000));
    
    if (tagName === 'd') {
      messages.push({
        time: relativeSeconds,
        timestamp: timestamp, // 添加绝对时间戳
        sender: user,
        uid: uid,
        text: text,
        isSC: false
      });
    } else if (tagName === 'sc') {
      let price = parseInt(getAttr('price') || '0');
      const tsAttr = getAttr('ts');
      // 优化价格解析逻辑：
      // 1. 带有 ts 属性的 <sc> 标签（如 <sc ts="411.766" price="30000" ...>）通常来自 B 站标准 XML 录制格式，其 price 单位为毫元 (1元=1000)。
      // 2. 不带 ts 属性的（如 <sc price="50" ...>）通常直接是元单位。
      // 3. 只有当存在 ts 属性且价格明显是毫元倍数（>=1000）时才进行转换，这样即使有 10 万元的 SC (price="100000")，如果没有 ts 属性，也会被正确识别为 10 万元。
      if (tsAttr && price >= 1000) {
        price = Math.floor(price / 1000);
      }
      messages.push({
        time: relativeSeconds,
        timestamp: timestamp, // 添加绝对时间戳
        sender: user,
        uid: uid,
        text: text,
        price: price,
        isSC: true
      });
    }
  }

  // 排序：按时间顺序排列
  messages.sort((a, b) => a.time - b.time);

  // 3. 分页逻辑
  const total = messages.length;
  const start = (page - 1) * pageSize;
  const end = start + pageSize;
  const pagedMessages = messages.slice(start, end);

  return {
    total,
    page,
    pageSize,
    totalPages: Math.ceil(total / pageSize),
    messages: pagedMessages
  };
}
