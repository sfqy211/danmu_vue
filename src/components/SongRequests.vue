<template>
  <div class="song-requests-container" v-loading="loading">
    <Teleport to="#header-dynamic-actions" :disabled="isMobile" v-if="isMountedFlag">
      <div class="header-controls-teleported">
        <div class="streamer-selector">
          <el-select 
            v-if="!isStreamerMode"
            v-model="selectedStreamer" 
            placeholder="选择主播" 
            style="width: 160px"
            filterable
            clearable
            value-key="user_name"
            @change="handleStreamerChange"
            size="default"
          >
            <el-option
              v-for="streamer in streamers"
              :key="streamer.user_name"
              :label="streamer.user_name"
              :value="streamer"
            />
          </el-select>
          
          <el-radio-group v-model="viewMode" size="small" @change="fetchRequests" v-if="selectedStreamer && store.currentSession && store.currentSession.user_name === selectedStreamer.user_name">
            <el-radio-button value="current">本场</el-radio-button>
            <el-radio-button value="all">全部</el-radio-button>
          </el-radio-group>
        </div>
        
        <div class="control-right">
            <el-input
              v-model="searchText"
              placeholder="搜索歌名/用户..."
              :prefix-icon="Search"
              clearable
              style="width: 200px"
              @change="handleSearch"
            />
          </div>
      </div>
    </Teleport>

    <div v-if="!selectedStreamer && !requests.length" class="empty-state">
      <el-empty description="请选择一位主播查看点歌记录" />
    </div>
    
    <div v-else-if="requests.length === 0 && !loading" class="empty-state">
      <el-empty description="未找到点歌记录" />
    </div>

    <div v-else class="requests-content">
      <div class="stats-header">
        <div class="stats-card">
          <div class="label">点歌总数</div>
          <div class="value">{{ total }}</div>
        </div>
        <div class="stats-card">
          <div class="label">点歌人数</div>
          <div class="value">{{ uniqueUsers }}</div>
        </div>
      </div>

      <el-table 
        :data="requests" 
        style="width: 100%" 
        height="calc(100vh - 330px)"
        stripe
        :header-cell-style="{ background: 'var(--bg-secondary)', color: 'var(--text-secondary)' }"
      >
        <el-table-column type="index" :index="indexMethod" label="#" width="60" align="center" />
        
        <el-table-column label="点歌时间" min-width="160">
          <template #default="{ row }">
            <div class="time-cell">
              <span class="date">{{ formatDate(row.created_at) }}</span>
              <span class="time">{{ formatTime(row.created_at) }}</span>
            </div>
            <div class="session-info" v-if="viewMode === 'all' && row.session_title">
              <el-tag size="small" type="info" effect="plain" class="session-tag">{{ row.session_title }}</el-tag>
            </div>
          </template>
        </el-table-column>
        
        <el-table-column prop="user_name" label="点歌用户" width="140" show-overflow-tooltip />
        <el-table-column prop="song_name" label="歌名" min-width="180" show-overflow-tooltip>
          <template #default="{ row }">
            <span class="song-name">{{ row.song_name }}</span>
          </template>
        </el-table-column>
        <el-table-column prop="singer" label="歌手" width="120" show-overflow-tooltip>
          <template #default="{ row }">
            {{ row.singer || '-' }}
          </template>
        </el-table-column>
      </el-table>
      
      <div class="pagination-container" v-if="total > 0">
        <el-pagination
          v-model:current-page="currentPage"
          v-model:page-size="pageSize"
          :page-sizes="[20, 50, 100]"
          :small="isMobile"
          layout="total, sizes, prev, pager, next"
          :total="total"
          @size-change="handleSizeChange"
          @current-change="handleCurrentChange"
        />
      </div>
      

    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted, onActivated, onDeactivated, computed, watch } from 'vue';
