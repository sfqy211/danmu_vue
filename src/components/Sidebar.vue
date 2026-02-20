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
      <el-button 
        class="refresh-btn" 
        :icon="Refresh" 
        link 
        v-show="!store.isSidebarCollapsed" 
        @click="handleRefresh"
      />
    </div>
    
    <Teleport to="#header-dynamic-actions" :disabled="isMobile" v-if="isMountedFlag">
      <div class="sidebar-filter-wrapper" v-show="(!store.isSidebarCollapsed || !isMobile) && !isStreamerMode">
        <el-select 
          v-model="selectedStreamer" 
          placeholder="选择主播" 
          clearable 
          @change="fetchSessions" 
          class="streamer-select"
          :size="isMobile ? 'default' : 'default'"
        >
          <el-option
            v-for="streamer in streamers"
            :key="streamer.user_name"
            :label="streamer.user_name"
            :value="streamer.user_name"
          />
        </el-select>
      </div>
    </Teleport>

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
import { ref, onMounted, onUnmounted, computed, watch } from 'vue';
import { useRoute } from 'vue-router';
import { getStreamers, getSessions, type SessionInfo, type StreamerInfo } from '../api/danmaku';
import { useDanmakuStore } from '../stores/danmakuStore';
import { Fold, Expand, Refresh } from '@element-plus/icons-vue';
import { VUP_LIST } from '../constants/vups';

const store = useDanmakuStore();
const route = useRoute();
const streamers = ref<StreamerInfo[]>([]);
const sessions = ref<SessionInfo[]>([]);
const selectedStreamer = ref('');
const activeSessionId = ref('');
const isMobile = ref(window.innerWidth <= 768);
const isMountedFlag = ref(false);

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

const syncStreamerName = async () => {
  const uid = Array.isArray(route.params.uid) ? route.params.uid[0] : route.params.uid;
  if (!uid) return;
  
  const vup = VUP_LIST.find(v => v.uid === uid);
  if (!vup) return;
  
  // 默认使用 VUP_LIST 中的名字
  let targetName = vup.name;
  
  // 尝试通过 room_id 匹配后端列表中的名字 (以防名字不一致)
  if (streamers.value.length > 0 && vup.livestreamUrl) {
    const match = vup.livestreamUrl.match(/\/(\d+)$/);
    if (match) {
      const roomId = match[1];
      const backendStreamer = streamers.value.find(s => s.room_id?.toString() === roomId);
      if (backendStreamer) {
        targetName = backendStreamer.user_name;
      }
    }
  }
  
  // 如果名字有变化或者当前未选中，则更新并获取回放
  if (selectedStreamer.value !== targetName || !sessions.value.length) {
    // 切换主播时，清理之前的会话状态
    if (store.currentSession && store.currentSession.user_name !== targetName) {
      store.clearSession();
      activeSessionId.value = '';
    }
    
    selectedStreamer.value = targetName;
    await fetchSessions();
  }
};

const isStreamerMode = computed(() => !!route.params.uid);

// 监听路由参数 UID 变化
watch(() => route.params.uid, async () => {
  await syncStreamerName();
}, { immediate: true });

const handleResize = () => {
  isMobile.value = window.innerWidth <= 768;
};

const handleSelect = (session: SessionInfo) => {
  activeSessionId.value = session.id.toString();
  store.loadSession(session);
  // Auto collapse sidebar on mobile or desktop when selection is made
  if (!store.isSidebarCollapsed) {
    store.isSidebarCollapsed = true;
  }
};

const handleRefresh = async () => {
  try {
    // 刷新主播列表
    streamers.value = await getStreamers();
    // 刷新当前选中主播的回放列表
    if (selectedStreamer.value) {
      await fetchSessions();
    }
  } catch (e) {
    console.error(e);
  }
};

onMounted(async () => {
  isMountedFlag.value = true;
  window.addEventListener('resize', handleResize);
  try {
    streamers.value = await getStreamers();
    // 获取主播列表后，尝试再次同步名字 (以防 watch 执行时列表为空)
    await syncStreamerName();
  } catch (e) {
    console.error(e);
  }
});

onUnmounted(() => {
  window.removeEventListener('resize', handleResize);
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

.refresh-btn {
  font-size: 1rem;
  color: var(--text-secondary);
  padding: 8px;
  margin-left: auto;
  transition: color 0.2s;
}

.refresh-btn:hover {
  color: var(--el-color-primary);
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
.sidebar-filter-wrapper {
  width: 160px;
}
@media (max-width: 768px) {
  .sidebar-filter-wrapper {
    width: 100%;
    padding: 0 16px 12px;
  }
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
