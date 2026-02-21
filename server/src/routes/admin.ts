import express from 'express';
import { getRooms, addRoom, updateRoom, deleteRoom, getRoomById } from '../processor.js';
import path from 'node:path';
import { startRecorder, stopRecorder, restartRecorder, getProcessStatus } from '../pm2_manager.js';

const router = express.Router();

// 简单的 Token 验证中间件
const authMiddleware = (req: express.Request, res: express.Response, next: express.NextFunction) => {
  const token = req.headers['authorization'] || req.query.token;
  const adminToken = process.env.ADMIN_TOKEN;
  
  // 安全检查：必须配置 ADMIN_TOKEN，且不能为空
  // 防止因环境变量未加载导致 adminToken 为 undefined，从而与未传 token 的请求 (undefined) 匹配，造成鉴权绕过
  if (!adminToken || adminToken.trim() === '') {
    console.error('Security Error: ADMIN_TOKEN is not set in environment variables.');
    return res.status(500).json({ error: 'Server configuration error: ADMIN_TOKEN missing' });
  }
  
  if (token === adminToken || `Bearer ${adminToken}` === token) {
    next();
  } else {
    res.status(401).json({ error: 'Unauthorized' });
  }
};

router.use(authMiddleware);

// 获取所有直播间及状态
router.get('/rooms', async (req, res) => {
  try {
    const rooms = await getRooms();
    
    // 获取 PM2 进程状态
    const processes = await getProcessStatus();
    
    const result = rooms.map((room: any) => {
      const procName = `danmu-${room.name || room.room_id}`;
      const proc = processes.find(p => p.name === procName);
      
      return {
        ...room,
        process_status: proc ? proc.pm2_env?.status : 'stopped',
        process_uptime: proc ? proc.pm2_env?.pm_uptime : 0,
        pid: proc ? proc.pid : null
      };
    });
    
    res.json(result);
  } catch (error: any) {
    res.status(500).json({ error: error.message });
  }
});

// 添加直播间
router.post('/rooms', async (req, res) => {
  const { roomId, name, uid } = req.body;
  
  if (!roomId || !name) {
    return res.status(400).json({ error: 'Missing roomId or name' });
  }

  try {
    // 1. 存入数据库
    await addRoom({ roomId, name, uid });
    
    // 2. 启动 PM2 进程
    await startRecorder(roomId, name);
    
    res.json({ success: true });
  } catch (error: any) {
    res.status(500).json({ error: error.message });
  }
});

// 停止/删除直播间
router.delete('/rooms/:id', async (req, res) => {
  try {
    const room = await getRoomById(parseInt(req.params.id));
    if (!room) return res.status(404).json({ error: 'Room not found' });
    
    // 1. 停止并删除 PM2 进程
    await stopRecorder(room.name || room.room_id.toString());
    
    // 2. 删除数据库记录
    await deleteRoom(parseInt(req.params.id));
    
    res.json({ success: true });
  } catch (error: any) {
    res.status(500).json({ error: error.message });
  }
});

// 重启进程
router.post('/rooms/:id/restart', async (req, res) => {
  try {
    const room = await getRoomById(parseInt(req.params.id));
    if (!room) return res.status(404).json({ error: 'Room not found' });
    
    await restartRecorder(room.room_id, room.name);
    
    res.json({ success: true });
  } catch (error: any) {
    res.status(500).json({ error: error.message });
  }
});

export default router;
