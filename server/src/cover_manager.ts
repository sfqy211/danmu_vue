import fs from 'node:fs';
import path from 'node:path';
import axios from 'axios';
import sharp from 'sharp';
import { fileURLToPath } from 'node:url';
import { getRooms } from './processor.js';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// Server storage for auto-fetched covers
// 对应 server/data/cover
const SERVER_COVER_DIR = path.resolve(__dirname, '../data/cover');
// Public directory for frontend build
// 对应 public/vup-cover
const PUBLIC_COVER_DIR = path.resolve(__dirname, '../../public/vup-cover');

// Ensure directories exist
if (!fs.existsSync(SERVER_COVER_DIR)) {
  fs.mkdirSync(SERVER_COVER_DIR, { recursive: true });
}
if (!fs.existsSync(PUBLIC_COVER_DIR)) {
  fs.mkdirSync(PUBLIC_COVER_DIR, { recursive: true });
}

// VUP 列表 (硬编码，确保能覆盖 constants/vups.ts 中的所有主播)
const VUP_LIST = [
  { uid: '1104048496', name: '桃几OvO', room_id: 22642754 },
  { uid: '4718716', name: '鱼鸽鸽', room_id: 673 },
  { uid: '3493271057730096', name: '妮莉安Lily', room_id: 27484357 },
  { uid: '17967817', name: '大哥L-', room_id: 443197 },
  { uid: '15641218', name: '帅比笙歌超可爱OvO', room_id: 545 },
  { uid: '1376650682', name: '葡冷尔子gagako', room_id: 22857429 },
  { uid: '7591465', name: '里奈Rina', room_id: 873642 },
  { uid: '390647282', name: '浅野天琪_TANCHJIM', room_id: 21465419 },
  { uid: '188679', name: 'Niya阿布', room_id: 685026 },
  { uid: '128667389', name: '-蔻蔻CC-', room_id: 23587427 },
  { uid: '703018634', name: '莱妮娅_Rynia', room_id: 54363 },
  { uid: '90873', name: '内德维德', room_id: 5424 },
  { uid: '1112031857', name: '薇Steria', room_id: 22924075 },
  { uid: '121309', name: 'CODE-V', room_id: 858080 },
  { uid: '796556', name: '-菫時-', room_id: 3473884 }
];

// 获取单个直播间封面
const fetchCover = async (room: any) => {
  const { room_id, name } = room;
  let uid = room.uid;

  try {
    console.log(`[Cover] 正在获取 ${name} (${room_id}) 的封面...`);
    
    // 获取直播间信息
    const infoUrl = `https://api.live.bilibili.com/room/v1/Room/get_info?room_id=${room_id}`;
    
    const res = await axios.get(infoUrl, {
      headers: {
        'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36',
        'Referer': `https://live.bilibili.com/${room_id}`
      }
    });

    if (res.data.code !== 0) {
      console.error(`[Cover] API 错误 ${name}: ${res.data.message || res.data.msg}`);
      return;
    }

    const roomInfo = res.data.data;
    // 优先顺序: user_cover (用户上传封面) -> cover (直播封面) -> keyframe (关键帧)
    const coverUrl = roomInfo.user_cover || roomInfo.cover || roomInfo.keyframe;
    
    // 如果数据库中没有 UID，尝试从 API 获取并使用
    if (!uid && roomInfo.uid) {
        uid = roomInfo.uid.toString();
    }
    
    if (!coverUrl) {
      console.warn(`[Cover] 未找到封面 URL: ${name} (Res: ${JSON.stringify(roomInfo).substring(0, 100)}...)`);
      return;
    }

    // 下载图片
    const response = await axios.get(coverUrl, { responseType: 'arraybuffer' });
    const buffer = Buffer.from(response.data);

    // 统一转换为 PNG 格式
    const filenameBase = uid ? uid.toString() : room_id.toString();
    const filename = `${filenameBase}.png`;
    
    const serverPath = path.join(SERVER_COVER_DIR, filename);
    const publicPath = path.join(PUBLIC_COVER_DIR, filename);

    // 保存到 server/data/cover
    await sharp(buffer)
      .png()
      .toFile(serverPath);
    console.log(`[Cover] 已保存到服务端: ${serverPath}`);

    // 复制到 public/vup-cover
    fs.copyFileSync(serverPath, publicPath);
    console.log(`[Cover] 已同步到前端目录: ${publicPath}`);

  } catch (error: any) {
    console.error(`[Cover] 获取失败 ${name}:`, error.message);
  }
};

// 批量更新任务
const runUpdateTask = async () => {
  console.log('[Cover] 开始封面更新任务...');
  try {
    // 1. 获取数据库中的房间
    const dbRooms = await getRooms();
    
    // 2. 合并 VUP_LIST 和 dbRooms (以 room_id 为键去重)
    const roomMap = new Map();
    
    // 先加入 VUP_LIST (优先级较低，如果 DB 有更新的信息则覆盖？或者 VUP_LIST 优先？)
    // 通常硬编码列表比较准确，作为基础
    VUP_LIST.forEach(vup => {
      roomMap.set(vup.room_id, { ...vup, room_id: vup.room_id });
    });
    
    // 再合并 DB 数据
    dbRooms.forEach((room: any) => {
      // 数据库里的 room_id 可能是数字或字符串，统一转数字
      const rid = parseInt(room.room_id);
      if (roomMap.has(rid)) {
        // 如果已存在，可以选择合并信息
        const existing = roomMap.get(rid);
        roomMap.set(rid, { ...existing, ...room, room_id: rid });
      } else {
        roomMap.set(rid, { ...room, room_id: rid });
      }
    });

    const allRooms = Array.from(roomMap.values());
    console.log(`[Cover] 待更新房间数: ${allRooms.length}`);

    for (const room of allRooms) {
      await fetchCover(room);
      // 间隔 30 秒，与头像获取保持一致
      await new Promise(resolve => setTimeout(resolve, 30000));
    }
    console.log('[Cover] 封面更新任务完成');
  } catch (e: any) {
    console.error('[Cover] 获取房间列表失败:', e.message);
  }
};

// 启动调度器
export const startCoverScheduler = () => {
  // 启动 15 秒后首次运行
  setTimeout(() => {
    runUpdateTask();
  }, 15000);

  // 每 24 小时执行一次
  setInterval(() => {
    runUpdateTask();
  }, 24 * 60 * 60 * 1000);
  
  console.log('[Cover] 封面自动更新调度器已启动 (周期: 24h)');
};