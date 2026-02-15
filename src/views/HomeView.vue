<template>
  <div class="home-carousel-container" @wheel="handleWheel">
    <el-carousel 
      ref="carouselRef"
      trigger="click" 
      :autoplay="false" 
      arrow="never"
      height="100vh"
      direction="vertical"
      indicator-position="none"
      class="full-screen-carousel"
      @change="handleSlideChange"
    >
      <!-- 主播卡片页 -->
      <el-carousel-item v-for="streamer in featuredStreamers" :key="streamer.uid">
        <div class="carousel-slide">
          <!-- 背景层：模糊处理 -->
          <div class="slide-background" :style="{ backgroundImage: `url(${streamer.coverUrl || streamer.imageUrl})` }"></div>
          
          <!-- 内容层：清晰展示 -->
          <div class="slide-content">
            <div class="image-card">
              <img 
                :src="streamer.coverUrl || streamer.imageUrl" 
                :alt="streamer.name" 
                class="streamer-avatar" 
                @click="openLivestream(streamer.livestreamUrl)"
                @error="handleImageError($event, streamer.imageUrl)"
              />
            </div>
            
            <div class="info-container">
              <h1 class="streamer-name">{{ streamer.name }}</h1>
              <div class="action-buttons">
                  <div class="action-item" @click="navigateTo(streamer.uid, 'danmaku')">
                    <el-button circle size="large" class="icon-btn">
                      <el-icon><ChatDotRound /></el-icon>
                    </el-button>
                  </div>
                
                  <div class="action-item" @click="navigateTo(streamer.uid, 'songs')">
                    <el-button circle size="large" class="icon-btn">
                      <el-icon><Headset /></el-icon>
                    </el-button>
                  </div>
              </div>
            </div>
          </div>
        </div>
      </el-carousel-item>

      <!-- 最后一页：完整 VUP 列表 -->
      <el-carousel-item>
        <div class="vup-list-slide">
          <VupList />
        </div>
      </el-carousel-item>
    </el-carousel>

    <!-- 自定义右侧垂直导航栏 -->
    <div 
      class="right-nav-bar"
      :class="{ visible: isNavVisible }"
      @mouseenter="isNavVisible = true"
      @mouseleave="isNavVisible = false"
    >
      <div 
        v-for="(streamer, index) in featuredStreamers" 
        :key="streamer.uid"
        class="nav-item"
        :class="{ active: activeIndex === index }"
        @click="setActiveItem(index)"
      >
        <div class="nav-avatar-wrapper">
            <img :src="streamer.imageUrl" class="nav-avatar" />
          </div>
      </div>
      
      <!-- 更多列表按钮 -->
      <div 
        class="nav-item"
        :class="{ active: activeIndex === featuredStreamers.length }"
        @click="setActiveItem(featuredStreamers.length)"
      >
        <div class="nav-avatar-wrapper more-icon">
          <el-icon><Grid /></el-icon>
        </div>
      </div>

      <!-- 提示箭头 -->
      <div class="nav-hint" v-show="!isNavVisible">
        <el-icon><ArrowLeft /></el-icon>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue';
import { useRouter } from 'vue-router';
import { VUP_LIST } from '../constants/vups';
import { ChatDotRound, Headset, Grid, ArrowLeft } from '@element-plus/icons-vue';
import VupList from '../components/VupList.vue';

const router = useRouter();
const carouselRef = ref<any>(null);
const activeIndex = ref(0);
const isScrolling = ref(false);
const isNavVisible = ref(false);

// 只取前三个主播作为推荐展示
const featuredStreamers = computed(() => {
  return VUP_LIST.slice(0, 3);
});

const navigateTo = (uid: string, type: 'danmaku' | 'songs') => {
  if (type === 'danmaku') {
    router.push(`/vup/${uid}`);
  } else if (type === 'songs') {
    router.push(`/vup/${uid}/songs`);
  }
};

const openLivestream = (url: string) => {
  if (url) {
    window.open(url, '_blank');
  }
};

const setActiveItem = (index: number) => {
  if (carouselRef.value) {
    carouselRef.value.setActiveItem(index);
    activeIndex.value = index;
  }
};

const handleSlideChange = (index: number) => {
  activeIndex.value = index;
};

// 鼠标滚轮切换逻辑
const handleWheel = (e: WheelEvent) => {
  // 只有当导航栏可见时，才允许滚轮切换主页
  if (!isNavVisible.value) return;

  if (isScrolling.value) return;
  
  // 阈值判断，防止轻微滑动误触
  if (Math.abs(e.deltaY) < 30) return;

  isScrolling.value = true;
  
  if (e.deltaY > 0) {
    carouselRef.value?.next();
  } else {
    carouselRef.value?.prev();
  }

  // 防抖延迟，根据轮播动画时长调整（默认约300ms）
  setTimeout(() => {
    isScrolling.value = false;
  }, 500);
};

const handleImageError = (e: Event, fallbackUrl: string) => {
  const img = e.target as HTMLImageElement;
  
  // 防止死循环：如果已经重试过一次，或者 fallbackUrl 就是当前 src，就停止
  if (img.dataset.retried === 'true') {
    return;
  }
  
  // 标记已重试
  img.dataset.retried = 'true';
  
  if (fallbackUrl && img.src !== fallbackUrl) {
    img.src = fallbackUrl;
  } else {
    // 如果 fallback 也失败，或者没有 fallback，显示默认图或隐藏
    img.style.display = 'none';
  }
};
</script>

