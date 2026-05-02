<template>
  <div class="header-container">
    <div class="left-panel">
      <el-button 
        v-if="showSidebarToggle"
        class="menu-btn mobile-only" 
        :icon="Expand" 
        circle 
        @click="store.toggleSidebar" 
        title="打开列表"
      />
      
      <!-- 首页显示返回按钮(如果不在首页) -->
      <el-button 
        v-if="isStreamerPage"
        class="back-btn" 
        :icon="ArrowLeft" 
        circle 
        @click="goHome" 
        title="返回首页"
      />

      <h2 v-if="!isDanmakuPage" class="static-title">{{ pageTitle }}</h2>
      
      <template v-else>
        <div v-if="store.currentSession" class="session-info">
          <span class="streamer-name">{{ store.currentSession.user_name }}</span>
          <span class="divider">/</span>
          <span class="session-date">{{ formatShortDate(store.currentSession.start_time) }}</span>
          <span class="session-title" :title="store.currentSession.title">{{ store.currentSession.title }}</span>
        </div>
        <h2 v-else class="static-title">{{ currentStreamerName || '请选择回放' }}</h2>
      </template>
    </div>
    
    <div class="right-panel">
      <div id="header-dynamic-actions" class="dynamic-actions"></div>
      <div class="header-search-container">
        <el-input
          v-if="isDanmakuPage"
          v-model="store.searchText"
          placeholder="搜索弹幕/用户..."
          :prefix-icon="Search"
          clearable
          class="header-search-input"
        />
      </div>
      <el-button v-if="!isHome" class="settings-btn" :icon="Setting" circle @click="drawerVisible = true" title="设置与工具" />
    </div>

    <!-- Settings Drawer -->
    <el-drawer
      v-model="drawerVisible"
      title="设置与工具"
      direction="rtl"
      size="320px"
      class="settings-drawer"
      :append-to-body="true"
    >
      <div class="drawer-content">
        <!-- Search & Filter -->
        <div class="drawer-section mobile-drawer-search">
          <div class="section-title">搜索与筛选</div>
          <el-input
            v-model="store.searchText"
            placeholder="搜索弹幕/用户..."
            :prefix-icon="Search"
            clearable
            class="drawer-search-input"
          />
        </div>

        <el-divider class="mobile-drawer-search" />

        <!-- Navigation -->
        <div class="drawer-section">
          <div class="section-title">导航</div>
          <div class="drawer-item clickable" @click="goHome">
            <div class="item-left">
              <el-icon><ChatDotRound /></el-icon>
              <span>弹幕列表</span>
            </div>
            <el-icon><ArrowRight /></el-icon>
          </div>
          <div class="drawer-item clickable" @click="openSongRequests">
            <div class="item-left">
              <el-icon><Headset /></el-icon>
              <span>点歌历史</span>
            </div>
            <el-icon><ArrowRight /></el-icon>
          </div>
        </div>

        <el-divider />

        <!-- Data & Analysis -->
        <div class="drawer-section" v-if="isDanmakuPage">
          <div class="section-title">数据与分析</div>
          <div class="drawer-item clickable" @click="openStats">
            <div class="item-left">
              <el-icon><DataAnalysis /></el-icon>
              <span>弹幕发送统计</span>
            </div>
            <el-icon><ArrowRight /></el-icon>
          </div>
          <div class="drawer-item clickable" @click="openRevenue">
            <div class="item-left">
              <el-icon><Wallet /></el-icon>
              <span>营收统计</span>
            </div>
            <el-icon><ArrowRight /></el-icon>
          </div>
          <div class="drawer-item clickable" @click="openTimeline">
            <div class="item-left">
              <el-icon><Histogram /></el-icon>
              <span>弹幕时间轴分布</span>
            </div>
            <el-icon><ArrowRight /></el-icon>
          </div>

        </div>

        <el-divider v-if="isDanmakuPage" />

        <!-- Interface Settings -->
        <div class="drawer-section">
          <div class="section-title">界面设置</div>
          <div class="drawer-item">
            <div class="item-left">
              <el-icon><Moon /></el-icon>
              <span>深色模式</span>
            </div>
            <el-switch v-model="isDarkMode" @change="toggleTheme" />
          </div>
          <div class="drawer-item">
            <div class="item-left">
              <el-icon><Clock /></el-icon>
              <span>显示实际时间</span>
            </div>
            <el-switch 
              v-model="store.timeDisplayMode" 
              active-value="absolute" 
              inactive-value="relative"
            />
          </div>
          <div class="drawer-item">
            <div class="item-left">
              <el-icon><ZoomIn /></el-icon>
              <span>页面缩放</span>
            </div>
            <div class="zoom-control">
              <el-slider v-model="store.zoomLevel" :min="70" :max="160" :step="5" :format-tooltip="(val: number) => val + '%'" @input="(val: number) => handleZoom(val)" style="width: 100px;" />
              <span class="zoom-value">{{ store.zoomLevel }}%</span>
            </div>
          </div>
        </div>
        
        <el-divider />

        <!-- About & System -->
        <div class="drawer-section">
          <div class="drawer-item clickable" @click="aboutDialogVisible = true; loadChangelog()">
            <div class="item-left">
              <el-icon><InfoFilled /></el-icon>
              <span>关于本工具</span>
            </div>
            <el-icon><ArrowRight /></el-icon>
          </div>
          <div class="drawer-item">
            <div class="item-left">
              <el-icon><Monitor /></el-icon>
              <span>后端服务状态</span>
            </div>
            <el-icon class="status-icon" :class="pm2Status">
              <component :is="pm2Status === 'error' ? WarningFilled : CircleCheckFilled" />
            </el-icon>
          </div>
        </div>
      </div>
      
      <template #footer>
        <div class="drawer-footer">
          <p>BiliDanmu 弹幕时光机 v2.3</p>
        </div>
      </template>
    </el-drawer>

    <!-- Stats Dialog -->
    <el-dialog
      v-model="statsDialogVisible"
      title="弹幕发送统计"
      :width="isMobile ? '100%' : '70%'"
      :fullscreen="isMobile"
      destroy-on-close
      align-center
      append-to-body
    >
      <DanmakuStats v-if="statsDialogVisible" />
    </el-dialog>

    <!-- Revenue Dialog -->
    <el-dialog
      v-model="revenueDialogVisible"
      title="营收统计"
      :width="isMobile ? '100%' : '70%'"
      :fullscreen="isMobile"
      destroy-on-close
      align-center
      append-to-body
    >
      <RevenueStats v-if="revenueDialogVisible" />
    </el-dialog>

    <!-- Timeline Dialog -->
    <el-dialog
      v-model="timelineDialogVisible"
      title="弹幕时间轴分布"
      :width="isMobile ? '100%' : '70%'"
      :fullscreen="isMobile"
      destroy-on-close
      align-center
      append-to-body
    >
      <TimelineAnalysis v-if="timelineDialogVisible" />
    </el-dialog>



