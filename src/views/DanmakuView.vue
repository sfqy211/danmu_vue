<template>
  <div class="main-content">
    <Sidebar class="sidebar" />
    <div 
      v-if="!store.isSidebarCollapsed" 
      class="sidebar-overlay mobile-only" 
      @click="store.isSidebarCollapsed = true"
    ></div>
    <div class="content-area">
      <Header class="app-header" />
      <div class="zoom-container">
        <div class="zoom-wrapper" :style="zoomStyle">
          <DanmakuList class="danmaku-list-container" />
        </div>
      </div>
    </div>
    
    <el-dialog
      v-model="store.isVupListVisible"
      title="VUP列表"
      fullscreen
      destroy-on-close
      append-to-body
      class="vup-list-dialog"
    >
      <VupList />
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import Sidebar from '../components/Sidebar.vue';
import Header from '../components/Header.vue';
import DanmakuList from '../components/DanmakuList.vue';
import VupList from '../components/VupList.vue';
import { useDanmakuStore } from '../stores/danmakuStore';

const store = useDanmakuStore();

const zoomStyle = computed(() => {
  const scale = store.zoomLevel / 100;
  return {
    // 使用 transform 替代 zoom 以获得更好的兼容性和更稳定的渲染效果
    transform: `scale(${scale})`,
    width: `${100 / scale}%`,
    height: `${100 / scale}%`,
  };
});
</script>

<style scoped>
/* 确保组件占据全屏且不出现外部滚动条 */
.main-content {
  height: 100vh;
  width: 100vw;
  display: flex;
  overflow: hidden;
  background-color: var(--bg-primary);
}

.sidebar {
  flex-shrink: 0;
}

.content-area {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  min-width: 0; /* 防止内容撑破 flex 容器 */
}

.zoom-container {
  flex: 1;
  overflow: hidden;
  display: flex;
  flex-direction: column;
}

.zoom-wrapper {
  display: flex;
  flex-direction: column;
  overflow: hidden;
  flex: 1;
  transform-origin: 0 0;
}

.sidebar-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background-color: rgba(0, 0, 0, 0.5);
  z-index: 999;
}

.mobile-only {
  display: none;
}

@media (max-width: 768px) {
  .mobile-only {
    display: block;
  }
}
</style>

<style>
.vup-list-dialog .el-dialog__body {
  padding: 0 !important;
  height: calc(100vh - 54px); /* 减去头部高度 */
  overflow: hidden;
  background-color: var(--bg-primary);
}
</style>
