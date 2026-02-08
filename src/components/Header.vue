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
        @click="router.push('/')" 
        title="返回首页"
      />

      <h2 v-if="!isDanmakuPage" class="static-title">{{ pageTitle }}</h2>
      
      <template v-else>
        <div v-if="store.currentSession" class="session-info">
          <span class="streamer-name">{{ store.currentSession.user_name }}</span>
          <span class="divider">/</span>
          <span class="session-date">{{ formatDate(store.currentSession.start_time) }}</span>
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
          <div class="drawer-item clickable" @click="openAiAnalysis">
            <div class="item-left">
              <el-icon><MagicStick /></el-icon>
              <span>弹幕AI分析</span>
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
              <el-icon><ZoomIn /></el-icon>
              <span>页面缩放</span>
            </div>
            <div class="zoom-control">
              <el-slider v-model="store.zoomLevel" :min="80" :max="150" :step="5" :format-tooltip="(val: number) => val + '%'" @input="(val: number) => handleZoom(val)" style="width: 100px;" />
              <span class="zoom-value">{{ store.zoomLevel }}%</span>
            </div>
          </div>
        </div>
        
        <el-divider />

        <!-- About & System -->
        <div class="drawer-section">
          <div class="drawer-item clickable" @click="aboutDialogVisible = true">
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
          <p>弹幕预览工具 Vue版 v1.2</p>
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

    <!-- AI Analysis Dialog -->
    <el-dialog
      v-model="aiAnalysisDialogVisible"
      title="弹幕 AI 分析"
      :width="isMobile ? '100%' : '70%'"
      :fullscreen="isMobile"
      destroy-on-close
      align-center
      append-to-body
    >
      <AiAnalysis v-if="aiAnalysisDialogVisible" />
    </el-dialog>

    <!-- About Dialog -->
    <el-dialog
      v-model="aboutDialogVisible"
      title="关于本工具"
      :width="isMobile ? '90%' : '550px'"
      align-center
      append-to-body
      class="about-dialog"
    >
      <div class="about-content">
        <div class="about-section">
          <h3>项目介绍</h3>
          <p>这是一个专为 Bilibili 直播回放设计的弹幕预览与数据分析工具，旨在提供极致的复盘体验。</p>
          <p>核心功能：</p>
          <ul>
            <li><strong>弹幕回放与筛选</strong>：支持按需加载海量弹幕，提供精准的用户/内容搜索与筛选功能。</li>
            <li><strong>发送数据统计</strong>：可视化展示弹幕发送排行，支持按发送频次过滤，可一键导出或复制统计图表。</li>
            <li><strong>全场热度分布</strong>：基于整场直播的时间轴统计，直观呈现直播过程中的弹幕高能时刻。</li>
            <li><strong>跨端响应式布局</strong>：完美适配网页与手机端，网页端左右分栏提升效率，手机端上下布局优化观感。</li>
          </ul>
        </div>
        
        <el-divider />

        <div class="about-section">
          <h3>更新日志</h3>
          <el-collapse class="changelog-collapse">
            <el-collapse-item title="v1.2 - 2026-02-03 (最新更新)" name="1.2">
              <ul class="changelog-list">
                <li><strong>组件重构与复用</strong>：抽象并统一了统计逻辑，封装为通用的数据统计组件，提升代码可维护性。</li>
                <li><strong>营收统计深度优化</strong>：新增营收统计功能，支持按金额筛选和用户排行，并修复了数据丢失与计算精度问题。</li>
                <li><strong>交互体验升级</strong>：统计图表标签支持随滑动条实时动态更新，扇形图改为实心样式，视觉效果更扎实。</li>
                <li><strong>配置灵活性提升</strong>：支持为不同类型的统计设置独立的默认显示项数。</li>
                <li><strong>准备添加AI分析</strong>：预留接口，未来版本将集成基于弹幕内容的智能分析功能。</li>
              </ul>
            </el-collapse-item>
            <el-collapse-item title="v1.1 - 2026-02-02" name="1.1">
              <ul class="changelog-list">
                <li><strong>自动化部署上线</strong>：成功配置 GitHub Actions，实现代码推送后自动编译、上传并重启服务。</li>
                <li><strong>手机端图表优化</strong>：弹幕时间轴分析图表在手机端支持强制横屏旋转显示，提升观看体验，增加扇形图显示。</li>
                <li><strong>侧边栏按需加载</strong>：默认不加载回放列表，仅在选择主播后加载，大幅减少初始流量消耗。</li>
                <li><strong>界面细节优化</strong>：手机端统计弹窗支持全屏显示，优化了 UID 的展示方式。</li>
                <li><strong>交互修复</strong>：修复了手机端侧边栏宽度调整条无法拖动的问题。</li>
              </ul>
            </el-collapse-item>
            <el-collapse-item title="v1.0 - 2026-01-20" name="1.0">
              <ul class="changelog-list">
                <li><strong>基础功能上线</strong>：支持弹幕列表查看、主播筛选、基本的统计分析。</li>
                <li><strong>可视化集成</strong>：集成 ECharts 实现柱状图统计。</li>
                <li><strong>导出功能</strong>：支持统计图表导出为图片或复制到剪贴板。</li>
              </ul>
            </el-collapse-item>
          </el-collapse>
        </div>
        
        <el-divider />
        
        <div class="about-section">
          <h3>个人介绍</h3>
          <p>B站主页：<a href="https://space.bilibili.com/182587768" target="_blank">朔风秋叶</a></p>
          <p>项目主页：<a href="https://github.com/sfqy211/danmu_vue" target="_blank">danmu_vue</a></p>
        </div>
      </div>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref , onMounted, onUnmounted, watch, computed } from 'vue';
