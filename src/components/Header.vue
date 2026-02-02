<template>
  <div class="header-container">
    <div class="left-panel">
      <el-button 
        class="menu-btn mobile-only" 
        :icon="Expand" 
        circle 
        @click="store.toggleSidebar" 
        title="打开列表"
      />
      <div v-if="store.currentSession" class="session-info">
        <h2 class="session-title">{{ store.currentSession.title || '未命名直播' }}</h2>
        <a 
          v-if="store.currentSession.room_id"
          :href="`https://live.bilibili.com/${store.currentSession.room_id}`" 
          target="_blank" 
          class="room-link"
          title="点击跳转至直播间"
        >
          <el-icon><Position /></el-icon>
          <span>{{ store.currentSession.user_name }} 的直播间</span>
        </a>
      </div>
      <h2 v-else>请选择直播回放</h2>
    </div>
    
    <div class="right-panel">
      <el-button class="settings-btn" :icon="Setting" circle @click="drawerVisible = true" title="设置与工具" />
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
        <div class="drawer-section">
          <div class="section-title">搜索与筛选</div>
          <el-input
            v-model="store.searchText"
            placeholder="搜索弹幕/用户..."
            :prefix-icon="Search"
            clearable
            class="drawer-search-input"
          />
        </div>

        <el-divider />

        <!-- Navigation -->
        <div class="drawer-section">
          <div class="section-title">导航</div>
          <div class="drawer-item clickable" @click="router.push('/')">
            <div class="item-left">
              <el-icon><ArrowLeft /></el-icon>
              <span>返回歌单列表</span>
            </div>
            <el-icon><ArrowRight /></el-icon>
          </div>
        </div>

        <el-divider />

        <!-- Data & Analysis -->
        <div class="drawer-section">
          <div class="section-title">数据与分析</div>
          <div class="drawer-item clickable" @click="openStats">
            <div class="item-left">
              <el-icon><DataAnalysis /></el-icon>
              <span>弹幕发送统计</span>
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

        <el-divider />

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
        
        <!-- About -->
        <div class="drawer-section">
          <div class="drawer-item clickable" @click="aboutDialogVisible = true">
            <div class="item-left">
              <el-icon><InfoFilled /></el-icon>
              <span>关于本工具</span>
            </div>
            <el-icon><ArrowRight /></el-icon>
          </div>
        </div>
      </div>
      
      <template #footer>
        <div class="drawer-footer">
          <p>弹幕预览工具 Vue版 v1.1</p>
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
            <el-collapse-item title="v1.1 - 2026-02-02 (最新更新)" name="1.1">
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
          <p>Github主页：<a href="https://github.com/sfqy211" target="_blank">sfqy211</a></p>
        </div>
      </div>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue';
import { useRouter } from 'vue-router';
import { useDanmakuStore } from '../stores/danmakuStore';
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
  Download,
  Position
} from '@element-plus/icons-vue';
import DanmakuStats from './DanmakuStats.vue';
import TimelineAnalysis from './TimelineAnalysis.vue';

const router = useRouter();
const store = useDanmakuStore();
const statsDialogVisible = ref(false);
const timelineDialogVisible = ref(false);
const aboutDialogVisible = ref(false);
const drawerVisible = ref(false);
const isDarkMode = ref(false);
const isMobile = ref(window.innerWidth <= 768);

const handleResize = () => {
  isMobile.value = window.innerWidth <= 768;
};

const openStats = () => {
  statsDialogVisible.value = true;
};

const openTimeline = () => {
  timelineDialogVisible.value = true;
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
  flex-direction: column;
  gap: 4px;
}
.session-title {
  color: var(--text-primary);
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

.drawer-search-input {
  margin-bottom: 10px;
}

.mobile-only {
  display: none;
}

@media (max-width: 768px) {
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

.changelog-list li {
    font-size: 13px;
    margin-bottom: 6px;
    line-height: 1.5;
}

.changelog-list li strong {
    color: var(--text-primary);
}
</style>