<!-- About Dialog -->
    <el-dialog
      v-model="aboutDialogVisible"
      title="更新日志"
      :width="isMobile ? '90%' : '520px'"
      align-center
      append-to-body
      class="about-dialog"
    >
      <div class="changelog-scroll">
        <div v-if="changelogLoading" style="text-align: center; padding: 40px 0; color: var(--text-tertiary);">
          加载中...
        </div>
        <div v-else-if="changelogError" style="text-align: center; padding: 40px 0; color: var(--text-tertiary);">
          {{ changelogError }}
        </div>
        <div v-else-if="changelogEntries.length === 0" style="text-align: center; padding: 40px 0; color: var(--text-tertiary);">
          暂无更新日志
        </div>
        <template v-else>
          <div class="changelog-version" v-for="(entry, idx) in changelogEntries" :key="entry.id">
            <div class="version-header">
              <span class="version-badge" :class="{ new: idx === 0 }">{{ entry.version }}</span>
              <span class="version-date">{{ formatDate(entry.date) }}</span>
            </div>
            <ul class="changelog-list">
              <li v-for="(line, lineIdx) in entry.content.split('\n').filter((l: string) => l.trim())" :key="lineIdx">
                {{ line }}
              </li>
            </ul>
          </div>
        </template>
      </div>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref , onMounted, onUnmounted, watch, computed } from 'vue';
import { useRouter, useRoute } from 'vue-router';
import { useDanmakuStore } from '../stores/danmakuStore';
import { getChangelog, type ChangelogEntry } from '../api/danmaku';
import { getPm2Status } from '../api/danmaku';
import { ElMessage } from 'element-plus';
import { 
  Search, 
  Setting, 
  DataAnalysis, 
  InfoFilled, 
  ArrowRight, 
  ArrowLeft,
  Expand,
  Moon, 
  ZoomIn,
  Histogram,
  Monitor,
  CircleCheckFilled,
  WarningFilled,
  Wallet,
  Headset,
  ChatDotRound,
  Clock
} from '@element-plus/icons-vue';
import DanmakuStats from './DanmakuStats.vue';
import RevenueStats from './RevenueStats.vue';
import TimelineAnalysis from './TimelineAnalysis.vue';

const store = useDanmakuStore();
const router = useRouter();
const route = useRoute();

