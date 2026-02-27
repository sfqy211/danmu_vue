<template>
  <div class="home-container">
    <!-- ===== 顶部导航栏 ===== -->
    <TopNav />

    <!-- ===== 主内容区 ===== -->
    <main class="main-content">
      <!-- 主角卡片 -->
      <div
        class="profile-card"
        :key="currentVupData.uid"
        ref="profileCardRef"
        :style="cardStyle"
        @mousemove="handleCardMove"
        @mouseleave="resetCardTilt"
      >
        <!-- 信息区 -->
        <div class="info-section">
          <!-- 名称 + 状态 -->
          <div class="name-row">
            <div class="avatar-wrapper" @click="openLivestream(currentVupData.livestreamUrl)">
              <img :src="currentVupData.avatarUrl" class="profile-avatar" />
              <div class="avatar-hover-mask">
                <el-icon><VideoPlay /></el-icon>
              </div>
            </div>
            <div class="name-block">
              <h1 class="profile-name">{{ currentVupData.name }}</h1>
              <div class="status-tags">
                <span v-if="currentVupData.hasMonitor" class="tag tag-monitor">
                  <el-icon><DataLine /></el-icon> 弹幕监控
                </span>
                <span v-if="currentVupData.isLiving" class="tag tag-live">
                  <span class="live-dot"></span> 直播中
                </span>
              </div>
            </div>
          </div>

          <!-- 扩展信息行（预留，无数据时显示骨架） -->
          <div class="ext-info-grid">
            <div class="ext-info-item">
              <span class="ext-label">粉丝数</span>
              <span class="ext-value" v-if="currentVupData.followers != null">
                {{ formatNumber(currentVupData.followers) }}
              </span>
              <span class="ext-value placeholder" v-else>—</span>
            </div>
            <div class="ext-info-item">
              <span class="ext-label">舰长数</span>
              <span class="ext-value" v-if="currentVupData.guardNum != null">
                {{ currentVupData.guardNum }}
              </span>
              <span class="ext-value placeholder" v-else>—</span>
            </div>

            <div class="ext-info-item">
              <span class="ext-label">视频数</span>
              <span class="ext-value" v-if="currentVupData.videoCount != null">
                {{ currentVupData.videoCount }}
              </span>
              <span class="ext-value placeholder" v-else>—</span>
            </div>
            <div class="ext-info-item">
              <span class="ext-label">最近直播</span>
              <span class="ext-value" v-if="currentVupData.lastLiveTime">
                {{ formatRelativeTime(currentVupData.lastLiveTime) }}
              </span>
              <span class="ext-value placeholder" v-else>—</span>
            </div>
          </div>

          <!-- 操作按钮组 -->
          <div class="action-grid">
            <!-- 弹幕历史：仅限有监控的用户 -->
            <template v-if="currentVupData.hasMonitor">
              <button class="action-card" @click="navigateTo(currentVupData.uid, 'danmaku')">
                <el-icon class="action-icon"><ChatDotRound /></el-icon>
                <div class="action-text">
                  <span class="action-title">弹幕历史</span>
                  <span class="action-desc">查看历史弹幕记录</span>
                </div>
                <el-icon class="action-arrow"><ArrowRight /></el-icon>
              </button>
              <button class="action-card" @click="navigateTo(currentVupData.uid, 'songs')">
                <el-icon class="action-icon"><Headset /></el-icon>
                <div class="action-text">
                  <span class="action-title">点歌历史</span>
                  <span class="action-desc">查看点歌记录</span>
                </div>
                <el-icon class="action-arrow"><ArrowRight /></el-icon>
              </button>
            </template>

            <!-- 通用外链 -->
            <a :href="currentVupData.homepageUrl" target="_blank" class="action-card">
              <el-icon class="action-icon"><User /></el-icon>
              <div class="action-text">
                <span class="action-title">B站主页</span>
                <span class="action-desc">查看个人空间</span>
              </div>
              <el-icon class="action-arrow"><ArrowRight /></el-icon>
            </a>
            <a
              v-if="currentVupData.playlistUrl"
              :href="currentVupData.playlistUrl"
              target="_blank"
              class="action-card"
            >
              <el-icon class="action-icon"><Promotion /></el-icon>
              <div class="action-text">
                <span class="action-title">歌单</span>
                <span class="action-desc">查看歌单列表</span>
              </div>
              <el-icon class="action-arrow"><ArrowRight /></el-icon>
            </a>
          </div>

          <!-- 分组标签 -->
          <div class="group-tags">
            <span v-for="g in currentVupData.groups" :key="g" class="group-tag">{{ g }}</span>
          </div>
        </div>
      </div>

    </main>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, watch, onUnmounted, reactive } from 'vue';
