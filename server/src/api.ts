import express from 'express';
import cors from 'cors';
import dotenv from 'dotenv';
import path from 'node:path';
import fs from 'node:fs';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// 尝试加载根目录的 .env 文件
// 优先级：
// 1. server/.env (如果存在)
// 2. ../.env (项目根目录)
const envPathLocal = path.resolve(__dirname, '../.env');
const envPathRoot = path.resolve(__dirname, '../../.env');

if (fs.existsSync(envPathLocal)) {
  dotenv.config({ path: envPathLocal });
} else if (fs.existsSync(envPathRoot)) {
  dotenv.config({ path: envPathRoot });
} else {
  dotenv.config(); // 默认行为
}

import chokidar from 'chokidar';
import { getSessions, dbGet, getStreamers, processDanmakuFile, scanDirectory, getSessionDanmakuPaged, getSongRequests, getSongRequestsByRoomId, initDb, getSessionsTotal } from './processor.js';
import { startAvatarScheduler } from './avatar_manager.js';
import pm2 from 'pm2';
import adminRouter from './routes/admin.js';
import { getRooms } from './processor.js';
import { startRecorder } from './pm2_manager.js';

const app = express();
const port = process.env.PORT || 3001;

app.use(cors());
app.use(express.json());

// 挂载管理后台 API
app.use('/api/admin', adminRouter);

try {
  await initDb();
  console.log('数据库初始化完成');
  
  // 启动所有激活的直播间录制进程
  const rooms = await getRooms();
  for (const room of rooms) {
    if (room.is_active) {
      console.log(`Starting recorder for ${room.name} (${room.room_id})...`);
      try {
        await startRecorder(room.room_id, room.name);
      } catch (e) {
        console.error(`Failed to start recorder for ${room.name}:`, e);
      }
    }
  }
} catch (e) {
  console.error('初始化失败:', e);
}

// 托管前端静态文件
// 优先使用 Vue 构建后的 dist 目录，如果不存在则回退到 public
const distPath = path.resolve(__dirname, '../../dist');
const publicPath = path.resolve(__dirname, '../../public');
const staticPath = fs.existsSync(distPath) ? distPath : publicPath;
console.log(`Serving static files from: ${staticPath}`);

app.use(express.static(staticPath));

// 专门托管 VUP 头像/封面目录，确保即使 dist 目录被清空，头像也能持久化访问（前提是 public 挂载了 volume）
// 映射路径: /api/avatars -> server/../../public/vup-bg
const avatarPath = path.resolve(__dirname, '../../public/vup-bg');
if (!fs.existsSync(avatarPath)) {
  fs.mkdirSync(avatarPath, { recursive: true });
}
app.use('/api/avatars', express.static(avatarPath));
app.use('/api/covers', express.static(path.resolve(__dirname, '../../public/vup-cover')));

// 获取所有录制列表 (支持筛选)
app.get('/api/sessions', async (req, res) => {
  try {
    // 设置 Cache-Control 响应头，控制 CDN 和浏览器缓存
    // public: 允许 CDN 和浏览器缓存
    // max-age=60: 缓存 60 秒
    // s-maxage=60: 专门告诉 CDN 缓存 60 秒 (有些 CDN 优先看这个)
    res.set('Cache-Control', 'public, max-age=60, s-maxage=60');
    
    const filters = {
      userName: req.query.userName as string,
      startTime: req.query.startTime ? parseInt(req.query.startTime as string) : undefined,
      endTime: req.query.endTime ? parseInt(req.query.endTime as string) : undefined
    };
    const sessions = await getSessions(filters);
    res.json(sessions);
  } catch (error) {
    console.error('API Error /api/sessions:', error);
    res.status(500).json({ error: '获取列表失败' });
  }
});

// 获取会话总数 (支持筛选)
app.get('/api/sessions/total', async (req, res) => {
  try {
    const filters = {
      userName: req.query.userName as string
    };
    const total = await getSessionsTotal(filters);
    res.json({ total });
  } catch (error) {
    console.error('API Error /api/sessions/total:', error);
    res.status(500).json({ error: '获取总数失败' });
  }
});