const statsDialogVisible = ref(false);
const revenueDialogVisible = ref(false);
const timelineDialogVisible = ref(false);
const aboutDialogVisible = ref(false);
const changelogEntries = ref<ChangelogEntry[]>([]);
const changelogLoading = ref(false);
const changelogError = ref('');

const loadChangelog = async () => {
  changelogLoading.value = true;
  changelogError.value = '';
  try {
    changelogEntries.value = await getChangelog();
  } catch (e: any) {
    changelogError.value = '加载失败，请稍后重试';
  } finally {
    changelogLoading.value = false;
  }
};

const formatDate = (dateStr: string) => {
  if (!dateStr) return '';
  const d = new Date(dateStr);
  if (isNaN(d.getTime())) return dateStr;
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
};

const drawerVisible = ref(false);
const isDarkMode = ref(false);
const isMobile = ref(window.innerWidth <= 768);
const pm2Status = ref<'success' | 'error' | 'loading'>('loading');

const isHome = computed(() => route.name === 'home'); // 首页是 VupList
const isStreamerPage = computed(() => route.path.startsWith('/vup/')); // 主播页
const isDanmakuPage = computed(() => route.name === 'streamer-danmaku'); // 弹幕列表页
const showSidebarToggle = computed(() => {
  return route.path.startsWith('/vup/');
});

const routeUid = computed(() => {
  const uid = route.params.uid;
  if (Array.isArray(uid)) return uid[0];
  return typeof uid === 'string' ? uid : '';
});

const currentStreamerName = computed(() => {
  return store.getVupByUid(routeUid.value)?.name || '';
});

const pageTitle = computed(() => {
  if (route.name === 'home') return '主页'; // 首页标题
  if (route.name === 'vup-list') return 'VUP 列表';
  if (route.name === 'streamer-songs') return '点歌历史';
  return '';
});

const formatShortDate = (timestamp: number) => {
  if (!timestamp) return '';
  const date = new Date(timestamp);
  return `${date.getMonth() + 1}-${date.getDate()}`;
};

const checkPm2Status = async () => {
  pm2Status.value = 'loading';
  try {
    const data = await getPm2Status();
    pm2Status.value = data.status === 'success' ? 'success' : 'error';
  } catch (e) {
    console.error('Failed to check PM2 status:', e);
    pm2Status.value = 'error';
  }
};

watch(drawerVisible, (val) => {
  if (val) {
    checkPm2Status();
  }
});

const handleResize = () => {
  isMobile.value = window.innerWidth <= 768;
};

const goHome = () => {
  // Use replace to prevent history stack buildup when returning to home
  // This ensures that the back button logic works as expected (exiting the app or going to the previous external page)
  router.replace({ name: 'home' });
  drawerVisible.value = false;
};

const openStats = () => {
  if (!store.currentSession) {
    ElMessage.warning('请在左侧直播回放中选择主播与具体场次');
    return;
  }
  statsDialogVisible.value = true;
};

const openRevenue = () => {
  if (!store.currentSession) {
    ElMessage.warning('请在左侧直播回放中选择主播与具体场次');
    return;
  }
  revenueDialogVisible.value = true;
};

const openTimeline = () => {
  if (!store.currentSession) {
    ElMessage.warning('请在左侧直播回放中选择主播与具体场次');
    return;
  }
  timelineDialogVisible.value = true;
};



const openSongRequests = () => {
  const currentUid = route.params.uid;
  if (currentUid) {
    router.push({ name: 'streamer-songs', params: { uid: currentUid } });
  }
  drawerVisible.value = false;
};

const toggleTheme = (val: boolean) => {
  if (val) {
    document.documentElement.classList.add('dark-mode');
    document.documentElement.classList.add('dark');
  } else {
    document.documentElement.classList.remove('dark-mode');
    document.documentElement.classList.remove('dark');
  }
};

const handleZoom = (val: number) => {
  store.setZoomLevel(val);
};

onMounted(() => {
    isDarkMode.value = document.documentElement.classList.contains('dark-mode') || document.documentElement.classList.contains('dark');
    window.addEventListener('resize', handleResize);
});

onUnmounted(() => {
  window.removeEventListener('resize', handleResize);
});
</script>

