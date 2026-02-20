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
      <div class="session-count" v-show="!store.isSidebarCollapsed && selectedStreamer">
        <span class="current">{{ sessions.length }}</span>
        <span class="separator">/</span>
        <span class="total">{{ totalSessions }}</span>
      </div>
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
          @change="handleStreamerChange" 
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

    <div class="filter-section" v-show="!store.isSidebarCollapsed && selectedStreamer">
      <div class="filter-row">
        <div class="filter-item-wrapper time-range-wrapper">
          <el-select 
            v-model="timeRange" 
            placeholder="范围"
            size="small"
            @change="handleTimeRangeChange"
            class="time-range-select"
          >
            <el-option label="最近7天" value="7d" />
            <el-option label="最近30天" value="30d" />
            <el-option label="最近180天" value="180d" />
            <el-option label="最近365天" value="365d" />
            <el-option-group label="按年份选择">
              <el-option 
                v-for="year in availableYears" 
                :key="year" 
                :label="`${year}年`" 
                :value="`year-${year}`"
              />
            </el-option-group>
          </el-select>
        </div>
        
        <div class="filter-item-wrapper date-picker-wrapper">
          <el-date-picker
            v-model="customDate"
            type="date"
            placeholder="选择一个具体日期"
            size="small"
            @change="handleCustomDateChange"
            class="date-picker"
            :clearable="true"
          />
        </div>
      </div>
      

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
import { ref, onMounted, onUnmounted, computed, watch } from 'vue';
import { useRoute } from 'vue-router';
import { getStreamers, getSessions, getSessionsTotal, type SessionInfo, type StreamerInfo } from '../api/danmaku';
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

const timeRange = ref<string>('30d');
const customDate = ref<Date | null>(null);
const totalSessions = ref(0);
const availableYears = ref<number[]>([]);

const formatTime = (ts: number) => {
  const date = new Date(ts);
  return `${date.getFullYear()}/${date.getMonth() + 1}/${date.getDate()} ${date.getHours().toString().padStart(2, '0')}:${date.getMinutes().toString().padStart(2, '0')}`;
};

const fetchSessions = async () => {
  if (!selectedStreamer.value) {
    sessions.value = [];
    return;
  }
  
  const filters: any = {
    userName: selectedStreamer.value
  };
  
  if (timeRange.value) {
    const now = Date.now();
    
    if (timeRange.value === '7d') {
      filters.startTime = now - 7 * 24 * 60 * 60 * 1000;
    } else if (timeRange.value === '30d') {
      filters.startTime = now - 30 * 24 * 60 * 60 * 1000;
    } else if (timeRange.value === '180d') {
      filters.startTime = now - 180 * 24 * 60 * 60 * 1000;
    } else if (timeRange.value === '365d') {
      filters.startTime = now - 365 * 24 * 60 * 60 * 1000;
    } else if (timeRange.value.startsWith('year-')) {
      const year = parseInt(timeRange.value.split('-')[1]);
      filters.startTime = new Date(year, 0, 1).getTime();
      filters.endTime = new Date(year + 1, 0, 1).getTime();
    }
  } else if (customDate.value) {
    const selectedDate = new Date(customDate.value);
    const startOfDay = new Date(selectedDate.getFullYear(), selectedDate.getMonth(), selectedDate.getDate(), 0, 0, 0);
    const endOfDay = new Date(selectedDate.getFullYear(), selectedDate.getMonth(), selectedDate.getDate(), 23, 59, 59, 999);
    filters.startTime = startOfDay.getTime();
    filters.endTime = endOfDay.getTime();
  }
  
  try {
    sessions.value = await getSessions(filters);
  } catch (e) {
    console.error(e);
  }
};

const fetchTotalSessions = async () => {
  if (!selectedStreamer.value) {
    totalSessions.value = 0;
    return;
  }
  
  try {
    const result = await getSessionsTotal({ userName: selectedStreamer.value });
    totalSessions.value = result.total;
  } catch (e) {
    console.error(e);
  }
};

const handleStreamerChange = async () => {
  timeRange.value = '30d'; // 默认选择最近7天
  customDate.value = null;
  await Promise.all([fetchSessions(), fetchTotalSessions(), updateAvailableYears()]);
};

