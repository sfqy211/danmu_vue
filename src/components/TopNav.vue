<template>
  <header class="top-nav">
    <div class="nav-inner">
      <!-- Logo 区 -->
      <div class="nav-logo">
        <img src="/vite.svg" class="logo-avatar" />
        <span class="logo-text">VUP 弹幕站</span>
      </div>
      <!-- 导航按钮 -->
      <nav class="nav-buttons">
        <button class="nav-btn" :class="{ active: route.name === 'home' }" @click="router.push('/')">
          <el-icon><HomeFilled /></el-icon>
          <span>主页</span>
        </button>
        <button class="nav-btn" :class="{ active: route.name === 'vup-list' }" @click="router.push('/vup-list')">
          <el-icon><List /></el-icon>
          <span>列表</span>
        </button>
      </nav>
    </div>
  </header>
</template>
<script setup lang="ts">
import { useRouter, useRoute } from 'vue-router';
import { HomeFilled, List } from '@element-plus/icons-vue';
const router = useRouter();
const route = useRoute();
</script>
<style scoped>
/* ===== 顶部导航栏 ===== */
.top-nav {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  z-index: 100;
  /* 核心优化1：极低透明度的白色渐变，和亮彩背景完美融合，不抢视觉 */
  background: linear-gradient(
    180deg,
    rgba(255, 255, 255, 0.08) 0%,
    rgba(255, 255, 255, 0.03) 60%,
    rgba(255, 255, 255, 0) 100%
  );
  /* 核心优化2：和下方卡片完全一致的模糊值，质感100%对齐 */
  backdrop-filter: blur(8px);
  -webkit-backdrop-filter: blur(8px);
  /* 核心优化3：用mask做底部边缘羽化，彻底消除硬边界，无任何突兀线条 */
  mask-image: linear-gradient(to bottom, #000 85%, transparent 100%);
  -webkit-mask-image: linear-gradient(to bottom, #000 85%, transparent 100%);
  /* 去掉所有多余阴影，只保留极淡的内边框，强化嵌入感 */
  box-shadow: inset 0 -1px 0 rgba(255, 255, 255, 0.1);
}
.nav-inner {
  height: 56px;
  display: flex;
  align-items: center;
  padding: 0 20px;
  gap: 16px;
}
.nav-logo {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-shrink: 0;
  cursor: default;
}
.logo-avatar {
  width: 32px;
  height: 32px;
  border-radius: 50%;
  object-fit: cover;
}
.logo-text {
  color: white;
  font-weight: 700;
  font-size: 15px;
  white-space: nowrap;
  text-shadow: 0 1px 4px rgba(0, 0, 0, 0.2);
}
.nav-buttons {
  display: flex;
  gap: 8px;
  margin-left: auto;
  flex-shrink: 0;
}
.nav-btn {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 8px 16px;
  border-radius: 20px;
  border: 1px solid transparent;
  background: rgba(255, 255, 255, 0.08);
  color: rgba(255, 255, 255, 0.85);
  cursor: pointer;
  font-size: 14px;
  transition: all 0.2s ease;
  backdrop-filter: blur(10px);
  -webkit-backdrop-filter: blur(10px);
  white-space: nowrap;
}
.nav-btn:hover {
  background: rgba(255, 255, 255, 0.15);
  color: white;
  transform: translateY(-1px);
}
.nav-btn.active {
  background: rgba(255, 255, 255, 0.18);
  color: white;
  border-color: rgba(255, 255, 255, 0.2);
}
.nav-btn .el-icon {
  font-size: 16px;
}
</style>