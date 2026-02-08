<template>
  <div class="danmaku-container" v-loading="loading" 
    @mousemove="onMouseMove" @mouseup="stopResize" @mouseleave="stopResize"
    @touchmove="onTouchMove" @touchend="stopResize" @touchcancel="stopResize">
    <div v-if="!currentSession && !loading" class="empty-state">
      <el-empty description="暂无弹幕数据，请选择左侧直播回放" />
    </div>

    <div v-else-if="!isDanmakuLoaded && !loading" class="load-placeholder">
      <div class="load-card">
        <div class="load-icon">💬</div>
        <h3>已加载统计数据</h3>
        <p>该场次共有 {{ totalDanmaku }} 条弹幕</p>
        <el-button 
          type="primary" 
          size="large" 
          :loading="danmakuLoading"
          @click="store.fetchDanmaku"
        >
          查询弹幕列表
        </el-button>
      </div>
    </div>
    
    <div v-else ref="splitContainer" class="split-container" :class="{ 'is-mobile': isMobile }">
      <!-- 上部分/左部分：普通弹幕 -->
      <div class="column-side" :style="columnSideStyle(true)">
        <div class="column-header-sticky">
          <span>普通弹幕</span>
          <span class="badge">{{ filteredNormalList.length }}</span>
        </div>
        <div class="scrollable-list" ref="leftScroller" @scroll="onLeftScroll">
          <div class="danmaku-spacer" :style="{ height: leftTotalHeight + 'px' }"></div>
          <div class="danmaku-content" :style="{ transform: `translateY(${leftOffsetY}px)` }">
            <div 
              v-for="item in visibleLeftItems" 
              :key="getItemKey(item)"
              class="danmaku-item"
              :class="{ 'is-expanded': expandedItemKey === getItemKey(item) }"
            >
              <el-popover
                placement="right"
                :width="200"
                trigger="click"
                popper-class="user-menu-popover"
              >
                <template #reference>
                  <span class="danmaku-username" :title="`UID: ${item.uid}`">{{ item.user }}</span>
                </template>
                <div class="user-menu-content">
                  <div class="menu-item" @click="filterByUser(item.user)">
                    <span class="menu-icon">🔍</span>
                    <span>筛选此用户弹幕</span>
                  </div>
                  <div class="menu-item" :class="{ disabled: !item.uid }" @click="item.uid && openProfile(item.uid)">
                    <span class="menu-icon">👤</span>
                    <span>打开用户主页</span>
                  </div>
                  <div class="menu-item" :class="{ disabled: !item.uid }" @click="item.uid && openLaplace(item.uid)">
                    <span class="menu-icon">🧪</span>
                    <span>查成分 (Laplace)</span>
                  </div>
                </div>
              </el-popover>
              <span class="danmaku-text" :title="item.content" @click="toggleExpand(item)">{{ item.content }}</span>
              <span class="danmaku-time">{{ item.timeStr }}</span>
            </div>
          </div>
        </div>
        <div v-if="filteredNormalList.length === 0" class="preview-empty">
          <p>暂无普通弹幕</p>
        </div>
      </div>

      <!-- 拖动条 -->
      <div class="resizer" @mousedown="startResize" @touchstart="startResize"></div>

      <!-- 下部分/右部分：SC 付费弹幕 -->
      <div class="column-side" :style="columnSideStyle(false)">
        <div class="column-header-sticky">
          <span>Super Chat</span>
          <span class="badge">{{ filteredSCList.length }}</span>
        </div>
        <div class="scrollable-list" ref="rightScroller" @scroll="onRightScroll">
          <div class="danmaku-spacer" :style="{ height: rightTotalHeight + 'px' }"></div>
          <div class="danmaku-content" :style="{ transform: `translateY(${rightOffsetY}px)` }">
            <div 
              v-for="item in visibleRightItems" 
              :key="getItemKey(item)"
              class="danmaku-item sc-item"
              :class="{ 'is-expanded': expandedItemKey === getItemKey(item) }"
              :style="{ 
                borderLeftColor: getSCStyle(item.price || 0).main,
                backgroundColor: getSCStyle(item.price || 0).bg 
              }"
            >
              <div class="danmaku-content-wrapper">
                <div class="sc-header">
                  <span class="sc-price" :style="{ color: getSCStyle(item.price || 0).main }">
                    CNY {{ formatPrice(item.price || 0) }}
                  </span>
                  <el-popover
                    placement="right"
                    :width="200"
                    trigger="click"
                    popper-class="user-menu-popover"
                  >
                    <template #reference>
                      <span class="danmaku-username" :style="{ color: getSCStyle(item.price || 0).main }" :title="`UID: ${item.uid}`">
                        {{ item.user }}
                      </span>
                    </template>
                    <div class="user-menu-content">
                      <div class="menu-item" @click="filterByUser(item.user)">
                        <span class="menu-icon">🔍</span>
                        <span>筛选此用户弹幕</span>
                      </div>
                      <div class="menu-item" :class="{ disabled: !item.uid }" @click="item.uid && openProfile(item.uid)">
                        <span class="menu-icon">👤</span>
                        <span>打开用户主页</span>
                      </div>
                      <div class="menu-item" :class="{ disabled: !item.uid }" @click="item.uid && openLaplace(item.uid)">
                        <span class="menu-icon">🧪</span>
                        <span>查成分 (Laplace)</span>
                      </div>
                    </div>
                  </el-popover>
                </div>
                <div class="danmaku-text" :title="item.content" @click="toggleExpand(item)">{{ item.content }}</div>
                <div class="danmaku-time">{{ item.timeStr }}</div>
              </div>
            </div>
          </div>
        </div>
        <div v-if="filteredSCList.length === 0" class="preview-empty">
          <p>暂无付费弹幕</p>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue';
