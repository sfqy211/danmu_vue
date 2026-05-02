<template>
  <div class="danmaku-container" v-loading="loading"
    @mousemove="onMouseMove" @mouseup="stopResize" @mouseleave="stopResize"
    @touchmove="onTouchMove" @touchend="stopResize" @touchcancel="stopResize">
    <div v-if="!currentSession && !loading" class="empty-state">
      <el-empty description="暂无弹幕数据，请选择左侧直播回放">
        <el-button type="primary" @click="store.toggleSidebar">打开侧边栏选择</el-button>
      </el-empty>
    </div>

    <div v-else-if="!isDanmakuLoaded && !loading" class="load-placeholder">
      <div class="load-card">
        <div class="load-icon">💬</div>
        <h3>已加载统计数据</h3>
        <p>该场次共有 {{ totalDanmaku }} 条弹幕</p>
        <el-button type="primary" size="large" :loading="danmakuLoading" @click="store.fetchDanmaku">
          查询弹幕列表
        </el-button>
      </div>
    </div>

    <div v-else ref="splitContainer" class="split-container" :class="{ 'is-mobile': isMobile, 'is-resizing': isResizing }">
      <!-- 左列：普通弹幕 -->
      <div ref="leftPanel" class="column-side column-left">
        <div class="column-header-sticky">
          <span>普通弹幕</span>
          <span class="badge">{{ filteredNormalList.length }}</span>
        </div>
        <div ref="leftScroller" class="scrollable-list" @scroll="onLeftScroll">
          <div :style="{ height: normalVirtualizer.getTotalSize() + 'px', width: '100%', position: 'relative' }">
            <div
              v-for="virtualItem in normalVirtualizer.getVirtualItems()"
              :key="String(virtualItem.key)"
              :ref="(el: any) => normalVirtualizer.measureElement(el as Element)"
              :data-index="virtualItem.index"
              :style="{
                position: 'absolute',
                top: virtualItem.start + 'px',
                left: 0,
                width: '100%',
              }"
              class="danmaku-item"
              :title="`UID: ${filteredNormalList[virtualItem.index].uid ?? '未知'}\n时间: ${store.timeDisplayMode === 'absolute' ? formatAbsoluteTime(filteredNormalList[virtualItem.index].timestamp) : filteredNormalList[virtualItem.index].timeStr}`"
            >
              <span class="dm-time">{{ store.timeDisplayMode === 'absolute' ? formatAbsoluteTime(filteredNormalList[virtualItem.index].timestamp) : filteredNormalList[virtualItem.index].timeStr }}</span>
              <span class="dm-meta">
                <span class="dm-user" @click="openUserMenu($event, filteredNormalList[virtualItem.index])">{{ filteredNormalList[virtualItem.index].user }}</span>
              </span>
              <span class="dm-message">{{ filteredNormalList[virtualItem.index].content }}</span>
            </div>
          </div>
        </div>
        <div v-if="filteredNormalList.length === 0" class="preview-empty">
          <p>暂无普通弹幕</p>
        </div>
      </div>

      <!-- Resize Handle -->
      <div
        class="resizer"
        role="separator"
        tabindex="0"
        aria-label="调整面板大小"
        :data-resize-handle-state="isResizing ? 'drag' : 'inactive'"
        @mousedown="startResize"
        @touchstart="startResize"
      >
        <div data-slot="handle" class="resizer-handle"></div>
      </div>

      <!-- 右列：SC -->
      <div ref="rightPanel" class="column-side column-right">
        <div class="column-header-sticky">
          <span>Super Chat</span>
          <span class="badge">{{ filteredSCList.length }}</span>
        </div>
        <div ref="rightScroller" class="scrollable-list" @scroll="onRightScroll">
          <div :style="{ height: scVirtualizer.getTotalSize() + 'px', width: '100%', position: 'relative' }">
            <div
              v-for="virtualItem in scVirtualizer.getVirtualItems()"
              :key="String(virtualItem.key)"
              :ref="(el: any) => scVirtualizer.measureElement(el as Element)"
              :data-index="virtualItem.index"
              :style="{
                position: 'absolute',
                top: virtualItem.start + 'px',
                left: 0,
                width: '100%',
                borderLeftColor: getSCStyle(filteredSCList[virtualItem.index].price || 0).main,
                backgroundColor: getSCStyle(filteredSCList[virtualItem.index].price || 0).bg,
              }"
              class="danmaku-item sc-item"
              :title="`UID: ${filteredSCList[virtualItem.index].uid ?? '未知'}\n金额: ¥${filteredSCList[virtualItem.index].price || 0}`"
            >
              <span class="dm-time">{{ store.timeDisplayMode === 'absolute' ? formatAbsoluteTime(filteredSCList[virtualItem.index].timestamp) : filteredSCList[virtualItem.index].timeStr }}</span>
              <span class="dm-meta">
                <span class="sc-price" :style="{ color: getSCStyle(filteredSCList[virtualItem.index].price || 0).main }">
                  ¥{{ formatPrice(filteredSCList[virtualItem.index].price || 0) }}
                </span>
                <span class="dm-user" @click="openUserMenu($event, filteredSCList[virtualItem.index])">
                  {{ filteredSCList[virtualItem.index].user }}
                </span>
              </span>
              <span class="dm-message">
                {{ filteredSCList[virtualItem.index].content }}
                <span v-if="filteredSCList[virtualItem.index].contentJpn" class="dm-jpn">{{ filteredSCList[virtualItem.index].contentJpn }}</span>
              </span>
            </div>
          </div>
        </div>
        <div v-if="filteredSCList.length === 0" class="preview-empty">
          <p>暂无付费弹幕</p>
        </div>
      </div>
    </div>

    <!-- Shared user menu dropdown with virtual triggering for correct positioning -->
    <el-dropdown
      ref="userMenuDropdown"
      :virtual-ref="virtualTriggerRef"
      virtual-triggering
      trigger="click"
      popper-class="user-menu-popover"
      @command="onUserMenuCommand"
    >
      <template #dropdown>
        <el-dropdown-menu>
          <el-dropdown-item command="filter">
            <span class="menu-icon">🔍</span>
            <span>筛选此用户弹幕</span>
          </el-dropdown-item>
          <el-dropdown-item command="profile">
            <span class="menu-icon">👤</span>
            <span>打开用户主页</span>
          </el-dropdown-item>
          <el-dropdown-item command="laplace">
            <span class="menu-icon">🧪</span>
            <span>查成分 (Laplace)</span>
          </el-dropdown-item>
        </el-dropdown-menu>
      </template>
    </el-dropdown>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, nextTick } from 'vue';