import { useRouter, useRoute } from 'vue-router';
import TopNav from '../components/TopNav.vue';
import { VUP_LIST } from '../constants/vups';
import { useDanmakuStore } from '../stores/danmakuStore';
import {
  ChatDotRound, Headset, ArrowRight, User,
  Promotion, VideoPlay, DataLine
} from '@element-plus/icons-vue';

const router = useRouter();
const route = useRoute();
const store = useDanmakuStore();

// 本地存储 VUP 扩展数据
const vupDataMap = reactive<Record<string, any>>({});

const mql = window.matchMedia('(max-width: 768px)');
const isMobile = ref(mql.matches);

const updateMobile = (e: MediaQueryListEvent) => {
  isMobile.value = e.matches;
  if (isMobile.value) {
    resetCardTilt();
  }
};

onMounted(() => {
  mql.addEventListener('change', updateMobile);
});

onUnmounted(() => {
  mql.removeEventListener('change', updateMobile);
});

const activeStreamer = computed(() => store.currentVup);

// 合并静态配置和动态获取的数据
const currentVupData = computed(() => {
  const staticData = activeStreamer.value;
  const dynamicData = vupDataMap[staticData.uid] || {};
  return {
    ...staticData,
    ...dynamicData
  };
});



const showMeshGradient = computed(() => {
  return isMobile.value && currentVupData.value.themeColors && currentVupData.value.themeColors.length >= 3;
});

const meshColors = computed(() => {
  const colors = currentVupData.value.themeColors || [];
  if (colors.length >= 9) {
    // Return first 5 colors or a mix if needed
    return [colors[0], colors[1], colors[2], colors[3], colors[4]];
  }
  // Fallback: repeat colors if less than 5
  return [
    colors[0], 
    colors[1], 
    colors[2], 
    colors[0], 
    colors[1]
  ];
});

const bgImageStyle = computed(() => {
  const imgUrl = currentVupData.value.coverUrl || currentVupData.value.imageUrl;
  return {
    backgroundImage: `url(${imgUrl})`
  };
});

// 获取 VUP 详细数据 (调用 vtbs.moe API)
const fetchVupData = async (uid: string) => {
  if (vupDataMap[uid]) return; // 如果已有缓存数据则不重复请求
  
  try {
    // 使用 api.md 中提到的接口: /v1/detail/:mid
    // 注意：实际请求可能会遇到跨域问题，这里仅作为接口预留
    const res = await fetch(`https://api.vtbs.moe/v1/detail/${uid}`);
    if (res.ok) {
      const data = await res.json();
      vupDataMap[uid] = {
        followers: data.follower,
        guardNum: data.guardNum,
        archiveView: data.archiveView,
        videoCount: data.video,
        online: data.online, // 在线人气，通常直播时才有
        lastLiveTime: data.lastLive?.time,
        // liveStatus: 0 下播, 1 直播
        isLiving: data.liveStatus === 1
      };
    }
  } catch (error) {
    console.warn(`Failed to fetch data for uid ${uid}:`, error);
    // 出错时可以保持默认值或设置特定状态
  }
};

// 监听 activeStreamer 变化，获取数据
watch(() => activeStreamer.value.uid, (newUid) => {
  fetchVupData(newUid);
}, { immediate: true });

const profileCardRef = ref<HTMLElement | null>(null);
const tiltX = ref(0);
const tiltY = ref(0);
const glareX = ref(50);
const glareY = ref(50);
const glareOpacity = ref(0);
let rafId = 0;
const pointer = { x: 0, y: 0 };

const cardStyle = computed<Record<string, string>>(() => ({
  '--tilt-x': `${tiltX.value}deg`,
  '--tilt-y': `${tiltY.value}deg`,
  '--glare-x': `${glareX.value}%`,
  '--glare-y': `${glareY.value}%`,
  '--glare-opacity': `${glareOpacity.value}`
}));