import { useDanmakuStore } from '../stores/danmakuStore';
import { storeToRefs } from 'pinia';

const store = useDanmakuStore();
const { 
  danmakuList, 
  scList, 
  loading, 
  danmakuLoading, 
  isDanmakuLoaded, 
  currentSession,
  totalDanmaku,
  searchText 
} = storeToRefs(store);

// Expanded item state
const expandedItemKey = ref<string | null>(null);

const getItemKey = (item: any) => `${item.timestamp}-${item.user}-${item.content}`;

const toggleExpand = (item: any) => {
  const key = getItemKey(item);
  if (expandedItemKey.value === key) {
    expandedItemKey.value = null;
  } else {
    expandedItemKey.value = key;
  }
};

const filterByUser = (user: string) => {
  store.searchText = user;
};

const openProfile = (uid: string | number) => {
  window.open(`https://space.bilibili.com/${uid}`, '_blank');
};

const openLaplace = (uid: string | number) => {
  window.open(`https://laplace.live/user/${uid}`, '_blank');
};

// SC Styles logic from original project
const getSCLevel = (price: number) => {
  const yuan = price;
  if (yuan >= 2000) return 6;
  if (yuan >= 1000) return 5;
  if (yuan >= 500) return 4;
  if (yuan >= 100) return 3;
  if (yuan >= 50) return 2;
  if (yuan >= 30) return 1;
  return 1;
};

const getSCStyle = (price: number) => {
  const level = getSCLevel(price);
  const styles: Record<number, { main: string, bg: string }> = {
    1: { main: '#2A60B2', bg: 'rgba(42, 96, 178, 0.1)' },
    2: { main: '#427D9E', bg: 'rgba(66, 125, 158, 0.1)' },
    3: { main: '#E2B13C', bg: 'rgba(226, 177, 60, 0.1)' },
    4: { main: '#E09443', bg: 'rgba(224, 148, 67, 0.1)' },
    5: { main: '#E54D4D', bg: 'rgba(229, 77, 77, 0.1)' },
    6: { main: '#AB1A32', bg: 'rgba(171, 26, 50, 0.1)' }
  };
  return styles[level] || styles[1];
};

const formatPrice = (price: number) => {
  // If price is 0, it might be that the price was not parsed correctly or is actually 0
  return price.toString();
};

// Resizer Logic
const splitContainer = ref<HTMLElement | null>(null);
const splitRatio = ref(70); // Percentage
const isResizing = ref(false);
const isMobile = ref(window.innerWidth <= 768);

