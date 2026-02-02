import express from 'express';
import cors from 'cors';
import 'dotenv/config';
import path from 'node:path';
import fs from 'node:fs';
import { fileURLToPath } from 'node:url';
import chokidar from 'chokidar';
import { exec } from 'node:child_process';
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

// 获取 PM2 进程状态
app.get('/api/status', (req, res) => {
  // 使用 exec 执行 npx pm2 jlist 获取 JSON 格式的进程列表
  // 这种方式比 pm2 库连接更可靠，因为它直接复用了 shell 环境
  exec('npx pm2 jlist', (error: Error | null, stdout: string, stderr: string) => {
    if (error) {
      console.error('PM2 Exec Error:', error);
      console.error('Stderr:', stderr);
      return res.status(500).json({ error: '无法获取进程列表' });
    }

    try {
      const list = JSON.parse(stdout);
      const status = list.map((proc: any) => ({
        name: proc.name,
        status: proc.pm2_env?.status,
        cpu: proc.monit?.cpu,
        memory: proc.monit?.memory,
        uptime: proc.pm2_env?.pm_uptime,
        id: proc.pm_id
      }));
      res.json(status);
    } catch (parseError) {
      console.error('PM2 Parse Error:', parseError);
      res.status(500).json({ error: '解析进程数据失败' });
    }
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
});