const handleCardMove = (e: MouseEvent) => {
  if (isMobile.value) return;
  pointer.x = e.clientX;
  pointer.y = e.clientY;
  if (rafId) return;
  rafId = requestAnimationFrame(() => {
    rafId = 0;
    const card = profileCardRef.value;
    if (!card) return;
    const rect = card.getBoundingClientRect();
    const x = Math.min(1, Math.max(0, (pointer.x - rect.left) / rect.width));
    const y = Math.min(1, Math.max(0, (pointer.y - rect.top) / rect.height));
    const rotateY = (x - 0.5) * 2;
    const rotateX = (0.5 - y) * 2;
    const distLeft = x;
    const distRight = 1 - x;
    const distTop = y;
    const distBottom = 1 - y;
    const minEdge = Math.min(distLeft, distRight, distTop, distBottom);
    let edgeX = 50;
    let edgeY = 50;
    if (minEdge === distLeft) {
      edgeX = 0;
      edgeY = y * 100;
    } else if (minEdge === distRight) {
      edgeX = 100;
      edgeY = y * 100;
    } else if (minEdge === distTop) {
      edgeX = x * 100;
      edgeY = 0;
    } else {
      edgeX = x * 100;
      edgeY = 100;
    }
    const edgeStrength = Math.max(0, 1 - minEdge * 3.2);
    tiltX.value = rotateX;
    tiltY.value = rotateY;
    glareX.value = Math.max(0, Math.min(100, edgeX));
    glareY.value = Math.max(0, Math.min(100, edgeY));
    glareOpacity.value = 0.06 + edgeStrength * 0.2;
  });
};

const resetCardTilt = () => {
  tiltX.value = 0;
  tiltY.value = 0;
  glareX.value = 50;
  glareY.value = 50;
  glareOpacity.value = 0;
};

const navigateTo = (uid: string, type: 'danmaku' | 'songs') => {
  if (type === 'danmaku') {
    router.push(`/vup/${uid}`);
  } else {
    router.push(`/vup/${uid}/songs`);
  }
};

const openLivestream = (url: string) => {
  if (url) window.open(url, '_blank');
};

const formatNumber = (n: number): string => {
  if (n >= 10000) return (n / 10000).toFixed(1) + '万';
  return n.toString();
};

const formatRelativeTime = (ts: number): string => {
  const diff = Date.now() - ts;
  const d = Math.floor(diff / 86400000);
  if (d === 0) return '今天';
  if (d === 1) return '昨天';
  if (d < 30) return `${d}天前`;
  if (d < 365) return `${Math.floor(d / 30)}个月前`;
  return `${Math.floor(d / 365)}年前`;
};
</script>

<style scoped>
/* ===== 布局基础 ===== */
.home-container {
  min-height: 100vh;
  width: 100%;
  position: relative;
  /* background-color: var(--bg-primary, #0f0f13); */
  overflow-x: hidden;
}

/* ===== 主内容 ===== */
.main-content {
  position: relative;
  z-index: 2;
  display: flex;
  flex-direction: column;
  height: 100vh;
  padding-top: 72px;
  padding-bottom: 20px;
  width: 100%;
  margin: 0 auto;
  padding-left: 20px;
  padding-right: 20px;
  box-sizing: border-box;
}

/* ===== 主角卡片 ===== */
.profile-card {
  --tilt-x: 0deg;
  --tilt-y: 0deg;
  --glare-x: 50%;
  --glare-y: 50%;
  --glare-opacity: 0;
  position: relative;
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
  /* 优化：背景调黑一点点，增强对比度，但保持通透 */
  background: linear-gradient(140deg, rgba(0, 0, 0, 0.15), rgba(0, 0, 0, 0.05));
  backdrop-filter: blur(20px);
  -webkit-backdrop-filter: blur(20px);
  border: 1px solid rgba(255, 255, 255, 0.15);
  border-radius: 24px;
  padding: clamp(20px, 4vw, 40px);
  animation: cardIn 0.4s ease;
  overflow: hidden;
  max-width: clamp(1000px, 80vw, 1600px);
  margin: 0 auto;
  width: 100%;
  box-shadow: 0 12px 36px rgba(0, 0, 0, 0.12), inset 0 1px 0 rgba(255, 255, 255, 0.1);
  transform: perspective(1200px) rotateX(var(--tilt-x)) rotateY(var(--tilt-y));
  transform-style: preserve-3d;
  transition: transform 0.12s ease, box-shadow 0.2s ease, border-color 0.2s ease, background 0.2s ease;
  will-change: transform;
}

.profile-card::before {
  content: '';
  position: absolute;
  inset: -1px;
  border-radius: inherit;
  background: radial-gradient(420px 180px at var(--glare-x) var(--glare-y), rgba(255, 255, 255, 0.32), rgba(255, 255, 255, 0) 60%);
  opacity: var(--glare-opacity);
  pointer-events: none;
  mix-blend-mode: screen;
  z-index: 0;
}