const updateMobileStatus = () => {
  const wasMobile = isMobile.value;
  isMobile.value = window.innerWidth <= 768;
  
  // If switching between mobile and web, we might want to reset or adjust things
  if (wasMobile !== isMobile.value) {
    // Optional: could reset splitRatio to 70 if desired
    // splitRatio.value = 70;
  }
};

const columnSideStyle = (isFirst: boolean) => {
  const ratio = isFirst ? splitRatio.value : (100 - splitRatio.value);
  if (isMobile.value) {
    // 手机端：上下布局，设置高度
    return { height: ratio + '%', width: '100%' };
  } else {
    // 网页端：左右布局，设置宽度
    return { width: ratio + '%', height: '100%' };
  }
};

const startResize = () => {
  isResizing.value = true;
  document.body.style.cursor = isMobile.value ? 'row-resize' : 'col-resize';
  document.body.style.userSelect = 'none';
};

const stopResize = () => {
  if (isResizing.value) {
    isResizing.value = false;
    document.body.style.cursor = '';
    document.body.style.userSelect = '';
  }
};

const onMouseMove = (e: MouseEvent) => {
  handleResizeMove(e.clientX, e.clientY);
};

const onTouchMove = (e: TouchEvent) => {
  if (isResizing.value && e.touches.length > 0) {
    // Only prevent default if we are actually resizing to allow normal scrolling otherwise
    if (e.cancelable) e.preventDefault();
    handleResizeMove(e.touches[0].clientX, e.touches[0].clientY);
  }
};

const handleResizeMove = (clientX: number, clientY: number) => {
  if (!isResizing.value || !splitContainer.value) return;
  
  const containerRect = splitContainer.value.getBoundingClientRect();
  let newRatio: number;
  
  if (isMobile.value) {
    newRatio = ((clientY - containerRect.top) / containerRect.height) * 100;
  } else {
    newRatio = ((clientX - containerRect.left) / containerRect.width) * 100;
  }
  
  // Clamp between 0% and 100% to allow columns to be hidden
  newRatio = Math.max(0, Math.min(100, newRatio));
  splitRatio.value = newRatio;
  
  // Update virtual scroll container heights after resize
  updateLeftHeight();
  updateRightHeight();
};

// Data Filtering
const filteredNormalList = computed(() => {
  let list = danmakuList.value.filter(d => !d.isSC);
  if (searchText.value) {
    const lower = searchText.value.toLowerCase();
    list = list.filter(d => 
      d.content.toLowerCase().includes(lower) || 
      d.user.toLowerCase().includes(lower)
    );
  }
  return list;
});

const filteredSCList = computed(() => {
  let list = scList.value; // Store already has scList
  if (searchText.value) {
    const lower = searchText.value.toLowerCase();
    list = list.filter(d => 
      d.content.toLowerCase().includes(lower) || 
      d.user.toLowerCase().includes(lower)
    );
  }
  return list;
});

// Virtual Scroll Logic - Reusable Function
const useVirtualScroll = (listRef: any, scrollerRef: any) => {
  const scrollTop = ref(0);
  const containerHeight = ref(600);
  const itemHeight = 44; // 40px height + 4px margin-bottom
  const buffer = 10;

  const totalHeight = computed(() => listRef.value.length * itemHeight);

  const visibleRange = computed(() => {
    const start = Math.floor(scrollTop.value / itemHeight);
    const visibleCount = Math.ceil(containerHeight.value / itemHeight);
    return {
      start: Math.max(0, start - buffer),
      end: Math.min(listRef.value.length, start + visibleCount + buffer)
    };
  });

  const offsetY = computed(() => visibleRange.value.start * itemHeight);

  const visibleItems = computed(() => {
    return listRef.value.slice(visibleRange.value.start, visibleRange.value.end);
  });

  const onScroll = (e: Event) => {
    const target = e.target as HTMLElement;
    scrollTop.value = target.scrollTop;
    
    // Load more trigger (only if scrolling near bottom)
    if (target.scrollTop + target.clientHeight >= target.scrollHeight - 50) {
      store.loadMore();
    }
  };

  const updateContainerHeight = () => {
    if (scrollerRef.value) {
      containerHeight.value = scrollerRef.value.clientHeight;
    }
  };

  return {
    scrollTop,
    totalHeight,
    offsetY,
    visibleItems,
    onScroll,
    updateContainerHeight
  };
};

