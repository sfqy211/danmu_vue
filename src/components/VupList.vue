<template>
  <div class="vup-list-container">
    <div class="glass-container">
      <!-- 顶部标签栏 -->
      <div class="tabs-header">
        <button
          v-for="group in Object.values(GROUPS)"
          :key="group"
          @click="selectedGroup = group"
          class="tab-btn"
          :class="{ active: selectedGroup === group }"
        >
          {{ group }}
          <span class="tab-count">{{ groupCount(group) }}</span>
        </button>
      </div>

      <!-- 内容网格 -->
      <div class="content-grid">
        <div v-for="artist in filteredArtists" :key="artist.id" class="vup-card">
          <!-- 头像区 -->
          <div class="vup-avatar-wrapper">
            <img :src="artist.avatarUrl" :alt="artist.name" class="vup-avatar" @error="handleImageError" />
            <!-- 监控状态指示点 -->
            <span v-if="artist.hasMonitor" class="monitor-dot" title="已开启弹幕监控"></span>
          </div>

          <!-- 信息区 -->
          <div class="vup-info">
            <div class="vup-name-row">
              <h3 class="vup-name">{{ artist.name }}</h3>
              <span v-if="artist.isLiving" class="live-badge">
                <span class="live-pulse"></span>直播中
              </span>
            </div>

            <!-- 扩展信息预留行 -->
            <div class="vup-ext-row">
              <span class="ext-item" v-if="artist.followers != null">
                <span class="ext-icon">👥</span>{{ formatNumber(artist.followers) }}
              </span>
              <span class="ext-item ext-placeholder" v-else title="粉丝数（数据待接入）">
                <span class="ext-icon">👥</span>—
              </span>
              <span class="ext-item" v-if="artist.lastLiveTime">
                <span class="ext-icon">🕐</span>{{ formatRelativeTime(artist.lastLiveTime) }}
              </span>
              <span class="ext-item ext-placeholder" v-else title="最近直播（数据待接入）">
                <span class="ext-icon">🕐</span>—
              </span>
            </div>

            <!-- 操作链接 -->
            <div class="vup-links">
              <a :href="artist.homepageUrl" target="_blank" class="link-btn primary">主页</a>
              <a :href="artist.livestreamUrl" target="_blank" class="link-btn pink">直播间</a>
              <a v-if="artist.playlistUrl" :href="artist.playlistUrl" target="_blank" class="link-btn green">歌单</a>
              <!-- 监控用户额外展示弹幕入口 -->
              <router-link
                v-if="artist.hasMonitor"
                :to="`/vup/${artist.uid}`"
                class="link-btn monitor"
                title="查看弹幕历史"
              >弹幕</router-link>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue';
import { GROUPS, VUP_LIST, type VupItem } from '../constants/vups';

const selectedGroup = ref(GROUPS.OVO_FAMILY);

const filteredArtists = computed(() =>
  VUP_LIST.filter((artist: VupItem) => artist.groups.includes(selectedGroup.value))
);

const groupCount = (group: string) =>
  VUP_LIST.filter((a: VupItem) => a.groups.includes(group)).length;

const handleImageError = (e: Event) => {
  const target = e.target as HTMLImageElement;
  target.style.display = 'none';
  const wrapper = target.parentElement!;
  wrapper.style.backgroundColor = '#409EFF';
  wrapper.textContent = target.alt[0];
  wrapper.style.cssText += ';display:flex;align-items:center;justify-content:center;color:white;font-size:24px;font-weight:bold;';
};

const formatNumber = (n: number): string => {
  if (n >= 10000) return (n / 10000).toFixed(1) + '万';
  return n.toString();
};

const formatRelativeTime = (ts: number): string => {
  const d = Math.floor((Date.now() - ts) / 86400000);
  if (d === 0) return '今天';
  if (d === 1) return '昨天';
  if (d < 30) return `${d}天前`;
  if (d < 365) return `${Math.floor(d / 30)}月前`;
  return `${Math.floor(d / 365)}年前`;
};
</script>