import { useVirtualizer, measureElement } from '@tanstack/vue-virtual';
import { useDanmakuStore } from '../stores/danmakuStore';
import { storeToRefs } from 'pinia';
import type { Danmaku } from '../api/danmaku';

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

// ==================== Data Filtering (must be before Virtual Scrolling) ====================

const filteredNormalList = computed(() => {
  let list = danmakuList.value.filter(d => !d.isSC);
  if (searchText.value) {
    const lower = searchText.value.toLowerCase();
    list = list.filter(d =>
      (d.content && d.content.toLowerCase().includes(lower)) ||
      (d.user && d.user.toLowerCase().includes(lower))
    );
  }
  return list;
});

const filteredSCList = computed(() => {
  let list = scList.value;
  if (searchText.value) {
    const lower = searchText.value.toLowerCase();
    list = list.filter(d =>
      (d.content && d.content.toLowerCase().includes(lower)) ||
      (d.user && d.user.toLowerCase().includes(lower))
    );
  }
  return list;
});

// ==================== Virtual Scrolling ====================

const ROW_HEIGHT = 32; // Fixed row height for virtual scrolling

const leftScroller = ref<HTMLElement | null>(null);
const rightScroller = ref<HTMLElement | null>(null);

const normalVirtualizer = useVirtualizer(computed(() => ({
  count: filteredNormalList.value.length,
  getScrollElement: () => leftScroller.value,
  estimateSize: () => ROW_HEIGHT,
  overscan: 10,
  measureElement,
})));

const scVirtualizer = useVirtualizer(computed(() => ({
  count: filteredSCList.value.length,
  getScrollElement: () => rightScroller.value,
  estimateSize: () => ROW_HEIGHT,
  overscan: 10,
  measureElement,
})));

// ==================== Shared User Menu ====================