// 获取所有唯一主播列表
app.get('/api/streamers', async (req, res) => {
  try {
    const streamers = await getStreamers();
    res.json(streamers);
  } catch (error) {
    console.error('API Error /api/streamers:', error);
    res.status(500).json({ error: '获取主播列表失败' });
  }
});

// 获取特定录制的分析摘要
app.get('/api/summary', async (req, res) => {
  try {
    const id = parseInt(req.query.id as string, 10);
    if (isNaN(id)) {
      return res.status(400).json({ error: '无效的 ID' });
    }
    
    // 我们需要获取完整的 session 信息，不仅仅是 summary_json
    // 但为了兼容前端代码，我们应该返回一个包含 summary_json 的对象
    const session = await dbGet('SELECT * FROM sessions WHERE id = ?', [id]);
    
    if (session) {
      res.json(session);
    } else {
      res.status(404).json({ error: '未找到该录制分析' });
    }
  } catch (error) {
    console.error('API Error /api/summary:', error);
    res.status(500).json({ error: '获取摘要失败' });
  }
});

// 获取特定录制的分页弹幕
app.get('/api/danmaku', async (req, res) => {
  try {
    const id = parseInt(req.query.id as string, 10);
    const page = parseInt(req.query.page as string, 10) || 1;
    const pageSize = parseInt(req.query.pageSize as string, 10) || 200;

    if (isNaN(id)) {
      return res.status(400).json({ error: '无效的 ID' });
    }

    const result = await getSessionDanmakuPaged(id, page, pageSize);
    res.json(result);
  } catch (error: any) {
    console.error('API Error /api/danmaku:', error);
    res.status(500).json({ error: error.message || '获取弹幕失败' });
  }
});

// 获取 AI 分析结果
app.post('/api/analyze', async (req, res) => {
  try {
    const { id } = req.body;
    if (!id) {
      return res.status(400).json({ error: 'Missing session ID' });
    }

    // TODO: 实现真正的 AI 分析逻辑 (调用本地 LLM 或 外部 API)
    // 目前先返回一个模拟结果，证明接口已打通
    
    // 模拟延时
    await new Promise(resolve => setTimeout(resolve, 2000));

    const mockAnalysis = `
# 弹幕情感分析报告 (模拟数据)

## 核心观点
本次直播观众情绪高涨，主要集中在以下几个话题：
1. 对主播操作的赞赏 (60%)
2. 玩梗互动 (30%)
3. 其他讨论 (10%)

## 热门关键词
- 666
- 哈哈哈哈
- 强啊
- 这种事情见多了

*注：此功能为接口测试，尚未接入真实 AI 模型。*
    `;

    res.json({ analysis: mockAnalysis });
  } catch (error) {
    console.error('API Error /api/analyze:', error);
    res.status(500).json({ error: 'AI 分析服务暂不可用' });
  }
});

// 获取点歌记录
app.get('/api/song-requests', async (req, res) => {
  try {
    const roomId = req.query.roomId as string;
    const id = parseInt(req.query.id as string, 10);
    const page = parseInt(req.query.page as string, 10) || 1;
    const pageSize = parseInt(req.query.pageSize as string, 10) || 20;
    const search = (req.query.search as string || '').toLowerCase();
    
    let requests: any[] = [];
    if (roomId) {
      requests = await getSongRequestsByRoomId(roomId);
    } else if (!isNaN(id)) {
      requests = await getSongRequests(id);
    } else {
      return res.status(400).json({ error: '无效的 ID 或 Room ID' });
    }

    // 1. 过滤 (Search)
    if (search) {
      requests = requests.filter(r => 
        (r.song_name && r.song_name.toLowerCase().includes(search)) || 
        (r.user_name && r.user_name.toLowerCase().includes(search)) ||
        (r.singer && r.singer.toLowerCase().includes(search))
      );
    }

    // 2. 分页 (Pagination)
    const total = requests.length;
    const start = (page - 1) * pageSize;
    const end = start + pageSize;
    const list = requests.slice(start, end);
    
    res.json({
      list,
      total,
      page,
      pageSize
    });
  } catch (error) {
    console.error('API Error /api/song-requests:', error);
    res.status(500).json({ error: '获取点歌记录失败' });
  }
});