import { useRoute } from 'vue-router';
import { useDanmakuStore } from '../stores/danmakuStore';
import { getSongRequests, getStreamers, type SongRequest, type StreamerInfo } from '../api/danmaku';
import { ElMessage } from 'element-plus';
import { Search } from '@element-plus/icons-vue';
import { VUP_LIST } from '../constants/vups';

const store = useDanmakuStore();
const route = useRoute();
const loading = ref(false);
const requests = ref<SongRequest[]>([]);
const total = ref(0);
const streamers = ref<StreamerInfo[]>([]);
const selectedStreamer = ref<StreamerInfo | null>(null);
const viewMode = ref<'current' | 'all'>('current');
const searchText = ref('');
const isMobile = ref(window.innerWidth <= 768);
const isMountedFlag = ref(false);
const currentPage = ref(1);
const pageSize = ref(20);

const isStreamerMode = computed(() => !!route.params.uid);

const handleResize = () => {
  isMobile.value = window.innerWidth <= 768;
};

const initStreamerData = async () => {
  // 检查路由参数
  if (route.params.uid) {
    selectedStreamer.value = null; // 先重置，防止残留
    requests.value = [];
    total.value = 0;
    
    const vup = VUP_LIST.find(v => v.uid === route.params.uid);
    if (vup) {
      // 1. 尝试通过名字匹配
      let streamer = streamers.value.find(s => s.user_name === vup.name);
      
      // 2. 如果名字没匹配上，尝试从 livestreamUrl 提取 room_id 匹配
      if (!streamer && vup.livestreamUrl) {
        const match = vup.livestreamUrl.match(/\/(\d+)$/);
        if (match) {
          const roomId = match[1];
          streamer = streamers.value.find(s => s.room_id?.toString() === roomId);
        }
      }

      if (streamer) {
        selectedStreamer.value = streamer;
        viewMode.value = 'all'; 
        if (store.currentSession && store.currentSession.user_name === streamer.user_name) {
          viewMode.value = 'current';
        }
        await fetchRequests();
      } else {
         ElMessage.warning(`未在后端找到主播 ${vup.name} 的数据`);
      }
    }
  } else if (store.currentSession) {
    // 兼容旧逻辑
    const current = streamers.value.find(s => s.user_name === store.currentSession?.user_name);
    if (current) {
      selectedStreamer.value = current;
      viewMode.value = 'current';
      await fetchRequests();
    }
  }
};

onMounted(async () => {
  isMountedFlag.value = true;
  window.addEventListener('resize', handleResize);
  await loadStreamers();
  await initStreamerData();
});

// 监听路由参数变化，自动刷新数据
watch(() => route.params.uid, async (newUid) => {
  if (newUid) {
    await initStreamerData();
  }
});

onUnmounted(() => {
  isMountedFlag.value = false;
  window.removeEventListener('resize', handleResize);
});

onActivated(() => {
  isMountedFlag.value = true;
});

onDeactivated(() => {
  isMountedFlag.value = false;
});

watch(() => store.currentSession, (newSession) => {
  if (newSession) {
    const current = streamers.value.find(s => s.user_name === newSession.user_name);
    if (current) {
      selectedStreamer.value = current;
      viewMode.value = 'current';
      fetchRequests();
    }
  }
});

// 搜索条件变化时重置页码并重新请求
const handleSearch = () => {
  currentPage.value = 1;
  fetchRequests();
};

const handleSizeChange = (val: number) => {
  pageSize.value = val;
  currentPage.value = 1;
  fetchRequests();
};

const handleCurrentChange = (val: number) => {
  currentPage.value = val;
  fetchRequests();
};

const indexMethod = (index: number) => {
  return (currentPage.value - 1) * pageSize.value + index + 1;
};

const uniqueUsers = computed(() => {
  // 注意：后端分页模式下，只能统计当前页的唯一用户数，或者后端返回总人数
  // 这里暂时只显示当前页的，或者如果需要总数，后端需要提供
  const users = new Set(requests.value.map(r => r.uid));
  return users.size;
});