// Left Column Virtual Scroll
const leftScroller = ref<HTMLElement | null>(null);
const { 
  totalHeight: leftTotalHeight, 
  offsetY: leftOffsetY, 
  visibleItems: visibleLeftItems, 
  onScroll: onLeftScroll,
  updateContainerHeight: updateLeftHeight
} = useVirtualScroll(filteredNormalList, leftScroller);

// Right Column Virtual Scroll
const rightScroller = ref<HTMLElement | null>(null);
const { 
  totalHeight: rightTotalHeight, 
  offsetY: rightOffsetY, 
  visibleItems: visibleRightItems, 
  onScroll: onRightScroll,
  updateContainerHeight: updateRightHeight
} = useVirtualScroll(filteredSCList, rightScroller);

// Lifecycle
onMounted(() => {
  updateLeftHeight();
  updateRightHeight();
  window.addEventListener('resize', () => {
    updateMobileStatus();
    updateLeftHeight();
    updateRightHeight();
  });
  window.addEventListener('mouseup', stopResize);
  window.addEventListener('touchend', stopResize);
  window.addEventListener('touchcancel', stopResize);
});

onUnmounted(() => {
  window.removeEventListener('mouseup', stopResize);
  window.removeEventListener('touchend', stopResize);
  window.removeEventListener('touchcancel', stopResize);
});
</script>

<style scoped>
.danmaku-container {
  height: 100%;
  display: flex;
  flex-direction: column;
  background-color: var(--bg-primary);
  overflow: hidden;
}

.empty-state, .load-placeholder {
  height: 100%;
  display: flex;
  justify-content: center;
  align-items: center;
  background-color: var(--bg-primary);
}

.load-card {
  text-align: center;
  padding: 40px;
  background: var(--bg-secondary);
  border-radius: 12px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
  max-width: 400px;
  width: 90%;
}

.load-icon {
  font-size: 48px;
  margin-bottom: 16px;
}

.load-card h3 {
  margin: 0 0 8px;
  color: var(--text-primary);
  font-size: 1.2rem;
}

.load-card p {
  margin: 0 0 24px;
  color: var(--text-secondary);
  font-size: 0.9rem;
}

/* Split Container */
.split-container {
  flex: 1;
  display: flex;
  flex-direction: row; /* Default: Horizontal (Web) */
  overflow: hidden;
  height: 100%;
  position: relative;
}

.split-container.is-mobile {
  flex-direction: column; /* Mobile: Vertical (Stacked) */
}

.column-side {
  display: flex;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
  background-color: var(--bg-primary);
  position: relative;
  overflow: hidden;
}

.split-container:not(.is-mobile) .column-side:first-child {
  border-right: none;
}

.column-header-sticky {
  padding: 12px 16px;
  font-weight: 600;
  color: var(--text-secondary);
  border-bottom: 1px solid var(--border);
  display: flex;
  justify-content: space-between;
  align-items: center;
  background-color: var(--bg-primary);
  z-index: 10;
  flex-shrink: 0;
}

.split-container.is-mobile .column-header-sticky {
  padding: 8px 16px;
}

.badge {
  background-color: var(--bg-hover);
  padding: 2px 8px;
  border-radius: 12px;
  font-size: 0.8rem;
  color: var(--text-secondary);
}

.scrollable-list {
  flex: 1;
  overflow-y: auto;
  padding: 8px 12px;
  position: relative;
}

.preview-empty {
  display: flex;
  justify-content: center;
  align-items: center;
  padding: 20px;
  color: var(--text-tertiary);
  font-size: 0.9rem;
}

/* Resizer */
.resizer {
  background-color: transparent;
  position: relative;
  z-index: 20;
  display: flex;
  flex-shrink: 0;
  transition: background-color 0.2s;
}

/* Web Resizer (Vertical bar) */
.split-container:not(.is-mobile) .resizer {
  width: 8px;
  height: 100%;
  cursor: col-resize;
  flex-direction: row;
  justify-content: center;
  background-color: transparent;
}

.split-container:not(.is-mobile) .resizer:hover,
.split-container:not(.is-mobile) .resizer:active {
  background-color: rgba(0, 113, 227, 0.05);
}

.split-container:not(.is-mobile) .resizer::after {
  content: '';
  width: 1px;
  height: 100%;
  background-color: var(--border);
}