const userMenuDropdown = ref<any>(null);
const activeUser = ref<Danmaku | null>(null);
const virtualTriggerRef = ref<HTMLElement>({
  getBoundingClientRect: () => DOMRect.fromRect({ x: 0, y: 0, width: 0, height: 0 }),
} as any);

const openUserMenu = (event: MouseEvent, item: Danmaku) => {
  activeUser.value = item;
  const rect = (event.target as HTMLElement).getBoundingClientRect();
  virtualTriggerRef.value = {
    getBoundingClientRect: () => new DOMRect(rect.left, rect.bottom + 4, rect.width, 0),
  } as any;
  nextTick(() => {
    if (userMenuDropdown.value) {
      userMenuDropdown.value.handleOpen();
    }
  });
};

const onUserMenuCommand = (command: string) => {
  if (!activeUser.value) return;
  const user = activeUser.value.user;
  const uid = activeUser.value.uid;
  switch (command) {
    case 'filter':
      store.searchText = user;
      break;
    case 'profile':
      if (uid) window.open(`https://space.bilibili.com/${uid}`, '_blank');
      break;
    case 'laplace':
      if (uid) window.open(`https://laplace.live/user/${uid}`, '_blank');
      break;
  }
};

// ==================== Helpers ====================

const formatAbsoluteTime = (timestamp: number) => {
  if (!timestamp) return '';
  const date = new Date(timestamp);
  return date.toLocaleTimeString('zh-CN', { hour12: false });
};

const getSCLevel = (price: number) => {
  if (price >= 2000) return 6;
  if (price >= 1000) return 5;
  if (price >= 500) return 4;
  if (price >= 100) return 3;
  if (price >= 50) return 2;
  return 1; // <50
};

const getSCStyle = (price: number) => {
  const level = getSCLevel(price);
  const styles: Record<number, { main: string; bg: string }> = {
    1: { main: '#2A60B2', bg: 'rgba(42, 96, 178, 0.08)' },
    2: { main: '#427D9E', bg: 'rgba(66, 125, 158, 0.08)' },
    3: { main: '#E2B52B', bg: 'rgba(226, 181, 43, 0.08)' },
    4: { main: '#E09443', bg: 'rgba(224, 148, 67, 0.08)' },
    5: { main: '#E54D4D', bg: 'rgba(229, 77, 77, 0.08)' },
    6: { main: '#AB1A32', bg: 'rgba(171, 26, 50, 0.08)' }
  };
  return styles[level] || styles[1];
};

const formatPrice = (price: number) => price.toString();

// ==================== Scroll / Load More ====================

const onLeftScroll = (e: Event) => {
  const target = e.target as HTMLElement;
  if (target.scrollTop + target.clientHeight >= target.scrollHeight - 50) {
    store.loadMore();
  }
};

const onRightScroll = (e: Event) => {
  const target = e.target as HTMLElement;
  if (target.scrollTop + target.clientHeight >= target.scrollHeight - 50) {
    store.loadMore();
  }
};

// ==================== Resizer (LAPLACE-style flex) ====================

const splitContainer = ref<HTMLElement | null>(null);
const leftPanel = ref<HTMLElement | null>(null);
const rightPanel = ref<HTMLElement | null>(null);
const isResizing = ref(false);
const isMobile = ref(window.innerWidth <= 768);

let leftFlex = 70;
let rafId = 0;

const updateMobileStatus = () => {
  isMobile.value = window.innerWidth <= 768;
};

const applyFlexLayout = () => {
  if (!leftPanel.value || !rightPanel.value) return;
  const rightFlex = 100 - leftFlex;
  leftPanel.value.style.flex = `${leftFlex} 1 0px`;
  rightPanel.value.style.flex = `${rightFlex} 1 0px`;
};

const startResize = (e: MouseEvent | TouchEvent) => {
  isResizing.value = true;
  document.body.style.cursor = isMobile.value ? 'row-resize' : 'col-resize';
  document.body.style.userSelect = 'none';
  if ('preventDefault' in e) e.preventDefault();
};

const stopResize = () => {
  if (!isResizing.value) return;
  isResizing.value = false;
  document.body.style.cursor = '';
  document.body.style.userSelect = '';
  if (rafId) {
    cancelAnimationFrame(rafId);
    rafId = 0;
  }
};