const handleTimeRangeChange = async () => {
  customDate.value = null;
  await fetchSessions();
};

const handleCustomDateChange = async () => {
  timeRange.value = '';
  await fetchSessions();
};

const syncStreamerName = async () => {
  const uid = Array.isArray(route.params.uid) ? route.params.uid[0] : route.params.uid;
  if (!uid) return;
  
  const vup = VUP_LIST.find(v => v.uid === uid);
  if (!vup) return;
  
  let targetName = vup.name;
  
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
  
  if (selectedStreamer.value !== targetName || !sessions.value.length) {
    if (store.currentSession && store.currentSession.user_name !== targetName) {
      store.clearSession();
      activeSessionId.value = '';
    }
    
    selectedStreamer.value = targetName;
    timeRange.value = '30d'; // 默认选择最近7天
    customDate.value = null;
    await Promise.all([fetchSessions(), fetchTotalSessions(), updateAvailableYears()]);
  }
};

const updateAvailableYears = async () => {
  if (!selectedStreamer.value) {
    availableYears.value = [];
    return;
  }
  
  try {
    const allSessions = await getSessions({ userName: selectedStreamer.value });
    const years = new Set<number>();
    allSessions.forEach(session => {
      const year = new Date(session.start_time).getFullYear();
      years.add(year);
    });
    availableYears.value = Array.from(years).sort((a, b) => b - a);
  } catch (e) {
    console.error(e);
  }
};

const isStreamerMode = computed(() => !!route.params.uid);

watch(() => route.params.uid, async () => {
  await syncStreamerName();
}, { immediate: true });

const handleResize = () => {
  isMobile.value = window.innerWidth <= 768;
};

const handleSelect = (session: SessionInfo) => {
  activeSessionId.value = session.id.toString();
  store.loadSession(session);
  if (!store.isSidebarCollapsed) {
    store.isSidebarCollapsed = true;
  }
};

const handleRefresh = async () => {
  try {
    streamers.value = await getStreamers();
    if (selectedStreamer.value) {
      await Promise.all([fetchSessions(), fetchTotalSessions(), updateAvailableYears()]);
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
  margin-left: -4px;
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

.filter-section {
  padding: 0 16px 12px;
  border-bottom: 1px solid var(--border);
}

.filter-row {
  display: flex;
  flex-direction: row;
  gap: 8px;
  margin-bottom: 8px;
  width: 100%;
  box-sizing: border-box;
}

.filter-item-wrapper {
  min-width: 0;
  display: flex;
  box-sizing: border-box;
}

.time-range-wrapper {
  flex: 0 0 30%;
}

.date-picker-wrapper {
  flex: 0 0 70%;
}

/* 确保内部组件占满宽度 */
.filter-item-wrapper :deep(.el-select),
.filter-item-wrapper :deep(.el-date-editor) {
  width: 100% !important;
}

.filter-item-wrapper :deep(.el-select__wrapper),
.filter-item-wrapper :deep(.el-input__wrapper) {
  width: 100% !important;
  box-sizing: border-box !important;
}

.session-count {
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 0.85rem;
  color: var(--text-tertiary);
  margin: 0 8px;
  flex: 1;
  text-align: center;
}

.session-count .current {
  color: var(--el-color-primary);
  font-weight: 500;
}

.session-count .separator {
  margin: 0 4px;
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

.session-list {
  flex: 1;
  overflow-y: auto;
  padding: 8px 0;
}

.session-item {
  padding: 12px 16px;
  cursor: pointer;
  transition: background-color 0.2s;
  border-bottom: 1px solid var(--border-light);
}

.session-item:hover {
  background-color: var(--bg-hover);
}

.session-item.active {
  background-color: var(--bg-active);
  border-left: 3px solid var(--el-color-primary);
  margin: 4px 12px;
  border-radius: 8px;
  padding-left: 12px;
}

.session-title {
  font-size: 0.95rem;
  font-weight: 500;
  color: var(--text-primary);
  margin-bottom: 4px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.session-meta {
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-size: 0.8rem;
  color: var(--text-secondary);
}

.user-name {
  font-weight: 500;
}

.start-time {
  /* 使用默认字体，与主播名字保持一致 */
}
</style>
