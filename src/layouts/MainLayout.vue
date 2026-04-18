<template>
  <div class="main-content">
    <div class="dynamic-bg" v-if="showDynamicBg">
      <div class="bg-image" :style="bgImageStyle"></div>
    </div>
    <div class="bg-overlay" v-if="showDynamicBg"></div>

    <Sidebar v-if="showSidebar" class="sidebar" />
    <div class="content-area">
      <Header v-if="showHeader" class="app-header" />
      <div class="zoom-container">
        <div class="zoom-wrapper" :style="zoomStyle">
          <router-view v-slot="{ Component }">
            <component :is="Component" />
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

const resolveRouteUid = () => {
  const uid = route.params.uid;
  if (Array.isArray(uid)) return uid[0];
  return typeof uid === 'string' ? uid : undefined;
};

watch(
  () => route.params.uid,
  async () => {
    await store.initVupSelection(resolveRouteUid());
  },
  { immediate: true }
);

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
