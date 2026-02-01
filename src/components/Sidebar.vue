<template>
  <div class="sidebar-container" :class="{ collapsed: store.isSidebarCollapsed }">
    <div class="sidebar-header">
      <el-button 
        class="collapse-btn" 
        :icon="store.isSidebarCollapsed ? Expand : Fold" 
        link 
        @click="store.toggleSidebar"
      />
      <h3 v-show="!store.isSidebarCollapsed">直播回放列表</h3>
    </div>
    <div class="filter-box" v-show="!store.isSidebarCollapsed">
      <el-select v-model="selectedStreamer" placeholder="选择主播" clearable @change="fetchSessions" class="streamer-select">
        <el-option
          v-for="streamer in streamers"
          :key="streamer"
          :label="streamer"
          :value="streamer"
        />
      </el-select>
    </div>
    <div class="session-list" v-show="!store.isSidebarCollapsed">
      <div v-if="!selectedStreamer" class="empty-list-tip">
        <el-empty description="请选择主播以查看回放列表" :image-size="60" />
      </div>
      <template v-else>
        <div 
          v-for="session in sessions" 
          :key="session.id"
          class="session-item"
          :class="{ active: activeSessionId === session.id.toString() }"
          @click="handleSelect(session)"
        >
          <div class="session-title" :title="session.title">{{ session.title || '无标题' }}</div>
          <div class="session-meta">
            <span class="user-name">{{ session.user_name }}</span>
            <span class="start-time">{{ formatTime(session.start_time) }}</span>
          </div>
        </div>
      </template>
    </div>
    
    <!-- Collapsed State Placeholder -->
    <div class="collapsed-placeholder" v-show="store.isSidebarCollapsed" @click="store.toggleSidebar">
      <div class="vertical-text">直播列表</div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { getStreamers, getSessions, type SessionInfo } from '../api/danmaku';
import { useDanmakuStore } from '../stores/danmakuStore';
import { Fold, Expand } from '@element-plus/icons-vue';

const store = useDanmakuStore();
const streamers = ref<string[]>([]);
const sessions = ref<SessionInfo[]>([]);
const selectedStreamer = ref('');
const activeSessionId = ref('');

const formatTime = (ts: number) => {
  const date = new Date(ts);
  return `${date.getFullYear()}/${date.getMonth() + 1}/${date.getDate()} ${date.getHours().toString().padStart(2, '0')}:${date.getMinutes().toString().padStart(2, '0')}`;
};

const fetchSessions = async () => {
  if (!selectedStreamer.value) {
    sessions.value = [];
    return;
  }
  try {
    sessions.value = await getSessions({
      userName: selectedStreamer.value
    });
  } catch (e) {
    console.error(e);
  }
};

const handleSelect = (session: SessionInfo) => {
  activeSessionId.value = session.id.toString();
  store.loadSession(session);
  // Auto collapse sidebar on mobile or desktop when selection is made
  if (!store.isSidebarCollapsed) {
    store.isSidebarCollapsed = true;
  }
};

onMounted(async () => {
  try {
    streamers.value = await getStreamers();
  } catch (e) {
    console.error(e);
  }
});
</script>

<style scoped>
.sidebar-container {
  height: 100%;
  display: flex;
  flex-direction: column;
  background-color: var(--bg-secondary);
  transition: width 0.3s cubic-bezier(0.4, 0, 0.2, 1);
  width: 300px;
  overflow: hidden;
  border-right: 1px solid var(--border);
}

@media (max-width: 768px) {
  .sidebar-container {
    position: fixed;
    left: 0;
    top: 0;
    z-index: 1000;
    box-shadow: 2px 0 12px rgba(0,0,0,0.1);
  }
  .sidebar-container.collapsed {
    width: 0 !important;
    border-right: none;
  }
}

.sidebar-container.collapsed {
  width: 50px;
}

.sidebar-header {
  display: flex;
  align-items: center;
  justify-content: flex-start;
  gap: 8px;
  padding: 20px 16px 12px;
  min-height: 64px;
}

.collapsed .sidebar-header {
  padding: 20px 0;
  justify-content: center;
  gap: 0;
}

.collapse-btn {
  font-size: 1.2rem;
  color: var(--text-secondary);
  padding: 8px;
  margin-left: -4px; /* Align icon slightly better with container padding */
}

.collapsed .collapse-btn {
  margin-left: 0;
}

.collapsed-placeholder {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
}

.vertical-text {
  writing-mode: vertical-lr;
  text-orientation: mixed;
  color: var(--text-tertiary);
  letter-spacing: 4px;
  font-weight: 500;
  font-size: 0.9rem;
  opacity: 0.6;
  transition: opacity 0.2s;
}

.collapsed-placeholder:hover .vertical-text {
  opacity: 1;
  color: var(--el-color-primary);
}

.streamer-select {
  width: 100%;
}
.streamer-select :deep(.el-select__wrapper) {
  border-radius: 8px;
  background-color: var(--bg-card);
  box-shadow: none;
  border: 1px solid var(--border);
}

.empty-list-tip {
  height: 100%;
  display: flex;
  align-items: center;
  justify-content: center;
  padding-top: 40px;
}

.empty-list-tip :deep(.el-empty__description p) {
  color: var(--text-tertiary);
  font-size: 0.85rem;
}
</style>