<style scoped>
.vup-list-container {
  height: 100%;
  width: 100%;
  display: flex;
  align-items: center;
  justify-content: center;
  background: linear-gradient(135deg, #1a1a1a 0%, #2d3436 100%);
  padding: 40px;
  box-sizing: border-box;
}

.glass-container {
  width: 100%;
  max-width: 1200px;
  height: 90%;
  background: rgba(255, 255, 255, 0.05);
  backdrop-filter: blur(20px);
  border-radius: 24px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  padding: 30px;
  display: flex;
  flex-direction: column;
  gap: 30px;
}

/* ===== tabs ===== */
.tabs-header {
  display: flex;
  justify-content: center;
  gap: 15px;
  flex-wrap: wrap;
}

.tab-btn {
  background: transparent;
  border: 1px solid rgba(255, 255, 255, 0.2);
  color: rgba(255, 255, 255, 0.7);
  padding: 10px 24px;
  border-radius: 30px;
  cursor: pointer;
  transition: all 0.3s;
  font-size: 16px;
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.tab-btn.active {
  background: rgba(255, 255, 255, 0.2);
  color: white;
  border-color: white;
  box-shadow: 0 0 15px rgba(255, 255, 255, 0.2);
}

.tab-count {
  font-size: 12px;
  background: rgba(255, 255, 255, 0.15);
  padding: 1px 7px;
  border-radius: 10px;
  color: rgba(255, 255, 255, 0.6);
}
.tab-btn.active .tab-count {
  background: rgba(255, 255, 255, 0.3);
  color: white;
}

/* ===== 内容网格 ===== */
.content-grid {
  flex: 1;
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
  grid-auto-rows: max-content;
  gap: 20px;
  overflow-y: auto;
  padding-right: 10px;
}

.content-grid::-webkit-scrollbar { width: 6px; }
.content-grid::-webkit-scrollbar-thumb {
  background: rgba(255, 255, 255, 0.2);
  border-radius: 3px;
}

/* ===== VUP 卡片 ===== */
.vup-card {
  background: rgba(0, 0, 0, 0.3);
  border-radius: 16px;
  padding: 18px;
  display: flex;
  align-items: flex-start;
  gap: 15px;
  transition: transform 0.2s, background 0.2s, box-shadow 0.2s;
  border: 1px solid transparent;
}

.vup-card:hover {
  transform: translateY(-4px);
  background: rgba(0, 0, 0, 0.5);
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.4);
  border-color: rgba(255, 255, 255, 0.08);
}

/* 头像 */
.vup-avatar-wrapper {
  position: relative;
  width: 60px;
  height: 60px;
  border-radius: 50%;
  overflow: visible;
  flex-shrink: 0;
}

.vup-avatar {
  width: 60px;
  height: 60px;
  border-radius: 50%;
  object-fit: cover;
  border: 2px solid rgba(255, 255, 255, 0.2);
  display: block;
}

.monitor-dot {
  position: absolute;
  bottom: 2px;
  right: 2px;
  width: 12px;
  height: 12px;
  border-radius: 50%;
  background: #67C23A;
  border: 2px solid #1a1a1a;
  box-shadow: 0 0 6px rgba(103, 194, 58, 0.7);
}

/* 信息区 */
.vup-info {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-width: 0;
}

.vup-name-row {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.vup-name {
  margin: 0;
  color: white;
  font-size: 16px;
  font-weight: 600;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.live-badge {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: 11px;
  padding: 2px 7px;
  border-radius: 10px;
  background: rgba(245, 108, 108, 0.2);
  color: #f56c6c;
  border: 1px solid rgba(245, 108, 108, 0.35);
  flex-shrink: 0;
}

.live-pulse {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: #f56c6c;
  animation: pulse 1.2s infinite;
}

@keyframes pulse {
  0%, 100% { opacity: 1; transform: scale(1); }
  50% { opacity: 0.5; transform: scale(1.3); }
}

/* 扩展信息行 */
.vup-ext-row {
  display: flex;
  gap: 12px;
}

.ext-item {
  display: inline-flex;
  align-items: center;
  gap: 3px;
  font-size: 12px;
  color: rgba(255, 255, 255, 0.55);
}

.ext-item.ext-placeholder {
  color: rgba(255, 255, 255, 0.2);
}

.ext-icon {
  font-size: 11px;
}

/* 链接区 */
.vup-links {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
}

.link-btn {
  font-size: 12px;
  padding: 4px 10px;
  border-radius: 6px;
  text-decoration: none;
  color: white;
  background: rgba(255, 255, 255, 0.1);
  transition: all 0.2s;
  white-space: nowrap;
}

.link-btn:hover { filter: brightness(1.25); transform: translateY(-1px); }

.link-btn.primary  { background: #409EFF; }
.link-btn.pink     { background: #F56C6C; }
.link-btn.green    { background: #67C23A; }
.link-btn.monitor  { background: rgba(103, 194, 58, 0.25); border: 1px solid rgba(103, 194, 58, 0.4); color: #85d15e; }
</style>
