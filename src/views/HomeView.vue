<template>
  <div class="home-container">
    <!-- 动态背景层 -->
    <div
      class="dynamic-bg"
      :style="{ backgroundImage: `url(${activeStreamer.coverUrl || activeStreamer.imageUrl})` }"
    ></div>
    <div class="bg-overlay"></div>

    <!-- ===== 顶部导航栏 ===== -->
    <TopNav />

    <!-- ===== 主内容区 ===== -->
    <main class="main-content">
      <!-- 主角卡片 -->
      <div
        class="profile-card"
        :key="activeStreamer.uid"
        ref="profileCardRef"
        :style="cardStyle"
        @mousemove="handleCardMove"
        @mouseleave="resetCardTilt"
      >
        <!-- 信息区 -->
        <div class="info-section">
          <!-- 名称 + 状态 -->
          <div class="name-row">
            <div class="avatar-wrapper" @click="openLivestream(activeStreamer.livestreamUrl)">
              <img :src="activeStreamer.avatarUrl" class="profile-avatar" />
              <div class="avatar-hover-mask">
                <el-icon><VideoPlay /></el-icon>
              </div>
            </div>
            <div class="name-block">
              <h1 class="profile-name">{{ activeStreamer.name }}</h1>
              <div class="status-tags">
                <span v-if="activeStreamer.hasMonitor" class="tag tag-monitor">
                  <el-icon><DataLine /></el-icon> 弹幕监控
                </span>
                <span v-if="activeStreamer.isLiving" class="tag tag-live">
                  <span class="live-dot"></span> 直播中
                </span>
              </div>
            </div>
          </div>

          <!-- 扩展信息行（预留，无数据时显示骨架） -->
          <div class="ext-info-grid">
            <div class="ext-info-item">
              <span class="ext-label">粉丝数</span>
              <span class="ext-value" v-if="activeStreamer.followers != null">
                {{ formatNumber(activeStreamer.followers) }}
              </span>
              <span class="ext-value placeholder" v-else>—</span>
            </div>
            <div class="ext-info-item">
              <span class="ext-label">最近直播</span>
              <span class="ext-value" v-if="activeStreamer.lastLiveTime">
                {{ formatRelativeTime(activeStreamer.lastLiveTime) }}
              </span>
              <span class="ext-value placeholder" v-else>—</span>
            </div>
            <div class="ext-info-item ext-info-wide">
              <span class="ext-label">最新投稿</span>
              <a
                v-if="activeStreamer.latestVideo"
                :href="activeStreamer.latestVideoUrl"
                target="_blank"
                class="ext-value ext-link"
              >{{ activeStreamer.latestVideo }}</a>
              <span class="ext-value placeholder" v-else>—</span>
            </div>
          </div>

          <!-- 操作按钮组 -->
          <div class="action-grid">
            <!-- 弹幕历史：仅限有监控的用户 -->
            <template v-if="activeStreamer.hasMonitor">
              <button class="action-card" @click="navigateTo(activeStreamer.uid, 'danmaku')">
                <el-icon class="action-icon"><ChatDotRound /></el-icon>
                <div class="action-text">
                  <span class="action-title">弹幕历史</span>
                  <span class="action-desc">查看历史弹幕记录</span>
                </div>
                <el-icon class="action-arrow"><ArrowRight /></el-icon>
              </button>
              <button class="action-card" @click="navigateTo(activeStreamer.uid, 'songs')">
                <el-icon class="action-icon"><Headset /></el-icon>
                <div class="action-text">
                  <span class="action-title">点歌历史</span>
                  <span class="action-desc">查看点歌记录</span>
                </div>
                <el-icon class="action-arrow"><ArrowRight /></el-icon>
              </button>
            </template>

            <!-- 通用外链 -->
            <a :href="activeStreamer.homepageUrl" target="_blank" class="action-card">
              <el-icon class="action-icon"><User /></el-icon>
              <div class="action-text">
                <span class="action-title">B站主页</span>
                <span class="action-desc">查看个人空间</span>
              </div>
              <el-icon class="action-arrow"><ArrowRight /></el-icon>
            </a>
            <a
              v-if="activeStreamer.playlistUrl"
              :href="activeStreamer.playlistUrl"
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
            <span v-for="g in activeStreamer.groups" :key="g" class="group-tag">{{ g }}</span>
          </div>
        </div>
      </div>

    </main>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue';
import { useRouter, useRoute } from 'vue-router';
import TopNav from '../components/TopNav.vue';
import { VUP_LIST } from '../constants/vups';
import {
  ChatDotRound, Headset, ArrowRight, User,
  Promotion, VideoPlay, DataLine
} from '@element-plus/icons-vue';

const router = useRouter();
const route = useRoute();
const activeIndex = ref(0);

const checkSelectedStreamer = () => {
  const savedIndex = localStorage.getItem('selectedStreamerIndex');
  if (savedIndex !== null) {
    const index = parseInt(savedIndex, 10);
    if (index >= 0 && index < VUP_LIST.length) {
      activeIndex.value = index;
    }
  }
};

onMounted(() => {
  checkSelectedStreamer();
});

// 监听路由变化，当从列表页跳转到主页时检查选中的主播
watch(() => route.path, (newPath) => {
  if (newPath === '/') {
    checkSelectedStreamer();
  }
});

const activeStreamer = computed(() => VUP_LIST[activeIndex.value]);
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

