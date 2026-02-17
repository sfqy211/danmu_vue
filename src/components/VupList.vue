<template>
  <div class="vup-list-container">
    <div class="glass-container">
      <!-- 顶部标签栏 -->
      <div class="tabs-header">
        <button
          v-for="group in Object.values(GROUPS)"
          :key="group"
          @click="selectedGroup = group"
          class="tab-btn"
          :class="{ active: selectedGroup === group }"
        >
          {{ group }}
        </button>
      </div>

      <!-- 内容网格 -->
      <div class="content-grid">
        <div v-for="artist in filteredArtists" :key="artist.id" class="vup-card">
          <div class="vup-avatar-wrapper">
             <img :src="artist.avatarUrl" :alt="artist.name" class="vup-avatar" @error="handleImageError" />
          </div>
          <div class="vup-info">
            <h3 class="vup-name">{{ artist.name }}</h3>
            <div class="vup-links">
              <a :href="artist.homepageUrl" target="_blank" class="link-btn primary">主页</a>
              <a :href="artist.livestreamUrl" target="_blank" class="link-btn pink">直播间</a>
              <a v-if="artist.playlistUrl" :href="artist.playlistUrl" target="_blank" class="link-btn green">歌单</a>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue';
import { GROUPS, VUP_LIST } from '../constants/vups';

const selectedGroup = ref(GROUPS.OVO_FAMILY);

const filteredArtists = computed(() => {
  return VUP_LIST.filter(artist => artist.groups.includes(selectedGroup.value));
});

const handleImageError = (e: Event) => {
  const target = e.target as HTMLImageElement;
  // 如果图片加载失败，显示默认占位图或颜色
  target.style.display = 'none';
  target.parentElement!.style.backgroundColor = '#409EFF';
  target.parentElement!.textContent = target.alt[0];
  target.parentElement!.style.display = 'flex';
  target.parentElement!.style.alignItems = 'center';
  target.parentElement!.style.justifyContent = 'center';
  target.parentElement!.style.color = 'white';
  target.parentElement!.style.fontSize = '24px';
  target.parentElement!.style.fontWeight = 'bold';
};
</script>

<style scoped>
.vup-list-container {
  height: 100%;
  width: 100%;
  display: flex;
  align-items: center;
  justify-content: center;
  background: linear-gradient(135deg, #1a1a1a 0%, #2d3436 100%); /* 深色渐变背景 */
  padding: 40px;
  box-sizing: border-box;
}

.glass-container {
  width: 100%;
  max-width: 1200px;
  height: 90%;
  background: rgba(255, 255, 255, 0.05);
  backdrop-filter: blur(20px);
  border-radius: 24px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  padding: 30px;
  display: flex;
  flex-direction: column;
  gap: 30px;
}

.tabs-header {
  display: flex;
  justify-content: center;
  gap: 15px;
}

.tab-btn {
  background: transparent;
  border: 1px solid rgba(255, 255, 255, 0.2);
  color: rgba(255, 255, 255, 0.7);
  padding: 10px 24px;
  border-radius: 30px;
  cursor: pointer;
  transition: all 0.3s;
  font-size: 16px;
}

.tab-btn.active {
  background: rgba(255, 255, 255, 0.2);
  color: white;
  border-color: white;
  box-shadow: 0 0 15px rgba(255, 255, 255, 0.2);
}

.content-grid {
  flex: 1;
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
  grid-auto-rows: max-content;
  gap: 20px;
  overflow-y: auto;
  padding-right: 10px;
}

/* 隐藏滚动条但保留功能 */
.content-grid::-webkit-scrollbar {
  width: 6px;
}
.content-grid::-webkit-scrollbar-thumb {
  background: rgba(255, 255, 255, 0.2);
  border-radius: 3px;
}

.vup-card {
  background: rgba(0, 0, 0, 0.3);
  border-radius: 16px;
  padding: 20px;
  display: flex;
  align-items: center;
  gap: 15px;
  transition: transform 0.2s, background 0.2s;
}

.vup-card:hover {
  transform: translateY(-5px);
  background: rgba(0, 0, 0, 0.5);
}

.vup-avatar-wrapper {
  width: 60px;
  height: 60px;
  border-radius: 50%;
  overflow: hidden;
  flex-shrink: 0;
  border: 2px solid rgba(255, 255, 255, 0.2);
}

.vup-avatar {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.vup-info {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.vup-name {
  margin: 0;
  color: white;
  font-size: 18px;
}

.vup-links {
  display: flex;
  gap: 8px;
}

.link-btn {
  font-size: 12px;
  padding: 4px 10px;
  border-radius: 6px;
  text-decoration: none;
  color: white;
  background: rgba(255, 255, 255, 0.1);
  transition: all 0.2s;
}

.link-btn:hover {
  filter: brightness(1.2);
}

.link-btn.primary { background: #409EFF; }
.link-btn.pink { background: #F56C6C; }
.link-btn.green { background: #67C23A; }

</style>
