<template>
  <div class="home-container">
    <!-- 动态背景层 -->
    <div
      class="dynamic-bg"
      :style="{ backgroundImage: `url(${activeStreamer.coverUrl || activeStreamer.imageUrl})` }"
    ></div>
    <div class="bg-overlay"></div>

    <!-- ===== 顶部导航栏 ===== -->
    <header class="top-nav" :class="{ 'menu-open': mobileMenuOpen }">
      <div class="nav-inner">
        <!-- Logo 区 -->
        <div class="nav-logo" @click="activeIndex = 0">
          <img src="/vite.svg" class="logo-avatar" />
          <span class="logo-text">VUP 弹幕站</span>
        </div>

        <!-- 桌面端：用户切换 tab 列表 -->
        <nav class="nav-tabs" ref="navTabsRef">
          <button
            v-for="(streamer, i) in VUP_LIST"
            :key="streamer.uid"
            class="nav-tab"
            :class="{ active: activeIndex === i }"
            @click="selectStreamer(i)"
          >
            <img :src="streamer.avatarUrl" :alt="streamer.name" class="tab-avatar" />
            <span class="tab-name">{{ streamer.name }}</span>
            <span v-if="streamer.hasMonitor" class="monitor-dot" title="已开启弹幕监控"></span>
          </button>
        </nav>

        <!-- 移动端：汉堡菜单按钮 -->
        <button class="hamburger" @click="mobileMenuOpen = !mobileMenuOpen" aria-label="切换菜单">
          <span></span><span></span><span></span>
        </button>
      </div>

      <!-- 移动端展开菜单 -->
      <div class="mobile-menu" v-show="mobileMenuOpen">
        <button
          v-for="(streamer, i) in VUP_LIST"
          :key="streamer.uid"
          class="mobile-menu-item"
          :class="{ active: activeIndex === i }"
          @click="selectStreamer(i); mobileMenuOpen = false"
        >
          <img :src="streamer.avatarUrl" :alt="streamer.name" class="mobile-avatar" />
          <span>{{ streamer.name }}</span>
          <span v-if="streamer.hasMonitor" class="monitor-badge">监控</span>
        </button>
      </div>
    </header>

    <!-- ===== 主内容区 ===== -->
    <main class="main-content">
      <!-- 主角卡片 -->
      <div class="profile-card" :key="activeStreamer.uid">
        <!-- 封面/头像区 -->
        <div class="cover-section" @click="openLivestream(activeStreamer.livestreamUrl)">
          <img
            :src="activeStreamer.coverUrl || activeStreamer.imageUrl"
            :alt="activeStreamer.name"
            class="cover-img"
            @error="handleImageError($event, activeStreamer.imageUrl)"
          />
          <div class="cover-hover-mask">
            <el-icon class="play-icon"><VideoPlay /></el-icon>
            <span>进入直播间</span>
          </div>
        </div>

        <!-- 信息区 -->
        <div class="info-section">
          <!-- 名称 + 状态 -->
          <div class="name-row">
            <img :src="activeStreamer.avatarUrl" class="profile-avatar" />
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

            <!-- 无监控用户：显示禁用态占位 -->
            <template v-else>
              <div class="action-card disabled">
                <el-icon class="action-icon"><ChatDotRound /></el-icon>
                <div class="action-text">
                  <span class="action-title">弹幕历史</span>
                  <span class="action-desc">暂未开启监控</span>
                </div>
              </div>
              <div class="action-card disabled">
                <el-icon class="action-icon"><Headset /></el-icon>
                <div class="action-text">
                  <span class="action-title">点歌历史</span>
                  <span class="action-desc">暂未开启监控</span>
                </div>
              </div>
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
import { ref, computed, nextTick } from 'vue';
import { useRouter } from 'vue-router';
import { VUP_LIST } from '../constants/vups';
import {
  ChatDotRound, Headset, ArrowRight, User,
  Promotion, VideoPlay, DataLine
} from '@element-plus/icons-vue';

const router = useRouter();
const activeIndex = ref(0);
const mobileMenuOpen = ref(false);
const navTabsRef = ref<HTMLElement | null>(null);

const activeStreamer = computed(() => VUP_LIST[activeIndex.value]);

const selectStreamer = (index: number) => {
  activeIndex.value = index;
  // 滚动导航栏至对应 tab 可见
  nextTick(() => {
    const tabs = navTabsRef.value;
    if (!tabs) return;
    const activeTab = tabs.children[index] as HTMLElement;
    if (activeTab) {
      activeTab.scrollIntoView({ behavior: 'smooth', block: 'nearest', inline: 'center' });
    }
  });
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
  filter: blur(60px) brightness(0.4) saturate(1.4);
  transform: scale(1.15);
  z-index: 0;
  transition: background-image 0.8s ease;
}

.bg-overlay {
  position: fixed;
  inset: 0;
  background: linear-gradient(
    180deg,
    rgba(0, 0, 0, 0.55) 0%,
    rgba(0, 0, 0, 0.25) 40%,
    rgba(0, 0, 0, 0.6) 100%
  );
  z-index: 1;
}

/* ===== 顶部导航栏 ===== */
.top-nav {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  z-index: 100;
  background: rgba(10, 10, 15, 0.7);
  backdrop-filter: blur(20px);
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
}

.nav-inner {
  height: 56px;
  display: flex;
  align-items: center;
  padding: 0 16px;
  gap: 16px;
}

.nav-logo {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-shrink: 0;
  cursor: pointer;
}

