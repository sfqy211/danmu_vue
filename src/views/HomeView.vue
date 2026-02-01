<template>
  <div class="home-page">
    <div class="app-wrapper">
      <!-- Header -->
      <div class="header-section">
        <div class="header-content">
          <h1 class="page-title">VUP链接汇总</h1>
          <p class="page-subtitle">快速访问你喜爱的VUP主页、直播间和歌单</p>
        </div>
        <div class="header-action">
          <router-link to="/danmu" class="apple-button apple-primary link-btn">
            <span class="icon">💬</span> 弹幕预览工具
          </router-link>
        </div>
      </div>

      <div class="content-layout">
        <!-- Sidebar / Group Selector -->
        <div class="sidebar-section">
          <div class="group-selector">
            <h2 class="section-title">VUP分组</h2>
            <div class="group-list">
              <button
                v-for="group in Object.values(GROUPS)"
                :key="group"
                @click="selectedGroup = group"
                class="group-btn apple-button"
                :class="selectedGroup === group ? 'apple-primary active' : 'apple-secondary'"
              >
                {{ group }}
              </button>
            </div>
          </div>
        </div>

        <!-- Main Content / Artist List -->
        <div class="main-section">
          <div class="artist-grid">
            <div v-for="artist in filteredArtists" :key="artist.id" class="apple-card artist-card">
              <div class="card-content">
                <h3 class="artist-name">{{ artist.name }}</h3>
                
                <div class="artist-actions">
                  <a :href="artist.homepageUrl" target="_blank" rel="noopener noreferrer" class="action-btn apple-button apple-primary">
                    主页
                  </a>
                  
                  <a :href="artist.livestreamUrl" target="_blank" rel="noopener noreferrer" class="action-btn apple-button btn-pink">
                    直播间
                  </a>
                  
                  <a v-if="artist.playlistUrl" :href="artist.playlistUrl" target="_blank" rel="noopener noreferrer" class="action-btn apple-button btn-green">
                    歌单
                  </a>
                  <span v-else class="action-btn apple-button btn-disabled">
                    暂时还没有哦
                  </span>
                </div>
              </div>
            </div>
          </div>
          
          <div class="footer-info">
            <p>当前分组: {{ selectedGroup }} | 显示 {{ filteredArtists.length }} 位VUP</p>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue';

const GROUPS = {
  OVO_FAMILY: 'OVO家族',
  TYBK_SISTERS: '桃圆布蔻',
  FEIENDS: '桃几的好朋友们'
};