const onMouseMove = (e: MouseEvent) => {
  if (!isResizing.value) return;
  if (rafId) return;
  rafId = requestAnimationFrame(() => {
    rafId = 0;
    handleResizeMove(e.clientX, e.clientY);
  });
};

const onTouchMove = (e: TouchEvent) => {
  if (!isResizing.value || e.touches.length === 0) return;
  e.preventDefault();
  if (rafId) return;
  rafId = requestAnimationFrame(() => {
    rafId = 0;
    handleResizeMove(e.touches[0].clientX, e.touches[0].clientY);
  });
};

const handleResizeMove = (clientX: number, clientY: number) => {
  if (!splitContainer.value) return;
  const containerRect = splitContainer.value.getBoundingClientRect();
  let ratio: number;
  if (isMobile.value) {
    ratio = ((clientY - containerRect.top) / containerRect.height) * 100;
  } else {
    ratio = ((clientX - containerRect.left) / containerRect.width) * 100;
  }
  ratio = Math.max(15, Math.min(85, ratio));
  leftFlex = ratio;
  applyFlexLayout();
};

onMounted(() => {
  window.addEventListener('resize', updateMobileStatus);
  window.addEventListener('mouseup', stopResize);
  window.addEventListener('touchend', stopResize);
  window.addEventListener('touchcancel', stopResize);
  applyFlexLayout();
});

onUnmounted(() => {
  window.removeEventListener('resize', updateMobileStatus);
  window.removeEventListener('mouseup', stopResize);
  window.removeEventListener('touchend', stopResize);
  window.removeEventListener('touchcancel', stopResize);
  if (rafId) cancelAnimationFrame(rafId);
});
</script>

<style scoped>
.danmaku-container {
  flex: 1;
  display: flex;
  flex-direction: column;
  background-color: var(--bg-primary);
  overflow: hidden;
}

