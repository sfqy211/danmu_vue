import sqlite3 from 'sqlite3';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import 'dotenv/config';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// 数据库初始化
const dbPath = process.env.DB_PATH || path.resolve(__dirname, '../data/danmaku_data.db');
const db = new sqlite3.Database(dbPath);
console.log(`Database path: ${dbPath}`);

// 将 db.run/get/all 包装为 Promise
const dbRun = (sql: string, params: any[] = []) => new Promise<void>((resolve, reject) => {
  db.run(sql, params, (err) => err ? reject(err) : resolve());
});

export const dbGet = (sql: string, params: any[] = []) => new Promise<any>((resolve, reject) => {
  db.get(sql, params, (err, row) => err ? reject(err) : resolve(row));
});

const dbAll = (sql: string, params: any[] = []) => new Promise<any[]>((resolve, reject) => {
  db.all(sql, params, (err, rows) => err ? reject(err) : resolve(rows));
});

// 初始化表结构
export async function initDb() {
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
      created_at DATETIME DEFAULT CURRENT_TIMESTAMP
    )
  `);
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

    // 提取弹幕 <d p="...">内容</d>
    const danmakuRegex = /<d p="([^"]+)" user="([^"]+)" uid="([^"]+)" timestamp="([^"]+)"[^>]*>(.*?)<\/d>/g;
    let match;
    while ((match = danmakuRegex.exec(content)) !== null) {
      messages.push({
        type: 'comment',
        text: match[5],
        timestamp: parseInt(match[4]),
        sender: {
          name: match[2],
          uid: match[3]
        }
      });
    }

    // 提取礼物 <gift ...>
    const giftRegex = /<gift ts="[^"]+" giftname="([^"]+)" giftcount="([^"]+)" price="([^"]+)" user="([^"]+)" uid="([^"]+)" timestamp="([^"]+)"/g;
    while ((match = giftRegex.exec(content)) !== null) {
      messages.push({
        type: 'give_gift',
        name: match[1],
        count: parseInt(match[2]),
        price: parseInt(match[3]),
        timestamp: parseInt(match[6]),
        sender: {
          name: match[4],
          uid: match[5]
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

    const timelineMap = new Map<number, number>();
    const keywordMap = new Map<string, number>();

    messages.forEach((msg) => {
      // 1. 用户统计
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

      // 2. 时间轴 (每分钟)
      const ts = msg.timestamp;
      const bucketTime = Math.floor(ts / 60000) * 60000;
      timelineMap.set(bucketTime, (timelineMap.get(bucketTime) || 0) + 1);

      // 3. 关键词
      if (msg.text && msg.text.length > 1) {
        const words = msg.text.split(/\s+/);
        words.forEach(w => {
          if (w.length > 1) {
            keywordMap.set(w, (keywordMap.get(w) || 0) + 1);
          }
        });
      }
    });

    // 格式化时间轴
    analysis.timeline = Array.from(timelineMap.entries())
      .sort((a, b) => a[0] - b[0]);

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
    
    await dbRun(
      `INSERT INTO sessions (room_id, title, user_name, start_time, end_time, file_path, summary_json)
       VALUES (?, ?, ?, ?, ?, ?, ?)`,
      [
        meta.room_id || '',
        meta.title || '未知直播',
        meta.user_name || '未知主播',
        meta.recordStartTimestamp || Date.now(),
        endTime,
        relativeFilePath,
        JSON.stringify(analysis)
      ]
    );

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
  let sql = 'SELECT id, room_id, title, user_name, start_time, end_time FROM sessions';
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
 */
export async function getStreamers() {
  await ensureDbInit();
  const rows = await dbAll('SELECT DISTINCT user_name FROM sessions WHERE user_name IS NOT NULL AND user_name != "" ORDER BY user_name ASC');
  return rows.map(row => row.user_name);
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
      const price = parseInt(getAttr('price') || '0');
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
