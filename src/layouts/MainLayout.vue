<template>
  <div class="main-content">
    <!-- 动态背景层 (全局共享) -->
    <div class="dynamic-bg" v-if="showDynamicBg">
      <!-- Desktop / Fallback Image -->
      <div 
        class="bg-image" 
        :style="bgImageStyle"
        :class="{ 'hidden-on-mobile': showMeshGradient }"
      ></div>

      <!-- Mobile Mesh Gradient -->
      <div class="mesh-gradient" v-if="showMeshGradient">
        <div class="mesh-blob blob-1" :style="{ backgroundColor: meshColors[0] }"></div>
        <div class="mesh-blob blob-2" :style="{ backgroundColor: meshColors[1] }"></div>
        <div class="mesh-blob blob-3" :style="{ backgroundColor: meshColors[2] }"></div>
        <div class="mesh-blob blob-4" :style="{ backgroundColor: meshColors[3] || meshColors[0] }"></div>
        <div class="mesh-blob blob-5" :style="{ backgroundColor: meshColors[4] || meshColors[1] }"></div>
      </div>
    </div>
    <div class="bg-overlay" v-if="showDynamicBg"></div>

    <Sidebar v-if="showSidebar" class="sidebar" />
    <div class="content-area">
      <Header v-if="showHeader" class="app-header" />
      <div class="zoom-container">
        <div class="zoom-wrapper" :style="zoomStyle">
          <router-view v-slot="{ Component }">
            <keep-alive>
              <component :is="Component" />
            </keep-alive>
          </router-view>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, ref, onMounted, onUnmounted, watch } from 'vue';
import { useRoute } from 'vue-router';
import Sidebar from '../components/Sidebar.vue';
import Header from '../components/Header.vue';
import { useDanmakuStore } from '../stores/danmakuStore';

const store = useDanmakuStore();
const route = useRoute();

onMounted(() => {
  store.initVupSelection();
});

// ===== 动态背景逻辑 =====
const mql = window.matchMedia('(max-width: 768px)');
const isMobile = ref(mql.matches);

const updateMobile = (e: MediaQueryListEvent) => {
  isMobile.value = e.matches;
};

// 监听移动端状态变化，自动收起侧边栏
watch(isMobile, (newValue) => {
  if (newValue) {
    store.isSidebarCollapsed = true;
  }
});

onMounted(() => {
  mql.addEventListener('change', updateMobile);
});

onUnmounted(() => {
  mql.removeEventListener('change', updateMobile);
});

// 仅在主页和列表页显示动态背景
const showDynamicBg = computed(() => {
  return route.name === 'home' || route.name === 'vup-list';
});

const currentVup = computed(() => store.currentVup);

const showMeshGradient = computed(() => {
  return isMobile.value && currentVup.value?.themeColors && currentVup.value.themeColors.length >= 3;
});

const meshColors = computed(() => {
  const colors = currentVup.value?.themeColors || [];
  if (colors.length >= 9) {
    return [colors[0], colors[1], colors[2], colors[3], colors[4]];
  }
  if (colors.length > 0) {
    return [
      colors[0], 
      colors[1] || colors[0], 
      colors[2] || colors[0], 
      colors[0], 
      colors[1] || colors[0]
    ];
  }
  return ['#000', '#000', '#000', '#000', '#000'];
});

const bgImageStyle = computed(() => {
  if (!currentVup.value) return {};
  const imgUrl = currentVup.value.coverUrl || currentVup.value.imageUrl;
  return {
    backgroundImage: `url(${imgUrl})`
  };
});
// ========================

const showSidebar = computed(() => {
  // 在主播专属页（包含子路由）显示 Sidebar
  return route.path.startsWith('/vup/');
});

const showHeader = computed(() => {
  // 在主页和列表页隐藏 Header
  return route.name !== 'home' && route.name !== 'vup-list';
});

const zoomStyle = computed(() => {
  // 仅在弹幕列表页应用缩放
  if (route.name !== 'streamer-danmaku') {
    return {
      width: '100%',
      height: '100%'
    };
  }
  
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
  position: relative;
  z-index: 10;
}

.content-area {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  min-width: 0; /* 防止内容撑破 flex 容器 */
  position: relative;
  z-index: 2;
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

.mobile-only {
  display: none;
}

@media (max-width: 768px) {
  .mobile-only {
    display: block;
  }
  
  /* Mobile Dynamic Bg Adjustments */
  .dynamic-bg {
    background-position: 50% 20%;
  }

  .bg-image {
    filter: blur(20px);
    transform: scale(1.1);
  }

  /* 当显示网格渐变时，隐藏底层图片，避免干扰 */
  .bg-image.hidden-on-mobile {
    opacity: 0;
  }
}

/* Dynamic Background Styles */
.dynamic-bg {
  position: fixed;
  inset: 0;
  z-index: 0;
  overflow: hidden;
  pointer-events: none;
}

.bg-image {
  position: absolute;
  width: 100%;
  height: 100%;
  background-size: cover;
  background-position: center;
  filter: blur(4px) brightness(0.8) saturate(1.1);
  transform: scale(1.02);
  transition: background-image 0.8s ease;
  z-index: 0;
}

.mesh-gradient {
  position: absolute;
  top: 0;
  left: 0;
  width: 100%;
  height: 100%;
  overflow: hidden;
  background-color: transparent;
  z-index: 1;
}

.mesh-blob {
  position: absolute;
  border-radius: 50%;
  filter: blur(80px);
  opacity: 0.8;
  mix-blend-mode: normal;
}

.blob-1 { top: -10%; left: -10%; width: 70%; height: 70%; }
.blob-2 { top: -10%; right: -10%; width: 60%; height: 60%; }
.blob-3 { bottom: -10%; left: -10%; width: 70%; height: 70%; }
.blob-4 { bottom: -5%; right: -5%; width: 65%; height: 65%; }
.blob-5 { top: 35%; left: 35%; width: 50%; height: 50%; }

.bg-overlay {
  position: fixed;
  inset: 0;
  background: linear-gradient(
    180deg,
    rgba(0, 0, 0, 0.2) 0%,
    rgba(0, 0, 0, 0.1) 40%,
    rgba(0, 0, 0, 0.3) 100%
  );
  z-index: 2;
  pointer-events: none;
}
</style>