const mockArtists = [
    {
        id: '1',
        name: '桃几OvO',
        homepageUrl: 'https://space.bilibili.com/1104048496',
        livestreamUrl: 'https://live.bilibili.com/22642754',
        playlistUrl: 'https://www.ovo.fan',
        groups: [GROUPS.OVO_FAMILY, GROUPS.TYBK_SISTERS]
    },
    {
        id: '2',
        name: '鱼鸽鸽',
        homepageUrl: 'https://space.bilibili.com/4718716',
        livestreamUrl: 'https://live.bilibili.com/673',
        playlistUrl: 'https://bot.starlwr.com/songlist?uid=4718716',
        groups: [GROUPS.OVO_FAMILY]
    },
    {
        id: '3',
        name: '妮莉安Lily',
        homepageUrl: 'https://space.bilibili.com/3493271057730096',
        livestreamUrl: 'https://live.bilibili.com/27484357',
        playlistUrl: 'https://www.nilianlily.cn',
        groups: [GROUPS.OVO_FAMILY]
    },
    {
        id: '4',
        name: '大哥L-',
        homepageUrl: 'https://space.bilibili.com/17967817',
        livestreamUrl: 'https://live.bilibili.com/443197',
        playlistUrl: 'https://dagel.live',
        groups: [GROUPS.OVO_FAMILY]
    },
    {
        id: '5',
        name: '帅比笙歌超可爱OvO',
        homepageUrl: 'https://space.bilibili.com/15641218',
        livestreamUrl: 'https://live.bilibili.com/545',
        playlistUrl: 'https://tools.vupgo.com/LiveMusic?buid=15641218',
        groups: [GROUPS.OVO_FAMILY]
    },
    {
        id: '6',
        name: '葡冷尔子gagako',
        homepageUrl: 'https://space.bilibili.com/1376650682',
        livestreamUrl: 'https://live.bilibili.com/22857429',
        playlistUrl: 'http://gagako.minamini.cn',
        groups: [GROUPS.OVO_FAMILY]
    },
    {
        id: '7',
        name: '里奈Rina',
        homepageUrl: 'https://space.bilibili.com/7591465',
        livestreamUrl: 'https://live.bilibili.com/873642',
        playlistUrl: 'http://rinana.vsinger.ink',
        groups: [GROUPS.OVO_FAMILY]
    },
    {
        id: '8',
        name: '浅野天琪_TANCHJIM',
        homepageUrl: 'https://space.bilibili.com/390647282',
        livestreamUrl: 'https://live.bilibili.com/21465419',
        playlistUrl: 'http://yybb.vsinger.ink',
        groups: [GROUPS.TYBK_SISTERS]
    },
    {
        id: '9',
        name: 'Niya阿布',
        homepageUrl: 'https://space.bilibili.com/188679',
        livestreamUrl: 'https://live.bilibili.com/685026',
        playlistUrl: 'https://2some.ren/niyabu/songs',
        groups: [GROUPS.TYBK_SISTERS]
    },
    {
        id: '10',
        name: '-蔻蔻CC-',
        homepageUrl: 'https://space.bilibili.com/128667389',
        livestreamUrl: 'https://live.bilibili.com/23587427',
        playlistUrl: 'http://kkcc.vsinger.ink',
        groups: [GROUPS.TYBK_SISTERS]
    },
    {
        id: '11',
        name: '莱妮娅_Rynia',
        homepageUrl: 'https://space.bilibili.com/703018634',
        livestreamUrl: 'https://live.bilibili.com/54363',
        playlistUrl: 'http://songlist.rynia.live',
        groups: [GROUPS.FEIENDS]
    },
    {
        id: '12',
        name: '内德维德',
        homepageUrl: 'https://space.bilibili.com/90873',
        livestreamUrl: 'https://live.bilibili.com/5424',
        playlistUrl: '',
        groups: [GROUPS.FEIENDS]
    },
    {
        id: '13',
        name: '薇Steria',
        homepageUrl: 'https://space.bilibili.com/1112031857',
        livestreamUrl: 'https://live.bilibili.com/22924075',
        playlistUrl: 'http://weisteria.vsinger.ink',
        groups: [GROUPS.FEIENDS]
    },
    {
        id: '14',
        name: 'CODE-V',
        homepageUrl: 'https://space.bilibili.com/121309',
        livestreamUrl: 'https://live.bilibili.com/858080',
        playlistUrl: 'https://codev.starlwr.com',
        groups: [GROUPS.FEIENDS]
    },
    {
        id: '15',
        name: '-菫時-',
        homepageUrl: 'https://space.bilibili.com/796556',
        livestreamUrl: 'https://live.bilibili.com/3473884',
        playlistUrl: 'https://sumireji.com/',
        groups: [GROUPS.FEIENDS]
    },
];

const selectedGroup = ref(GROUPS.OVO_FAMILY);

const filteredArtists = computed(() => {
  return mockArtists.filter(artist => artist.groups.includes(selectedGroup.value));
});
</script>