.profile-card::after {
  content: '';
  position: absolute;
  inset: 0;
  border-radius: inherit;
  background: linear-gradient(135deg, rgba(255, 255, 255, 0.22), rgba(255, 255, 255, 0) 42%, rgba(255, 255, 255, 0.12) 62%, rgba(255, 255, 255, 0));
  opacity: 0.22;
  pointer-events: none;
  z-index: 0;
}

.profile-card > * {
  position: relative;
  z-index: 1;
}

@keyframes cardIn {
  from { opacity: 0; transform: translateY(16px); }
  to   { opacity: 1; transform: translateY(0); }
}

/* 封面区 */
.cover-section {
  position: relative;
  border-radius: 16px;
  overflow: hidden;
  cursor: pointer;
  box-shadow: 0 16px 48px rgba(0, 0, 0, 0.45);
  align-self: stretch;
  aspect-ratio: 1 / 1;
  min-height: clamp(400px, 40vw, 600px);
}

.cover-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  transition: transform 0.4s ease;
  display: block;
}

.cover-hover-mask {
  position: absolute;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 8px;
  color: white;
  font-size: 14px;
  opacity: 0;
  transition: opacity 0.3s ease;
}
.cover-section:hover .cover-hover-mask { opacity: 1; }
.cover-section:hover .cover-img { transform: scale(1.05); }

.play-icon {
  font-size: 40px;
}

/* 信息区 */
.info-section {
  display: flex;
  flex-direction: column;
  gap: 18px;
  overflow-y: auto;
  min-height: 0;
}

.name-row {
  display: flex;
  align-items: center;
  gap: 14px;
}

.profile-avatar {
  width: 100%;
  height: 100%;
  object-fit: cover;
  display: block;
}

.avatar-wrapper {
   position: relative;
   width: 80px;
   height: 80px;
   border-radius: 50%;
   overflow: hidden;
   cursor: pointer;
   flex-shrink: 0;
   transition: transform 0.3s ease;
 }

.avatar-wrapper:hover {
  transform: scale(1.05);
}

.avatar-hover-mask {
  position: absolute;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  color: white;
  font-size: 12px;
  opacity: 0;
  transition: opacity 0.3s ease;
}

.avatar-wrapper:hover .avatar-hover-mask {
  opacity: 1;
}

.avatar-hover-mask .el-icon {
  font-size: 24px;
  margin-bottom: 2px;
}

.profile-name {
  color: white;
  font-size: 1.6rem;
  font-weight: 700;
  margin: 0 0 6px;
  letter-spacing: 1px;
  text-shadow: 0 2px 4px rgba(0, 0, 0, 0.6);
}

.status-tags {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}

.tag {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: 13px;
  padding: 4px 10px;
  border-radius: 20px;
  font-weight: 600;
  text-shadow: 0 1px 2px rgba(0, 0, 0, 0.2);
}

.tag-monitor {
  background: rgba(103, 194, 58, 0.15);
  color: #95d475;
  border: 1px solid rgba(103, 194, 58, 0.3);
  text-shadow: 0 1px 2px rgba(0, 0, 0, 0.3);
}

.tag-live {
  background: rgba(245, 108, 108, 0.15);
  color: #f89898;
  border: 1px solid rgba(245, 108, 108, 0.3);
  text-shadow: 0 1px 2px rgba(0, 0, 0, 0.3);
}

.live-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: #f56c6c;
  animation: livePulse 1.2s infinite;
}

@keyframes livePulse {
  0%, 100% { opacity: 1; transform: scale(1); }
  50% { opacity: 0.5; transform: scale(1.3); }
}

/* 扩展信息网格 */
.ext-info-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
  gap: 12px;
}

.ext-info-item {
  background: linear-gradient(160deg, rgba(255, 255, 255, 0.05), rgba(255, 255, 255, 0.01));
  border: 1px solid rgba(255, 255, 255, 0.12);
  backdrop-filter: blur(12px);
  -webkit-backdrop-filter: blur(12px);
  border-radius: 12px;
  padding: 12px 16px;
  display: flex;
  flex-direction: column;
  gap: 4px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.05), inset 0 1px 0 rgba(255, 255, 255, 0.1);
}

.ext-info-wide {
  grid-column: span 1;
}

.ext-label {
  font-size: 13px;
  color: rgba(255, 255, 255, 0.75);
  text-transform: uppercase;
  letter-spacing: 0.5px;
  text-shadow: 0 1px 2px rgba(0, 0, 0, 0.2);
}

.ext-value {
  font-size: 18px;
  color: #ffffff;
  font-weight: 600;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  text-shadow: 0 1px 3px rgba(0, 0, 0, 0.4);
}