.split-container:not(.is-mobile) .resizer:hover::after,
.split-container:not(.is-mobile) .resizer:active::after {
  width: 4px;
  background-color: var(--el-color-primary);
  border-radius: 2px;
}

/* Mobile Resizer (Horizontal bar) */
.split-container.is-mobile .resizer {
  height: 24px; /* 增加触摸区域 */
  width: 100%;
  cursor: row-resize;
  flex-direction: column;
  justify-content: center;
  background-color: transparent;
  margin: -8px 0; /* 负边距防止撑开过多空间 */
  position: relative;
  z-index: 30;
}

.split-container.is-mobile .resizer:hover,
.split-container.is-mobile .resizer:active {
  background-color: rgba(0, 113, 227, 0.05);
}

.split-container.is-mobile .resizer::after {
  content: '';
  height: 1px;
  width: 100%;
  background-color: var(--border);
}

.split-container.is-mobile .resizer:hover::after,
.split-container.is-mobile .resizer:active::after {
  height: 4px;
  background-color: var(--el-color-primary);
  border-radius: 2px;
}

/* Danmaku Items */
.danmaku-spacer {
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  z-index: -1;
}

.danmaku-content {
  width: 100%;
}

.danmaku-item {
  padding: 0 10px;
  min-height: 40px;
  margin-bottom: 4px;
  background-color: var(--bg-card);
  border-radius: 6px;
  border: 1px solid var(--border);
  font-size: 0.95rem;
  display: flex;
  gap: 8px;
  align-items: center;
  box-sizing: border-box;
  width: 100%;
  cursor: pointer;
  transition: all 0.2s ease;
}

.danmaku-item.is-expanded {
  height: auto;
  padding: 8px 10px;
  flex-direction: column;
  align-items: flex-start;
  gap: 4px;
}

.danmaku-item:hover {
  background-color: var(--bg-hover);
  border-color: var(--accent);
}

.danmaku-item.is-expanded .danmaku-text {
  white-space: normal;
  overflow: visible;
  text-overflow: clip;
  line-height: 1.5;
}

.danmaku-item.is-expanded .danmaku-content-wrapper {
  flex-direction: column;
  align-items: flex-start;
}

.danmaku-item.is-expanded .danmaku-time {
  align-self: flex-end;
  margin-top: 4px;
}

/* User Menu Popover */
.user-menu-content {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 4px 0;
}

.menu-item {
  padding: 8px 12px;
  cursor: pointer;
  display: flex;
  align-items: center;
  gap: 12px;
  font-size: 0.9rem;
  color: var(--text-primary);
  border-radius: 6px;
  transition: all 0.2s ease;
}

.menu-item:hover {
  background-color: var(--bg-hover);
  color: var(--accent);
}

.menu-item.disabled {
  opacity: 0.5;
  cursor: not-allowed;
  pointer-events: none;
}

.menu-icon {
  font-size: 1.1rem;
  width: 20px;
  display: flex;
  justify-content: center;
}

:deep(.user-menu-popover) {
  padding: 8px !important;
  border-radius: 12px !important;
  box-shadow: var(--shadow-md) !important;
  border: 1px solid var(--border) !important;
  background-color: var(--bg-card) !important;
}

.danmaku-time {
  color: var(--text-tertiary);
  font-size: 0.8rem;
  white-space: nowrap;
  font-variant-numeric: tabular-nums;
  flex-shrink: 0;
  order: 3; /* Matches styles.css */
}

.danmaku-content-wrapper {
  flex: 1;
  overflow: hidden;
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
}

.danmaku-username {
  font-weight: 600;
  color: var(--text-secondary);
  font-size: 0.85rem;
  cursor: pointer;
  white-space: nowrap;
  flex-shrink: 0;
}

.danmaku-username:hover {
  color: var(--el-color-primary);
}

.danmaku-text {
  flex: 1;
  color: var(--text-primary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

/* SC Styles */
.sc-item {
  border-left: 4px solid var(--accent);
  background-color: rgba(0, 113, 227, 0.05);
}

.sc-header {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-shrink: 0;
}

.sc-price {
  font-weight: 600;
  color: var(--accent);
  white-space: nowrap;
}
</style>