<template>
  <div class="vup-list-page">
    <TopNav />

    <div class="vup-list-container">
      <div class="list-container">
        <div class="list-header">
          <h2 class="list-title">VUP 列表</h2>
          <span class="list-count">{{ vups.length }}</span>
        </div>

        <div v-if="vups.length > 0" class="streamer-grid">
          <div
            v-for="artist in vups"
            :key="artist.uid"
            class="streamer-item"
            @click="selectStreamer(artist.uid)"
          >
            <img :src="artist.avatarUrl" :alt="artist.name" class="streamer-avatar" />
            <div class="streamer-info">
              <span class="streamer-name">{{ artist.name }}</span>
              <span v-if="artist.hasMonitor" class="monitor-indicator">
                <el-icon><DataLine /></el-icon>
              </span>
            </div>
          </div>
        </div>

        <div v-else class="empty-state">
          <el-empty :description="store.vupLoading ? '正在加载 VUP 列表...' : '暂无可展示的 VUP'" />
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import TopNav from './TopNav.vue';
import { DataLine } from '@element-plus/icons-vue';
import { useDanmakuStore } from '../stores/danmakuStore';

const router = useRouter();
const store = useDanmakuStore();
const vups = computed(() => store.vups);

onMounted(() => {
  store.loadVups();
});

const selectStreamer = (uid: string) => {
  store.setCurrentVup(uid);
  router.replace('/');
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
.list-header {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 4px;
}

.list-title {
  font-size: 20px;
  font-weight: 600;
  color: white;
  margin: 0;
  text-shadow: 0 2px 4px rgba(0, 0, 0, 0.3);
}

.list-count {
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

.empty-state {
  min-height: 260px;
  display: flex;
  align-items: center;
  justify-content: center;
}

.streamer-item {
  position: relative;
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 12px 18px;
  background: rgba(255, 255, 255, 0.08);
  border: 1px solid rgba(255, 255, 255, 0.15);
  border-radius: 24px;
  cursor: pointer;
  transition: all 0.25s ease;
  backdrop-filter: blur(12px);
  -webkit-backdrop-filter: blur(12px);
  min-width: 200px;
  flex: 1 1 200px;
}

.streamer-item:hover {
  background: rgba(255, 255, 255, 0.15);
  border-color: rgba(255, 255, 255, 0.3);
  transform: translateY(-2px);
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.15);
}

.streamer-avatar {
  width: 42px;
  height: 42px;
  border-radius: 50%;
  object-fit: cover;
  border: 2px solid rgba(255, 255, 255, 0.2);
  transition: all 0.3s ease;
  flex-shrink: 0;
}

.streamer-item:hover .streamer-avatar {
  border-color: rgba(255, 255, 255, 0.5);
  transform: scale(1.05);
}

.streamer-info {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  flex: 1;
  min-width: 0;
}

.streamer-name {
  font-size: 15px;
  font-weight: 600;
  color: white;
  text-align: left;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  text-shadow: 0 1px 3px rgba(0, 0, 0, 0.4);
}

.monitor-indicator {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 24px;
  background: rgba(103, 194, 58, 0.2);
  border: 1px solid rgba(103, 194, 58, 0.4);
  border-radius: 50%;
  flex-shrink: 0;
}

.monitor-indicator .el-icon {
  font-size: 14px;
  color: #95d475;
}

/* 移动端优化：占满一行 */
@media (max-width: 768px) {
  .vup-list-container {
    padding: 72px 16px 40px;
  }
  
  .streamer-item {
    flex: 1 1 100%;
    max-width: none;
    padding: 14px 20px;
    background: rgba(255, 255, 255, 0.12); /* 移动端稍亮一点 */
    backdrop-filter: none; /* 移动端移除模糊以提升性能 */
    -webkit-backdrop-filter: none;
  }
  
  .streamer-name {
    font-size: 16px;
  }
  
  .streamer-avatar {
    width: 48px;
    height: 48px;
  }
}

</style>