import { useRouter, useRoute } from 'vue-router';
import { useDanmakuStore } from '../stores/danmakuStore';
import { VUP_LIST } from '../constants/vups';
import { ElMessage } from 'element-plus';
import { 
  Search, 
  Setting, 
  DataAnalysis, 
  InfoFilled, 
  ArrowRight, 
  ArrowLeft,
  MagicStick,
  Expand,
  Moon, 
  ZoomIn,
  Histogram,
  Monitor,
  CircleCheckFilled,
  WarningFilled,
  Wallet,
  Headset,
  ChatDotRound
} from '@element-plus/icons-vue';
import DanmakuStats from './DanmakuStats.vue';
import RevenueStats from './RevenueStats.vue';
import TimelineAnalysis from './TimelineAnalysis.vue';
import AiAnalysis from './AiAnalysis.vue';

const store = useDanmakuStore();
const router = useRouter();
const route = useRoute();
const statsDialogVisible = ref(false);
const revenueDialogVisible = ref(false);
const timelineDialogVisible = ref(false);
const aiAnalysisDialogVisible = ref(false);
const aboutDialogVisible = ref(false);
const drawerVisible = ref(false);
const isDarkMode = ref(false);
const isMobile = ref(window.innerWidth <= 768);
const pm2Status = ref<'success' | 'error' | 'loading'>('loading');

const isHome = computed(() => route.name === 'home'); // 首页是 VupList
const isStreamerPage = computed(() => route.path.startsWith('/vup/')); // 主播页
const isDanmakuPage = computed(() => route.name === 'streamer-danmaku'); // 弹幕列表页
const showSidebarToggle = computed(() => {
  return route.path.startsWith('/vup/') && route.name !== 'streamer-songs';
});

const currentStreamerName = computed(() => {
  if (route.params.uid) {
    const vup = VUP_LIST.find(v => v.uid === route.params.uid);
    return vup ? vup.name : '';
  }
  return '';
});

const pageTitle = computed(() => {
  if (route.name === 'home') return '主页'; // 首页标题
  if (route.name === 'vup-list') return 'VUP 列表';
  if (route.name === 'streamer-songs') return '点歌历史';
  return '';
});

const formatDate = (timestamp: number) => {
  if (!timestamp) return '';
  const date = new Date(timestamp);
  return `${date.getMonth() + 1}-${date.getDate()}`;
};

const checkPm2Status = async () => {
  pm2Status.value = 'loading';
  try {
    // 假设我们在同一域名下，或者配置了代理
    const res = await fetch('/api/pm2-status');
    if (!res.ok) throw new Error('Network response was not ok');
    const data = await res.json();
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
  const currentUid = route.params.uid;
  if (currentUid) {
    // 如果在主播页，"弹幕列表"应该跳转到该主播的弹幕列表页
    router.push({ name: 'streamer-danmaku', params: { uid: currentUid } });
  } else {
    // 只有在没有 UID 的情况下才回首页（理论上在 Header 显示时不会发生，除非在非 vup 路由）
    router.push({ name: 'home' });
  }
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

const openAiAnalysis = () => {
  if (!store.currentSession) {
    ElMessage.warning('请在左侧直播回放中选择主播与具体场次');
    return;
  }
  aiAnalysisDialogVisible.value = true;
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
}
.back-btn {
  margin-right: 5px;
}
.left-panel h2 {
  font-size: 18px;
  margin: 0;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 400px;
}
.session-info {
  display: flex;
  flex-direction: row;
  align-items: center;
  gap: 4px;
  font-size: 14px;
}
.streamer-name {
  font-weight: 600;
  color: var(--text-primary);
}
.divider {
  color: var(--text-tertiary);
}
.session-date {
  color: var(--text-secondary);
}
.session-title {
  color: var(--text-primary);
  font-weight: 500;
}
.static-title {
  color: var(--text-primary);
  font-weight: 600;
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
  .session-title {
    font-size: 14px;
    max-width: 150px;
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
    color: var(--accent);
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

.about-content {
    padding: 0 5px;
}
.about-section h3 {
    margin-top: 0;
    margin-bottom: 12px;
    font-size: 16px;
    color: var(--text-primary);
}
.about-section p {
    margin-bottom: 10px;
    line-height: 1.6;
    color: var(--text-secondary);
    font-size: 14px;
}
.about-section ul {
    padding-left: 20px;
    margin-bottom: 10px;
    color: var(--text-secondary);
    font-size: 14px;
}
.about-section li {
    margin-bottom: 5px;
}

.changelog-collapse {
    border: none;
    margin-top: 10px;
}

:deep(.changelog-collapse .el-collapse-item__header) {
    font-size: 14px;
    font-weight: 500;
    color: var(--text-primary);
    background-color: transparent;
    height: 40px;
}

:deep(.changelog-collapse .el-collapse-item__content) {
    padding-bottom: 10px;
    color: var(--text-secondary);
}

.changelog-list {
    margin: 0;
    padding-left: 18px;
    list-style-type: disc;
}

.status-icon {
  font-size: 18px;
}
.status-icon.success {
  color: var(--el-color-success);
}
.status-icon.error {
  color: var(--el-color-danger);
}
.status-icon.loading {
  color: var(--text-tertiary);
  opacity: 0.5;
}

.changelog-list li {
    font-size: 13px;
    margin-bottom: 6px;
    line-height: 1.5;
}

.changelog-list li strong {
    color: var(--text-primary);
}
</style>