<style scoped>
.home-carousel-container {
  height: 100vh;
  width: 100vw;
  background-color: var(--bg-primary);
  overflow: hidden;
}

.full-screen-carousel {
  height: 100%;
}

:deep(.el-carousel__container) {
  height: 100% !important;
}

.carousel-slide {
  height: 100%;
  width: 100%;
  position: relative;
  overflow: hidden;
}

.slide-background {
  position: absolute;
  top: 0;
  left: 0;
  width: 100%;
  height: 100%;
  background-size: cover;
  background-position: center;
  filter: blur(40px) brightness(0.9); /* 强模糊 + 稍微调亮 */
  transform: scale(1.1); /* 放大一点避免边缘模糊露白 */
  z-index: 1;
}

.slide-content {
  position: relative;
  z-index: 2;
  height: 100%;
  width: 100%;
  display: flex;
  flex-direction: column;
  justify-content: center;
  align-items: center;
  gap: 40px;
}

.image-card {
  width: 240px;
  height: 240px;
  border-radius: 20px;
  overflow: hidden;
  box-shadow: 0 20px 50px rgba(0,0,0,0.5);
  border: 4px solid rgba(255, 255, 255, 0.1);
  transition: transform 0.3s ease;
}

.image-card:hover {
  transform: translateY(-10px);
}

.streamer-avatar {
  width: 100%;
  height: 100%;
  object-fit: cover;
  cursor: pointer;
}

.info-container {
  text-align: center;
}

.streamer-name {
  color: white;
  font-size: 2.5rem;
  margin-bottom: 30px;
  text-shadow: 0 4px 10px rgba(0,0,0,0.3);
  font-weight: 700;
  letter-spacing: 2px;
}

.action-buttons {
  display: flex;
  gap: 60px;
  justify-content: center;
}

.action-item {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12px;
  cursor: pointer;
  transition: transform 0.2s;
}

.action-item:hover {
  transform: scale(1.1);
}

.icon-btn {
  width: 64px;
  height: 64px;
  font-size: 28px;
  border: 1px solid rgba(255, 255, 255, 0.2);
  box-shadow: 0 8px 20px rgba(0,0,0,0.3);
  background-color: rgba(255, 255, 255, 0.15); /* 半透明背景 */
  backdrop-filter: blur(5px);
  color: white;
  transition: all 0.3s ease;
}

.icon-btn:hover {
  background-color: rgba(255, 255, 255, 0.3);
  transform: scale(1.1);
  border-color: rgba(255, 255, 255, 0.4);
}



.right-nav-bar {
  position: absolute;
  right: -60px; /* 默认隐藏在屏幕右侧 */
  top: 50%;
  transform: translateY(-50%);
  z-index: 10;
  display: flex;
  flex-direction: column;
  gap: 20px;
  background-color: rgba(0, 0, 0, 0.3);
  padding: 15px 10px;
  border-radius: 30px 0 0 30px; /* 左边圆角 */
  backdrop-filter: blur(10px);
  transition: right 0.3s ease, opacity 0.3s ease;
  opacity: 0.3; /* 默认半透明，提示这里有东西 */
}

/* 增加一个隐形的触发区域，防止鼠标太难对准 */
.right-nav-bar::before {
  content: '';
  position: absolute;
  left: -20px;
  top: 0;
  width: 100px; /* 扩大触发区域 */
  height: 100%;
  z-index: -1;
}

.right-nav-bar.visible,
.right-nav-bar:hover {
  right: 0; /* 显示 */
  opacity: 1;
  border-radius: 30px; /* 恢复全圆角 */
  padding-right: 20px; /* 增加右边距，看起来居中 */
}

.nav-item {
  width: 50px;
  height: 50px;
  border-radius: 50%;
  cursor: pointer;
  transition: all 0.3s ease;
  border: 2px solid transparent;
  display: flex;
  align-items: center;
  justify-content: center;
  position: relative;
}

.nav-item:hover {
  transform: scale(1.1);
  background-color: rgba(255, 255, 255, 0.1);
}

.nav-item.active {
  border-color: #409EFF;
  transform: scale(1.15);
  box-shadow: 0 0 15px rgba(64, 158, 255, 0.5);
}

.nav-avatar-wrapper {
  width: 100%;
  height: 100%;
  border-radius: 50%;
  overflow: hidden;
  display: flex;
  align-items: center;
  justify-content: center;
  background-color: rgba(0,0,0,0.2);
}

.nav-avatar {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.more-icon {
  color: white;
  font-size: 20px;
}

.nav-hint {
  position: absolute;
  left: -25px; /* 放在导航栏左侧 */
  top: 50%;
  transform: translateY(-50%);
  color: rgba(255, 255, 255, 0.6);
  font-size: 20px;
  animation: pulse 2s infinite;
  pointer-events: none; /* 防止遮挡触发区域 */
}

@keyframes pulse {
  0% { transform: translateY(-50%) translateX(0); opacity: 0.6; }
  50% { transform: translateY(-50%) translateX(-5px); opacity: 1; }
  100% { transform: translateY(-50%) translateX(0); opacity: 0.6; }
}

.vup-list-slide {
  height: 100%;
  width: 100%;
  overflow: hidden;
  background-color: var(--bg-primary);
}

/* 适配深色模式下的 VupList 背景 */
:deep(.home-page) {
  height: 100%;
  background: transparent !important; /* 让 VupList 透明，融入 Slide 背景 */
}
</style>