<style scoped>
/* 局部变量定义，支持深色模式 */
.home-page {
  --home-bg: linear-gradient(to bottom right, #F9FAFC, #FFFFFF);
  --home-card-bg: #FFFFFF;
  --home-text-primary: #1C1C1E;
  --home-text-secondary: #424245;
  --home-border: #E8E8ED;
  
  height: 100vh;
  overflow-y: auto;
  -webkit-overflow-scrolling: touch;
  background: var(--home-bg);
  color: var(--home-text-secondary);
  font-family: "Inter", "思源黑体", Arial, Helvetica, sans-serif;
  padding: 1rem;
  transition: background 0.3s ease, color 0.3s ease;
}

@media (min-width: 768px) {
  .home-page {
    padding: 2rem 1rem;
  }
}

:global(.dark-mode) .home-page {
  --home-bg: linear-gradient(to bottom right, #1c1c1e, #000000);
  --home-card-bg: #2c2c2e;
  --home-text-primary: #FFFFFF;
  --home-text-secondary: #AEAeb2;
  --home-border: #38383a;
}

.app-wrapper {
  max-width: 80rem; /* max-w-7xl */
  margin: 0 auto;
}

/* 头部样式 */
.header-section {
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 1.5rem;
  gap: 1rem;
}

@media (min-width: 768px) {
  .header-section {
    flex-direction: row;
    margin-bottom: 3rem;
    gap: 1.5rem;
  }
}

.header-content {
  text-align: center;
}

@media (min-width: 768px) {
  .header-section {
    flex-direction: row;
  }
  .header-content {
    text-align: left;
    order: 1;
  }
  .header-action {
    order: 2;
  }
}

.page-title {
  font-size: 1.875rem; /* 3xl */
  font-weight: 500;
  color: var(--home-text-primary);
  margin-bottom: 0.5rem;
}

.page-subtitle {
  font-size: 1rem;
  color: var(--home-text-secondary);
}

.link-btn {
  display: inline-flex;
  align-items: center;
  padding: 0.625rem 1.5rem;
  font-weight: 500;
  text-decoration: none;
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.05);
  border-radius: 0.75rem; /* 统一圆角 */
}

.icon {
  margin-right: 0.5rem;
}

/* 布局 */
.content-layout {
  display: flex;
  flex-direction: column;
  gap: 2rem;
}

@media (min-width: 768px) {
  .content-layout {
    flex-direction: row;
  }
  .sidebar-section {
    width: 25%;
  }
  .main-section {
    width: 75%;
  }
}

/* 侧边栏分组 */
.section-title {
  font-size: 1.25rem; /* xl */
  font-weight: 500;
  color: var(--home-text-primary);
  margin-bottom: 1rem;
}

.group-list {
  display: flex;
  flex-direction: row;
  overflow-x: auto;
  gap: 0.5rem;
  padding-bottom: 0.5rem;
  -webkit-overflow-scrolling: touch;
  scrollbar-width: none; /* Firefox */
}

.group-list::-webkit-scrollbar {
  display: none; /* Chrome/Safari */
}

.group-btn {
  flex-shrink: 0;
  width: auto;
  padding: 0.6rem 1.2rem;
  text-align: center;
  border-radius: 2rem;
  font-size: 0.9rem;
  background: none;
  border: none;
}

@media (min-width: 768px) {
  .group-list {
    flex-direction: column;
    overflow-x: visible;
  }
  .group-btn {
    width: 100%;
    padding: 0.875rem 1rem;
    text-align: left;
    border-radius: 0.75rem;
    font-size: 1rem;
  }
}

/* 列表网格 */
.artist-grid {
  display: grid;
  grid-template-columns: 1fr;
  gap: 1.5rem;
}

.apple-card {
  background-color: var(--home-card-bg);
  border: 1px solid var(--home-border);
  box-shadow: 0 2px 8px rgba(0,0,0,0.04);
  border-radius: 0.75rem;
  padding: 1.5rem;
  transition: all 0.2s;
}

.apple-card:hover {
  box-shadow: 0 4px 12px rgba(0,0,0,0.08);
}

.card-content {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  align-items: flex-start;
}

@media (min-width: 640px) {
  .card-content {
    flex-direction: row;
    align-items: center;
  }
}

.artist-name {
  font-size: 1.125rem;
  font-weight: 500;
  color: var(--home-text-primary);
  width: 100%;
}

@media (min-width: 640px) {
  .artist-name {
    width: 20%;
  }
}

.artist-actions {
  display: flex;
  gap: 0.75rem;
  width: 100%;
}

@media (min-width: 640px) {
  .artist-actions {
    width: 80%;
  }
}

.action-btn {
  flex: 1;
  text-align: center;
  padding: 0.625rem 1rem;
  font-weight: 500;
  text-decoration: none;
  border-radius: 0.5rem;
  font-size: 0.95rem;
  white-space: nowrap;
  box-shadow: 0 1px 2px rgba(0,0,0,0.05);
}

/* 通用按钮样式 */
.apple-button {
  transition: all 150ms ease-in-out;
  cursor: pointer;
  display: inline-block;
}

.apple-button:hover {
  transform: scale(0.99);
}

.apple-button:active {
  transform: scale(0.98);
}

.apple-primary {
  background-color: #E6F0FF;
  color: #0071E3;
  border: none;
}

:global(.dark-mode) .apple-primary {
  background-color: #1a3a5a;
  color: #2997ff;
}

.apple-primary:hover {
  background-color: #D9E8FF;
}

:global(.dark-mode) .apple-primary:hover {
  background-color: #244b7a;
}

.apple-primary.active {
  box-shadow: 0 1px 3px rgba(0,0,0,0.1);
}

.apple-secondary {
  background-color: var(--home-card-bg);
  color: var(--home-text-secondary);
  border: 1px solid var(--home-border);
}

.apple-secondary:hover {
  border-color: #D2D2D7;
}

.btn-pink {
  background-color: #FFF5F7;
  color: #C71585;
}

:global(.dark-mode) .btn-pink {
  background-color: #3d1a2b;
  color: #ff69b4;
}

.btn-pink:hover {
  background-color: #FFE6F0;
}

.btn-green {
  background-color: #F0F9F2;
  color: #2E8B57;
}

:global(.dark-mode) .btn-green {
  background-color: #1a3321;
  color: #4ade80;
}

.btn-green:hover {
  background-color: #E0F2E9;
}

.btn-disabled {
  background-color: #F0F9F2;
  color: #86868B;
  cursor: not-allowed;
}

.btn-disabled:hover {
  transform: none;
}

.footer-info {
  text-align: center;
  margin-top: 3rem;
}

.footer-info p {
  color: var(--text-tertiary);
  font-size: 0.875rem;
}
</style>