// 获取 PM2 进程状态
app.get('/api/pm2-status', (req, res) => {
  pm2.connect((err) => {
    if (err) {
      console.error('PM2 connect error:', err);
      return res.status(500).json({ status: 'error', error: '无法连接到 PM2' });
    }

    pm2.list((err, list) => {
      pm2.disconnect(); // 获取完后断开连接，避免保持 socket
      
      if (err) {
        console.error('PM2 list error:', err);
        return res.status(500).json({ status: 'error', error: '无法获取进程列表' });
      }

      // 检查是否有任何进程处于 errored 状态
      const hasError = list.some((p) => p.pm2_env?.status === 'errored');
      
      res.json({
        status: hasError ? 'error' : 'success',
        processes: list.map((p) => ({
          name: p.name,
          status: p.pm2_env?.status,
          id: p.pm_id
        }))
      });
    });
  });
});

// 监听 data/danmaku 目录下的 XML 文件变化
const danmakuDir = process.env.DANMAKU_DIR 
  ? path.resolve(process.env.DANMAKU_DIR)
  : path.resolve(__dirname, '../data/danmaku');

if (!fs.existsSync(danmakuDir)) {
  console.log(`创建数据目录: ${danmakuDir}`);
  fs.mkdirSync(danmakuDir, { recursive: true });
}
const watcher = chokidar.watch(danmakuDir, {
  persistent: true,
  ignoreInitial: false, // 处理启动时已存在的文件
  awaitWriteFinish: {
    stabilityThreshold: 3000, // 稍微增加稳定性阈值，确保录制结束并改名后才处理
    pollInterval: 100
  }
});

// 监听新增和修改，因为 .raw 改名为 .xml 可能会触发 add 或 change
const handleFile = async (filePath: string) => {
  if (filePath.endsWith('.xml')) {
    console.log(`检测到文件变化: ${filePath}`);
    await processDanmakuFile(filePath);
  }
};

watcher.on('add', handleFile);
watcher.on('change', handleFile);

// 移除不再需要的 API 路由
// app.post('/api/scan', ...);


// SPA Fallback: 将所有非 API 请求重定向到 index.html
app.get('*', (req, res) => {
  if (!req.path.startsWith('/api/')) {
    res.sendFile(path.join(staticPath, 'index.html'));
  }
});

app.listen(Number(port), '0.0.0.0', () => {
  console.log(`API 服务已启动: http://0.0.0.0:${port}`);
  console.log(`正在监听目录: ${danmakuDir}`);
  
  // 启动头像自动更新服务
  startAvatarScheduler();
});

// 管理接口：手动初始化数据库
app.post('/api/admin/init-db', async (req, res) => {
  try {
    await initDb();
    res.json({ message: '数据库初始化成功' });
  } catch (error: any) {
    console.error('手动初始化数据库失败:', error);
    res.status(500).json({ error: error.message || '数据库初始化失败' });
  }
});

// 管理接口：手动触发全量扫描
app.post('/api/admin/scan', async (req, res) => {
  try {
    const danmakuDir = process.env.DANMAKU_DIR 
      ? path.resolve(process.env.DANMAKU_DIR)
      : path.resolve(__dirname, '../data/danmaku');
    
    console.log(`收到手动扫描请求: ${danmakuDir}`);
    // 异步执行扫描，不阻塞响应
    scanDirectory(danmakuDir).then(count => {
      console.log(`手动扫描完成，新增 ${count} 个文件`);
    }).catch(err => {
      console.error('手动扫描失败:', err);
    });

    res.json({ message: '扫描任务已在后台启动' });
  } catch (error: any) {
    res.status(500).json({ error: error.message || '扫描启动失败' });
  }
});