.logo-avatar {
  width: 32px;
  height: 32px;
  border-radius: 50%;
  object-fit: cover;
  border: 1.5px solid rgba(255, 255, 255, 0.3);
}

.logo-text {
  color: white;
  font-weight: 700;
  font-size: 15px;
  white-space: nowrap;
}

/* 滚动式 tab 列表 */
.nav-tabs {
  flex: 1;
  display: flex;
  gap: 4px;
  overflow-x: auto;
  scrollbar-width: none;
  -ms-overflow-style: none;
  padding: 4px 0;
}
.nav-tabs::-webkit-scrollbar { display: none; }

.nav-tab {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  gap: 7px;
  padding: 5px 12px;
  border-radius: 20px;
  border: 1px solid transparent;
  background: transparent;
  color: rgba(255, 255, 255, 0.55);
  cursor: pointer;
  font-size: 13px;
  transition: all 0.2s ease;
  position: relative;
  white-space: nowrap;
}

.nav-tab:hover {
  background: rgba(255, 255, 255, 0.08);
  color: rgba(255, 255, 255, 0.9);
}

.nav-tab.active {
  background: rgba(255, 255, 255, 0.15);
  color: white;
  border-color: rgba(255, 255, 255, 0.25);
  font-weight: 600;
}

.tab-avatar {
  width: 22px;
  height: 22px;
  border-radius: 50%;
  object-fit: cover;
}

.tab-name {
  max-width: 80px;
  overflow: hidden;
  text-overflow: ellipsis;
}

.monitor-dot {
  position: absolute;
  top: 4px;
  right: 6px;
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: #67C23A;
  box-shadow: 0 0 6px rgba(103, 194, 58, 0.8);
}

/* 汉堡菜单 */
.hamburger {
  display: none;
  flex-direction: column;
  gap: 5px;
  background: none;
  border: none;
  cursor: pointer;
  padding: 6px;
  flex-shrink: 0;
}
.hamburger span {
  display: block;
  width: 22px;
  height: 2px;
  background: rgba(255, 255, 255, 0.8);
  border-radius: 2px;
  transition: all 0.3s;
}

.mobile-menu {
  border-top: 1px solid rgba(255, 255, 255, 0.08);
  max-height: 60vh;
  overflow-y: auto;
  padding: 8px;
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 6px;
}

.mobile-menu-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 12px;
  border-radius: 12px;
  background: rgba(255, 255, 255, 0.05);
  border: 1px solid transparent;
  color: rgba(255, 255, 255, 0.7);
  font-size: 13px;
  cursor: pointer;
  transition: all 0.2s;
  text-align: left;
}
.mobile-menu-item:hover { background: rgba(255, 255, 255, 0.1); }
.mobile-menu-item.active {
  background: rgba(64, 158, 255, 0.2);
  border-color: rgba(64, 158, 255, 0.4);
  color: white;
}

.mobile-avatar {
  width: 30px;
  height: 30px;
  border-radius: 50%;
  object-fit: cover;
  flex-shrink: 0;
}

.monitor-badge {
  margin-left: auto;
  font-size: 10px;
  padding: 2px 6px;
  border-radius: 6px;
  background: rgba(103, 194, 58, 0.25);
  color: #67C23A;
  border: 1px solid rgba(103, 194, 58, 0.4);
  flex-shrink: 0;
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
  flex: 1;
  min-height: 0;
  display: grid;
  grid-template-columns: 340px 1fr;
  gap: 36px;
  background: rgba(255, 255, 255, 0.05);
  backdrop-filter: blur(24px);
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 24px;
  padding: 32px;
  animation: cardIn 0.4s ease;
  overflow: hidden;
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
  box-shadow: 0 16px 48px rgba(0, 0, 0, 0.5);
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
  width: 54px;
  height: 54px;
  border-radius: 50%;
  object-fit: cover;
  border: 2px solid rgba(255, 255, 255, 0.25);
  flex-shrink: 0;
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
  background: rgba(255, 255, 255, 0.05);
  border: 1px solid rgba(255, 255, 255, 0.07);
  border-radius: 12px;
  padding: 12px 16px;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.ext-info-wide {
  grid-column: span 1;
}

.ext-label {
  font-size: 11px;
  color: rgba(255, 255, 255, 0.4);
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.ext-value {
  font-size: 14px;
  color: rgba(255, 255, 255, 0.9);
  font-weight: 500;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
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
  background: rgba(255, 255, 255, 0.07);
  border: 1px solid rgba(255, 255, 255, 0.1);
  color: rgba(255, 255, 255, 0.85);
  cursor: pointer;
  transition: all 0.25s ease;
  text-decoration: none;
}

.action-card:hover:not(.disabled) {
  background: rgba(255, 255, 255, 0.15);
  border-color: rgba(255, 255, 255, 0.25);
  transform: translateY(-2px);
  box-shadow: 0 6px 20px rgba(0, 0, 0, 0.3);
}

.action-card.disabled {
  opacity: 0.35;
  cursor: not-allowed;
}

.action-icon {
  font-size: 20px;
  flex-shrink: 0;
  color: rgba(255, 255, 255, 0.7);
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
  color: white;
}

.action-desc {
  font-size: 11px;
  color: rgba(255, 255, 255, 0.45);
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
  background: rgba(64, 158, 255, 0.12);
  color: rgba(121, 187, 255, 0.9);
  border: 1px solid rgba(64, 158, 255, 0.25);
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