const loadStreamers = async () => {
  try {
    streamers.value = await getStreamers();
  } catch (error) {
    console.error('Failed to load streamers:', error);
  }
};

const handleStreamerChange = () => {
  viewMode.value = 'all'; // 切换主播时默认显示全部
  fetchRequests();
};

const fetchRequests = async () => {
  // 清空现有数据
  if (!selectedStreamer.value) {
    requests.value = [];
    return;
  }
  
  loading.value = true;
  requests.value = []; // 先清空，给用户反馈

  try {
    const params: any = {
      page: currentPage.value,
      pageSize: pageSize.value
    };
    if (searchText.value) {
      params.search = searchText.value;
    }

    if (viewMode.value === 'current' && store.currentSession && store.currentSession.user_name === selectedStreamer.value.user_name) {
      // 仅获取当前场次
      params.id = store.currentSession.id;
    } else {
      // 获取该主播全部历史
      if (selectedStreamer.value.room_id) {
        params.roomId = selectedStreamer.value.room_id;
      } else {
        ElMessage.warning('无法获取该主播的历史记录（缺少房间号）');
        requests.value = [];
        total.value = 0;
        return;
      }
    }
    
    const res = await getSongRequests(params);
    
    // 检查返回结构是分页对象还是数组
    if ('list' in res) {
      requests.value = res.list;
      total.value = res.total;
    } else {
      // 理论上 getSongRequests 内部已经做了兼容，这里以防万一
      requests.value = res as unknown as SongRequest[];
      total.value = requests.value.length;
    }

  } catch (error) {
    console.error('Failed to fetch song requests:', error);
    ElMessage.error('获取点歌记录失败');
  } finally {
    loading.value = false;
  }
};

const formatDate = (timestamp: number) => {
  const date = new Date(timestamp);
  return date.toLocaleDateString('zh-CN');
};

const formatTime = (timestamp: number) => {
  const date = new Date(timestamp);
  return date.toLocaleTimeString('zh-CN', { hour12: false });
};


</script>

<style scoped>
.song-requests-container {
  height: 100%;
  overflow-y: auto;
  padding: 20px;
  background-color: var(--bg-primary);
}

.header-controls-teleported {
  display: flex;
  align-items: center;
  gap: 15px;
}

.streamer-selector {
  display: flex;
  align-items: center;
  gap: 10px;
}

.requests-content {
  max-width: 1200px;
  margin: 0 auto;
}

.stats-header {
  display: flex;
  gap: 15px;
  margin-bottom: 20px;
}

.stats-card {
  flex: 1;
  background-color: var(--bg-secondary);
  padding: 15px;
  border-radius: 8px;
  text-align: center;
}

.stats-card .label {
  font-size: 12px;
  color: var(--text-secondary);
  margin-bottom: 5px;
}

.stats-card .value {
  font-size: 20px;
  font-weight: 600;
  color: var(--text-primary);
}

.text-truncate {
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.song-name {
  font-weight: 500;
  color: var(--el-color-primary);
}

.time-cell {
  display: flex;
  flex-direction: row;
  align-items: center;
  gap: 8px;
  line-height: 1.2;
}

.time-cell .date {
  font-size: 12px;
  color: var(--text-secondary);
}

.session-info {
  margin-top: 4px;
}

.session-tag {
  max-width: 100%;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  display: inline-block;
}



.empty-state {
  padding: 40px 0;
  display: flex;
  justify-content: center;
}

@media (max-width: 768px) {
  .header-controls-teleported {
    display: flex;
    flex-direction: column;
    align-items: flex-start;
    margin-bottom: 15px;
    gap: 10px;
    width: 100%;
  }
  
  .streamer-selector {
    width: 100%;
    justify-content: space-between;
  }
  
  .control-right {
    width: 100%;
  }
  
  .control-right .el-input {
    width: 100% !important;
  }
}

.pagination-container {
  display: flex;
  justify-content: center;
  margin-top: 15px;
  padding-bottom: 10px;
}
</style>