const handleImageError = (e: Event, fallbackUrl: string) => {
  const img = e.target as HTMLImageElement;
  if (img.dataset.retried === 'true') return;
  img.dataset.retried = 'true';
  if (fallbackUrl && img.src !== fallbackUrl) {
    img.src = fallbackUrl;
  } else {
    img.style.display = 'none';
  }
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
  background-color: var(--bg-primary, #0f0f13);
  overflow-x: hidden;
}

/* ===== 动态背景 ===== */
.dynamic-bg {
  position: fixed;
  inset: 0;
  background-size: cover;
  background-position: center;
  filter: blur(4px) brightness(0.8) saturate(1.1);
  transform: scale(1.02);
  z-index: 0;
  transition: background-image 0.8s ease;
}

.bg-overlay {
  position: fixed;
  inset: 0;
  background: linear-gradient(
    180deg,
    rgba(0, 0, 0, 0.3) 0%,
    rgba(0, 0, 0, 0.1) 40%,
    rgba(0, 0, 0, 0.4) 100%
  );
  z-index: 1;
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
  background: linear-gradient(140deg, rgba(255, 255, 255, 0.12), rgba(255, 255, 255, 0.04));
  backdrop-filter: blur(8px);
  -webkit-backdrop-filter: blur(8px);
  border: 1px solid rgba(255, 255, 255, 0.24);
  border-radius: 24px;
  padding: 40px;
  animation: cardIn 0.4s ease;
  overflow: hidden;
  max-width: 1600px;
  margin: 0 auto;
  width: 100%;
  box-shadow: 0 12px 36px rgba(0, 0, 0, 0.12), inset 0 1px 0 rgba(255, 255, 255, 0.18);
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
  text-shadow: 0 2px 8px rgba(0, 0, 0, 0.4);
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
  font-size: 11px;
  padding: 3px 8px;
  border-radius: 20px;
  font-weight: 500;
  text-shadow: 0 1px 2px rgba(0, 0, 0, 0.2);
}

.tag-monitor {
  background: rgba(103, 194, 58, 0.15);
  color: #85d15e;
  border: 1px solid rgba(103, 194, 58, 0.3);
}

.tag-live {
  background: rgba(245, 108, 108, 0.15);
  color: #f56c6c;
  border: 1px solid rgba(245, 108, 108, 0.3);
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
  background: linear-gradient(160deg, rgba(255, 255, 255, 0.14), rgba(255, 255, 255, 0.05));
  border: 1px solid rgba(255, 255, 255, 0.18);
  backdrop-filter: blur(8px);
  -webkit-backdrop-filter: blur(8px);
  border-radius: 12px;
  padding: 12px 16px;
  display: flex;
  flex-direction: column;
  gap: 4px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.04), inset 0 1px 0 rgba(255, 255, 255, 0.15);
}

.ext-info-wide {
  grid-column: span 1;
}

.ext-label {
  font-size: 11px;
  color: rgba(255, 255, 255, 0.55);
  text-transform: uppercase;
  letter-spacing: 0.5px;
  text-shadow: 0 1px 2px rgba(0, 0, 0, 0.2);
}

.ext-value {
  font-size: 14px;
  color: #ffffff;
  font-weight: 500;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  text-shadow: 0 1px 3px rgba(0, 0, 0, 0.3);
}

.ext-value.placeholder {
  color: rgba(255, 255, 255, 0.25);
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
  background: linear-gradient(145deg, rgba(255, 255, 255, 0.2), rgba(255, 255, 255, 0.08));
  border: 1px solid rgba(255, 255, 255, 0.22);
  backdrop-filter: blur(12px);
  -webkit-backdrop-filter: blur(12px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.06), inset 0 1px 0 rgba(255, 255, 255, 0.25);
  color: #ffffff;
  cursor: pointer;
  transition: transform 0.25s ease, background 0.25s ease, border-color 0.25s ease, box-shadow 0.25s ease;
  text-decoration: none;
}

.action-card:hover:not(.disabled) {
  background: linear-gradient(145deg, rgba(255, 255, 255, 0.28), rgba(255, 255, 255, 0.12));
  border-color: rgba(255, 255, 255, 0.32);
  transform: translateY(-1px);
  box-shadow: 0 6px 16px rgba(0, 0, 0, 0.1), inset 0 1px 0 rgba(255, 255, 255, 0.35);
}

.action-card.disabled {
  opacity: 0.35;
  cursor: not-allowed;
}

.action-icon {
  font-size: 20px;
  flex-shrink: 0;
  color: #ffffff;
}

.action-text {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 2px;
  text-align: left;
}

.action-title {
  font-size: 13px;
  font-weight: 600;
  color: #ffffff;
  text-shadow: 0 1px 3px rgba(0, 0, 0, 0.3);
}

.action-desc {
  font-size: 11px;
  color: rgba(255, 255, 255, 0.65);
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
  font-size: 11px;
  padding: 3px 10px;
  border-radius: 20px;
  background: rgba(255, 255, 255, 0.2);
  color: #ffffff;
  border: 1px solid rgba(255, 255, 255, 0.3);
  backdrop-filter: blur(10px);
  -webkit-backdrop-filter: blur(10px);
  text-shadow: 0 1px 2px rgba(0, 0, 0, 0.3);
  box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.2);
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
    backdrop-filter: blur(16px);
    -webkit-backdrop-filter: blur(16px);
    box-shadow: 0 4px 20px rgba(0, 0, 0, 0.08);
  }

  .cover-section {
    max-width: 240px;
    max-height: 240px;
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
