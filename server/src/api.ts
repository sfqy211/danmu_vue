import express from 'express';
import cors from 'cors';
import 'dotenv/config';
import path from 'node:path';
import fs from 'node:fs';
import { fileURLToPath } from 'node:url';
import chokidar from 'chokidar';
import { getSessions, dbGet, getStreamers, processDanmakuFile, scanDirectory, getSessionDanmakuPaged } from './processor.js';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const app = express();
const port = process.env.PORT || 3001;

app.use(cors());
app.use(express.json());

// 托管前端静态文件
// 优先使用 Vue 构建后的 dist 目录，如果不存在则回退到 public
const distPath = path.resolve(__dirname, '../../dist');
const publicPath = path.resolve(__dirname, '../../public');
const staticPath = fs.existsSync(distPath) ? distPath : publicPath;
console.log(`Serving static files from: ${staticPath}`);

app.use(express.static(staticPath));

// 获取所有录制列表 (支持筛选)
app.get('/api/sessions', async (req, res) => {
  try {
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
  // depth: 0, // Removed to allow recursion for subdirectories
  awaitWriteFinish: {
    stabilityThreshold: 2000,
    pollInterval: 100
  }
});

watcher.on('add', async (filePath) => {
  if (filePath.endsWith('.xml')) {
    console.log(`检测到新文件: ${filePath}`);
    await processDanmakuFile(filePath);
  }
});

// 手动触发目录扫描
app.post('/api/scan', async (req, res) => {
  try {
    // const danmakuDir = path.resolve(__dirname, '../data/danmaku'); // Use global danmakuDir
    const count = await scanDirectory(danmakuDir);
    res.json({ success: true, message: `扫描完成，新增 ${count} 条记录` });
  } catch (error) {
    console.error('API Error /api/scan:', error);
    res.status(500).json({ error: '扫描失败' });
  }
});

// SPA Fallback: 将所有非 API 请求重定向到 index.html
app.get('*', (req, res) => {
  if (!req.path.startsWith('/api/')) {
    res.sendFile(path.join(staticPath, 'index.html'));
  }
});

app.listen(Number(port), '0.0.0.0', () => {
  console.log(`API 服务已启动: http://0.0.0.0:${port}`);
  console.log(`正在监听目录: ${danmakuDir}`);
});