<style scoped>
.header-container {
  display: flex;
  justify-content: space-between;
  align-items: center;
  width: 100%;
}
.left-panel {
  display: flex;
  align-items: center;
  gap: 15px;
  flex: 1;
  min-width: 0;
  overflow: hidden;
}
.back-btn {
  margin-right: 5px;
  flex-shrink: 0;
}
.left-panel h2 {
  font-size: 18px;
  margin: 0;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  flex: 1;
  min-width: 0;
}
.session-info {
  display: flex;
  flex-direction: row;
  align-items: center;
  gap: 4px;
  font-size: 14px;
  overflow: hidden;
  white-space: nowrap;
  flex: 1;
  min-width: 0;
}
.streamer-name {
  font-weight: 600;
  color: var(--text-primary);
  white-space: nowrap;
}
.divider {
  color: var(--text-tertiary);
  flex-shrink: 0;
}
.session-date {
  color: var(--text-secondary);
  flex-shrink: 0;
}
.session-title {
  color: var(--text-primary);
  font-weight: 500;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.static-title {
  color: var(--text-primary);
  font-weight: 600;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.room-link {
  display: flex;
  align-items: center;
  gap: 4px;
  font-size: 13px;
  color: var(--el-color-primary);
  text-decoration: none;
  width: fit-content;
  transition: opacity 0.2s;
  flex-shrink: 0;
}
.room-link:hover {
  opacity: 0.8;
  text-decoration: underline;
}
.room-link .el-icon {
  font-size: 14px;
}
.right-panel {
  display: flex;
  align-items: center;
  gap: 15px;
  flex-shrink: 0;
  margin-left: 10px;
}
.dynamic-actions {
  display: flex;
  align-items: center;
  gap: 15px;
}

.drawer-search-input {
  margin-bottom: 10px;
}

.header-search-container {
  display: flex;
  align-items: center;
}

.header-search-input {
  width: 220px;
  transition: width 0.3s;
}

.header-search-input:focus-within {
  width: 280px;
}

.mobile-drawer-search {
  display: none;
}

.mobile-only {
  display: none;
}

@media (max-width: 768px) {
  .header-search-input {
    display: none;
  }
  .mobile-drawer-search {
    display: block;
  }
  .mobile-only {
    display: inline-flex;
  }
  
  .left-panel {
    gap: 8px;
  }

  .streamer-name {
    max-width: 80px;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .session-title {
    font-size: 14px;
    max-width: 120px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .room-link {
    display: none; /* 手机端隐藏直播间链接以节省空间 */
  }
}

/* Drawer Styles */
:deep(.el-drawer) {
  background-color: var(--bg-primary);
}
:deep(.el-drawer__header) {
  margin-bottom: 0;
  padding-bottom: 16px;
  border-bottom: 1px solid var(--border);
  background-color: var(--bg-primary);
  color: var(--text-primary);
}
:deep(.el-drawer__title) {
  color: var(--text-primary);
  font-weight: 600;
}
.drawer-content {
  padding: 0 10px;
}
.section-title {
    font-size: 13px;
    color: var(--text-tertiary);
    margin-bottom: 12px;
    font-weight: 600;
    text-transform: uppercase;
    letter-spacing: 0.5px;
}
.drawer-item {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 10px 0;
    font-size: 14px;
    color: var(--text-primary);
}
.drawer-item.clickable {
    cursor: pointer;
    transition: color 0.2s;
}
.drawer-item.clickable:hover {
    color: var(--el-color-primary);
}

.drawer-item.clickable:hover .item-left {
    color: var(--el-color-primary);
}

.item-left {
    display: flex;
    align-items: center;
    gap: 10px;
}

.zoom-control {
    display: flex;
    align-items: center;
    gap: 10px;
}
.zoom-value {
    font-size: 12px;
    color: var(--text-secondary);
    font-weight: 600;
    width: 35px;
    text-align: right;
}
.drawer-footer {
    text-align: center;
    color: var(--text-tertiary);
    font-size: 12px;
}

:deep(.el-divider--horizontal) {
    margin: 16px 0;
}

.about-dialog .el-dialog__body {
  padding: 0 !important;
}

.changelog-scroll {
  max-height: 60vh;
  overflow-y: auto;
  padding: 16px 20px;
  overscroll-behavior: contain;
}

.changelog-version {
  margin-bottom: 20px;
}

.changelog-version:last-child {
  margin-bottom: 0;
}

.version-header {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 8px;
  padding-bottom: 6px;
  border-bottom: 1px solid var(--border);
}

.version-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 2px 10px;
  border-radius: 4px;
  font-size: 13px;
  font-weight: 700;
  background-color: var(--el-color-primary-light-9);
  color: var(--el-color-primary);
  flex-shrink: 0;
  letter-spacing: 0.3px;
}

.version-badge.new {
  background: linear-gradient(135deg, var(--el-color-primary), var(--el-color-primary-dark-2));
  color: #fff;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15);
}

.version-date {
  font-size: 12px;
  color: var(--text-tertiary);
  flex-shrink: 0;
}

.changelog-list {
  margin: 0;
  padding-left: 18px;
  list-style-type: disc;
}

.changelog-list li {
  font-size: 13px;
  margin-bottom: 5px;
  line-height: 1.5;
  color: var(--text-secondary);
}

.changelog-list li strong {
  color: var(--text-primary);
}
</style>