.ext-value.placeholder {
  color: rgba(255, 255, 255, 0.4);
}

.ext-link {
  text-decoration: none;
  color: #79bbff;
  transition: color 0.2s;
}
.ext-link:hover { color: #409eff; }

/* 操作按钮组 */
.action-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
  gap: 12px;
}

.action-card {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 12px 14px;
  border-radius: 14px;
  background: linear-gradient(145deg, rgba(255, 255, 255, 0.08), rgba(255, 255, 255, 0.02));
  border: 1px solid rgba(255, 255, 255, 0.12);
  backdrop-filter: blur(12px);
  -webkit-backdrop-filter: blur(12px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.05), inset 0 1px 0 rgba(255, 255, 255, 0.1);
  color: #ffffff;
  cursor: pointer;
  transition: transform 0.25s ease, background 0.25s ease, border-color 0.25s ease, box-shadow 0.25s ease;
  text-decoration: none;
}

.action-card:hover:not(.disabled) {
  background: linear-gradient(145deg, rgba(255, 255, 255, 0.15), rgba(255, 255, 255, 0.05));
  border-color: rgba(255, 255, 255, 0.25);
  transform: translateY(-1px);
  box-shadow: 0 6px 16px rgba(0, 0, 0, 0.1), inset 0 1px 0 rgba(255, 255, 255, 0.2);
}

.action-card.disabled {
  opacity: 0.35;
  cursor: not-allowed;
}

.action-icon {
  font-size: 20px;
  flex-shrink: 0;
  color: #ffffff;
  filter: drop-shadow(0 2px 4px rgba(0,0,0,0.2));
}

.action-text {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 2px;
  text-align: left;
}

.action-title {
  font-size: 15px;
  font-weight: 700;
  color: #ffffff;
  text-shadow: 0 1px 3px rgba(0, 0, 0, 0.3);
}

.action-desc {
  font-size: 12px;
  color: rgba(255, 255, 255, 0.8);
  text-shadow: 0 1px 2px rgba(0, 0, 0, 0.2);
}

.action-arrow {
  font-size: 14px;
  color: rgba(255, 255, 255, 0.3);
  flex-shrink: 0;
}

/* 分组标签 */
.group-tags {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}

.group-tag {
  font-size: 13px;
  padding: 5px 12px;
  border-radius: 20px;
  background: rgba(255, 255, 255, 0.1);
  color: #ffffff;
  border: 1px solid rgba(255, 255, 255, 0.2);
  backdrop-filter: blur(10px);
  -webkit-backdrop-filter: blur(10px);
  text-shadow: 0 1px 2px rgba(0, 0, 0, 0.3);
  box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.1);
  font-weight: 500;
}

/* ===== 响应式 ===== */
@media (max-width: 768px) {
  .nav-tabs { display: none; }
  .hamburger { display: flex; }

  .main-content {
    padding-top: 68px;
    padding-left: 14px;
    padding-right: 14px;
  }

  .profile-card {
    grid-template-columns: 1fr;
    padding: 18px;
    gap: 18px;
    /* 移动端性能优化：移除实时高斯模糊，改用微透的纯色背景 */
    backdrop-filter: none !important;
    -webkit-backdrop-filter: none !important;
    background: rgba(255, 255, 255, 0.12) !important;
    border: 1px solid rgba(255, 255, 255, 0.2);
    box-shadow: 0 4px 20px rgba(0, 0, 0, 0.08);
    transform: none !important;
    will-change: auto !important;
  }
  
  /* 同样优化子元素 */
  .ext-info-item,
  .action-card,
  .group-tag {
    backdrop-filter: none !important;
    -webkit-backdrop-filter: none !important;
  }
  
  .ext-info-item,
  .action-card {
    background: rgba(255, 255, 255, 0.15) !important;
    border: 1px solid rgba(255, 255, 255, 0.25);
  }
  
  .group-tag {
    background: rgba(255, 255, 255, 0.2) !important;
  }

  .cover-section {
    max-width: 320px;
    max-height: 320px;
    aspect-ratio: 1 / 1;
    margin: 0 auto;
  }

  .ext-info-grid {
    grid-template-columns: 1fr 1fr;
  }

  .action-grid {
    grid-template-columns: 1fr;
  }

}

@media (max-width: 480px) {
  .profile-name { font-size: 1.3rem; }
  .ext-info-grid { grid-template-columns: 1fr 1fr; }
  .mobile-menu { grid-template-columns: 1fr; }
}

</style>

