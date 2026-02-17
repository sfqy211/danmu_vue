import fs from 'node:fs';
import path from 'node:path';
import axios from 'axios';
import sharp from 'sharp';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// 对应前端 public/vup-bg
const BG_DIR = path.resolve(__dirname, '../../public/vup-bg');
// 对应前端 public/vup-avatar
const AVATAR_DIR = path.resolve(__dirname, '../../public/vup-avatar');

// VUP 列表 (需手动维护，或后续从数据库读取)
const VUPS = [
  { uid: '1104048496', name: '桃几OvO' },
  { uid: '4718716', name: '鱼鸽鸽' },
  { uid: '3493271057730096', name: '妮莉安Lily' },
  { uid: '17967817', name: '大哥L-' },
  { uid: '15641218', name: '帅比笙歌超可爱OvO' },
  { uid: '1376650682', name: '葡冷尔子gagako' },
  { uid: '7591465', name: '里奈Rina' },
  { uid: '390647282', name: '浅野天琪_TANCHJIM' },
  { uid: '188679', name: 'Niya阿布' },
  { uid: '128667389', name: '-蔻蔻CC-' },
  { uid: '703018634', name: '莱妮娅_Rynia' },
  { uid: '90873', name: '内德维德' },
  { uid: '1112031857', name: '薇Steria' },
  { uid: '121309', name: 'CODE-V' },
  { uid: '796556', name: '-菫時-' }
];

// 确保目录存在
if (!fs.existsSync(BG_DIR)) {
  fs.mkdirSync(BG_DIR, { recursive: true });
}
if (!fs.existsSync(AVATAR_DIR)) {
  fs.mkdirSync(AVATAR_DIR, { recursive: true });
}

// 获取单个 VUP 头像
const fetchAvatar = async (uid: string, name: string) => {
  try {
    console.log(`[Avatar] 正在获取 ${name} (${uid}) 的头像...`);
    // 改用 web-interface/card 接口，通常限制较少
    const infoUrl = `https://api.bilibili.com/x/web-interface/card?mid=${uid}`;
    
    // 1. 获取用户信息
    const res = await axios.get(infoUrl, {
      headers: {
        'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36',
        'Referer': `https://space.bilibili.com/${uid}`,
        'Origin': 'https://space.bilibili.com'
      }
    });

    if (res.data.code !== 0) {
      console.error(`[Avatar] API 错误 ${name}: ${res.data.message}`);
      return;
    }

    // card 接口的数据结构是 data.card.face
    const faceUrl = res.data.data?.card?.face;
    if (!faceUrl) {
      console.warn(`[Avatar] 未找到头像 URL: ${name}`);
      return;
    }

    // 2. 下载图片
    const response = await axios.get(faceUrl, { responseType: 'arraybuffer' });
    const buffer = Buffer.from(response.data);
    
    // 保存原图到 vup-bg
    const bgPath = path.join(BG_DIR, `${uid}.png`);
    fs.writeFileSync(bgPath, buffer);
    console.log(`[Avatar] 已更新 ${name} 的原图 -> ${bgPath}`);

    // 生成缩略图到 vup-avatar
    const avatarPath = path.join(AVATAR_DIR, `${uid}.png`);
    try {
        await sharp(buffer)
            .resize(200, 200, {
                fit: 'cover',
                position: 'center'
            })
            .toFile(avatarPath);
        console.log(`[Avatar] 已生成 ${name} 的缩略图 -> ${avatarPath}`);
    } catch (sharpError: any) {
        console.error(`[Avatar] 生成缩略图失败 ${name}:`, sharpError.message);
    }

  } catch (error: any) {
    console.error(`[Avatar] 获取失败 ${name}:`, error.message);
  }
};

// 批量更新任务
const runUpdateTask = async () => {
  console.log('[Avatar] 开始每日头像更新任务...');
  
  for (const vup of VUPS) {
    await fetchAvatar(vup.uid, vup.name);
    // 间隔 30 秒
    await new Promise(resolve => setTimeout(resolve, 30000));
  }
  
  console.log('[Avatar] 头像更新任务完成');
};

// 启动调度器
export const startAvatarScheduler = () => {
  // 立即运行一次（可选，防止第一次启动没有图片）
  // 为了不阻塞启动，延迟一点运行
  setTimeout(() => {
    runUpdateTask();
  }, 10000); // 启动 10秒后开始

  // 每天执行一次 (24小时 = 86400000 毫秒)
  setInterval(() => {
    runUpdateTask();
  }, 24 * 60 * 60 * 1000);
  
  console.log('[Avatar] 头像自动更新调度器已启动 (周期: 24h)');
};