.empty-state,
.load-placeholder {
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

/* Split Container — LAPLACE-style flex-based panels */
.split-container {
  flex: 1;
  display: flex;
  flex-direction: row;
  overflow: hidden;
  position: relative;
  min-height: 0;
}

.split-container.is-mobile {
  flex-direction: column;
}

.split-container.is-resizing .column-side {
  transition: none !important;
}

.column-side {
  display: flex;
  flex-direction: column;
  flex: 1 1 0px;
  min-width: 0;
  min-height: 0;
  background-color: var(--bg-primary);
  position: relative;
  overflow: hidden;
  transition: flex 0.15s ease;
}

.column-header-sticky {
  padding: 10px 14px;
  font-weight: 600;
  color: var(--text-secondary);
  border-bottom: 1px solid var(--border);
  display: flex;
  justify-content: space-between;
  align-items: center;
  background-color: var(--bg-primary);
  z-index: 10;
  flex-shrink: 0;
  font-size: 0.9rem;
}

.badge {
  background-color: var(--el-color-primary-light-9);
  padding: 1px 7px;
  border-radius: 10px;
  font-size: 0.75rem;
  color: var(--el-color-primary);
  font-weight: 500;
}

/* Scrollable list */
.scrollable-list {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  position: relative;
  overscroll-behavior: contain;
}

.preview-empty {
  display: flex;
  justify-content: center;
  align-items: center;
  padding: 20px;
  color: var(--text-tertiary);
  font-size: 0.85rem;
}



/* Resizer — LAPLACE-style */
.resizer {
  position: relative;
  z-index: 20;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  touch-action: none;
  user-select: none;
  -webkit-user-select: none;
  outline: none;
  transition: background-color 0.15s ease;
}

.split-container:not(.is-mobile) .resizer {
  width: 1px;
  height: 100%;
  cursor: col-resize;
  background-color: var(--border);
}

.split-container:not(.is-mobile) .resizer::after {
  content: '';
  position: absolute;
  inset: 0;
  left: -4px;
  right: -4px;
  z-index: -1;
}

.split-container:not(.is-mobile) .resizer:hover,
.split-container:not(.is-mobile) .resizer[data-resize-handle-state="drag"] {
  background-color: var(--el-color-primary);
}

.resizer-handle {
  display: none;
  border-radius: 4px;
  background-color: var(--el-color-primary);
  opacity: 0;
  transition: opacity 0.15s ease;
}

.split-container:not(.is-mobile) .resizer-handle {
  width: 4px;
  height: 48px;
}

.split-container:not(.is-mobile) .resizer:hover .resizer-handle,
.split-container:not(.is-mobile) .resizer[data-resize-handle-state="drag"] .resizer-handle {
  display: block;
  opacity: 1;
}

.split-container.is-mobile .resizer {
  height: 1px;
  width: 100%;
  cursor: row-resize;
  background-color: var(--border);
  margin: 0;
}

.split-container.is-mobile .resizer::after {
  content: '';
  position: absolute;
  inset: 0;
  top: -4px;
  bottom: -4px;
  z-index: -1;
}

.split-container.is-mobile .resizer:hover,
.split-container.is-mobile .resizer[data-resize-handle-state="drag"] {
  background-color: var(--el-color-primary);
}

.split-container.is-mobile .resizer-handle {
  width: 48px;
  height: 4px;
}

.split-container.is-mobile .resizer:hover .resizer-handle,
.split-container.is-mobile .resizer[data-resize-handle-state="drag"] .resizer-handle {
  display: block;
  opacity: 1;
}

/* ========== Virtual-scrolled danmaku rows ========== */

.danmaku-item {
  display: flex;
  align-items: baseline;
  gap: 3px;
  padding: 4px 10px;
  font-size: 0.9rem;
  line-height: 2;
  /* Override global style.css card styles */
  border: none !important;
  border-radius: 0 !important;
  margin: 0 !important;
  min-height: auto !important;
  box-sizing: border-box;
  /* Light separator */
  border-bottom: 1px solid rgba(128, 128, 128, 0.06) !important;
}

.danmaku-item:last-child {
  border-bottom: none !important;
}

/* SC: left color bar + taller line height */
.danmaku-item.sc-item {
  border-left: 3px solid transparent;
  padding-left: 7px;
  line-height: 2.5;
}

/* Time: far left, muted */
.dm-time {
  flex-shrink: 0;
  width: 48px;
  margin-right: 2px;
  font-size: 0.75rem;
  color: var(--text-tertiary);
  opacity: 0.45;
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
  overflow: hidden;
}

/* Meta: username container */
.dm-meta {
  display: inline-flex;
  align-items: baseline;
  gap: 2px;
  flex-shrink: 0;
  margin-right: 2px;
}

/* Username */
.dm-user {
  max-width: 140px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-weight: 600;
  color: #409eff;
  font-size: 0.85rem;
  cursor: pointer;
}

.dm-user:hover {
  text-decoration: underline;
}

/* Message */
.dm-message {
  flex: 1;
  min-width: 0;
  color: var(--text-primary);
  white-space: normal;
  word-break: break-all;
}

.dm-jpn {
  color: var(--text-secondary);
  font-size: 0.8rem;
  margin-left: 4px;
  opacity: 0.75;
}

/* SC Price */
.sc-price {
  font-weight: 700;
  font-size: 0.8rem;
  flex-shrink: 0;
}

/* User Menu Dropdown */
:deep(.user-menu-popover) {
  padding: 8px !important;
  border-radius: 12px !important;
  box-shadow: var(--shadow-md) !important;
  border: 1px solid var(--border) !important;
  background-color: var(--bg-card) !important;
}

:deep(.user-menu-popover .el-dropdown-menu) {
  border: none !important;
  box-shadow: none !important;
  background-color: transparent !important;
  padding: 0 !important;
}

:deep(.user-menu-popover .el-dropdown-menu__item) {
  padding: 8px 12px !important;
  display: flex !important;
  align-items: center !important;
  gap: 12px !important;
  font-size: 0.9rem !important;
  color: var(--text-primary) !important;
  border-radius: 6px !important;
}

:deep(.user-menu-popover .el-dropdown-menu__item:hover) {
  background-color: var(--bg-hover) !important;
  color: var(--accent) !important;
}

:deep(.user-menu-popover .el-dropdown-menu__item.is-disabled) {
  opacity: 0.5 !important;
  cursor: not-allowed !important;
}

.menu-icon {
  font-size: 1.1rem;
  width: 20px;
  display: flex;
  justify-content: center;
}
</style>