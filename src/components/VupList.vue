<template>
  <div class="vup-list-page">
    <!-- 顶部导航栏 -->
    <TopNav />

    <div class="vup-list-container">
      <div class="list-container">
        <!-- 所有分类的主播 -->
        <div v-for="group in Object.values(GROUPS)" :key="group" class="group-section">
          <!-- 分类标题 -->
          <div class="group-header">
            <h3 class="group-title">{{ group }}</h3>
            <span class="group-count">{{ groupCount(group) }}</span>
          </div>
          
          <!-- 主播网格 -->
          <div class="streamer-grid">
            <div
              v-for="artist in getArtistsByGroup(group)"
              :key="artist.uid"
              class="streamer-item"
              @click="selectStreamer(artist)"
            >
              <img :src="artist.avatarUrl" :alt="artist.name" class="streamer-avatar" />
              <div class="streamer-info">
                <span class="streamer-name">{{ artist.name }}</span>
                <span v-if="artist.hasMonitor" class="monitor-indicator">
                  <el-icon><DataLine /></el-icon>
                </span>
              </div>
              <span v-if="artist.isLiving" class="live-indicator">
                <span class="live-dot"></span>
              </span>
            </div>
          </div>
          
          <!-- 分类分割线（最后一个分类不需要） -->
          <div v-if="group !== Object.values(GROUPS)[Object.values(GROUPS).length - 1]" class="group-divider"></div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { useRouter } from 'vue-router';
import TopNav from './TopNav.vue';
import { VUP_LIST, GROUPS, type VupItem } from '../constants/vups';
import { DataLine } from '@element-plus/icons-vue';
import { useDanmakuStore } from '../stores/danmakuStore';

const router = useRouter();
const store = useDanmakuStore();

const getArtistsByGroup = (group: string) => {
  return VUP_LIST.filter((artist: VupItem) => artist.groups.includes(group));
};

const groupCount = (group: string) => {
  return VUP_LIST.filter((a: VupItem) => a.groups.includes(group)).length;
};

const selectStreamer = (artist: VupItem) => {
  const index = VUP_LIST.findIndex(v => v.uid === artist.uid);
  if (index !== -1) {
    store.setCurrentVupIndex(index);
    router.push('/');
  }
};
</script>

<style scoped>
.vup-list-page {
  min-height: 100vh;
  width: 100%;
  position: relative;
  /* background: linear-gradient(135deg, #1a1a1a 0%, #2d3436 100%); */
  overflow-x: hidden;
}

.vup-list-container {
  min-height: 100vh;
  width: 100%;
  display: flex;
  align-items: flex-start;
  justify-content: center;
  padding: 80px 40px 40px;
  box-sizing: border-box;
}

.list-container {
  max-width: 1200px;
  margin: 0 auto;
  padding: 20px;
  display: flex;
  flex-direction: column;
  gap: 32px;
  width: 100%;
}

/* ===== 分类区域 ===== */
.group-section {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.group-header {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 8px;
}

.group-title {
  font-size: 18px;
  font-weight: 600;
  color: white;
  margin: 0;
  text-shadow: 0 2px 4px rgba(0, 0, 0, 0.3);
}

.group-count {
  font-size: 14px;
  background: rgba(255, 255, 255, 0.12);
  padding: 4px 12px;
  border-radius: 12px;
  color: rgba(255, 255, 255, 0.7);
  backdrop-filter: blur(10px);
  -webkit-backdrop-filter: blur(10px);
}

/* ===== 主播网格 ===== */
.streamer-grid {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  padding-bottom: 8px;
}

.streamer-item {
  position: relative;
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 8px 16px;
  background: rgba(255, 255, 255, 0.06);
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 24px;
  cursor: pointer;
  transition: all 0.25s ease;
  backdrop-filter: blur(8px);
  -webkit-backdrop-filter: blur(8px);
  min-width: 160px;
  max-width: 220px;
  flex: 1 1 auto;
}

.streamer-item:hover {
  background: rgba(255, 255, 255, 0.12);
  border-color: rgba(255, 255, 255, 0.25);
  transform: translateY(-2px);
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.3);
}

.streamer-avatar {
  width: 36px;
  height: 36px;
  border-radius: 50%;
  object-fit: cover;
  border: 2px solid rgba(255, 255, 255, 0.2);
  transition: all 0.3s ease;
  flex-shrink: 0;
}

.streamer-item:hover .streamer-avatar {
  border-color: rgba(255, 255, 255, 0.4);
  transform: scale(1.05);
}

.streamer-info {
  display: flex;
  align-items: center;
  gap: 6px;
  flex: 1;
  min-width: 0;
}

.streamer-name {
  font-size: 14px;
  font-weight: 500;
  color: white;
  text-align: left;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  flex: 1;
  text-shadow: 0 1px 3px rgba(0, 0, 0, 0.3);
}

.monitor-indicator {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 18px;
  height: 18px;
  background: rgba(103, 194, 58, 0.2);
  border: 1px solid rgba(103, 194, 58, 0.4);
  border-radius: 50%;
  flex-shrink: 0;
}

.monitor-indicator .el-icon {
  font-size: 12px;
  color: #67c23a;
}

.live-indicator {
  position: absolute;
  top: 6px;
  right: 6px;
  width: 12px;
  height: 12px;
  border-radius: 50%;
  background: rgba(245, 108, 108, 0.2);
  border: 1px solid rgba(245, 108, 108, 0.4);
  display: flex;
  align-items: center;
  justify-content: center;
}

.live-indicator .live-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: #f56c6c;
  animation: livePulse 1.2s infinite;
}

/* ===== 分类分割线 ===== */
.group-divider {
  height: 1px;
  background: linear-gradient(90deg, transparent, rgba(255, 255, 255, 0.2), transparent);
  margin-top: 24px;
  margin-bottom: 8px;
}

@keyframes livePulse {
  0%, 100% { opacity: 1; transform: scale(1); }
  50% { opacity: 0.5; transform: scale(1.3); }
}
</style>
