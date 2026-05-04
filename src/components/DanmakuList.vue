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

    <!-- ═══════════════ FOUR-ZONE SPLIT LAYOUT ═══════════════ -->
    <div v-else ref="splitContainer" class="split-container" :class="{ 'is-mobile': isMobile, 'is-resizing': isResizing }">

      <!-- ─── LEFT SECTION (flex column: top / bottom) ─── -->
      <div ref="leftPanel" class="column-side column-left-section"
        :style="{ flex: flexLeft + ' 1 0px' }"
        :class="{ 'pane-hidden': isLeftHidden }">

        <!-- LEFT TOP: DANMU_MSG (normal comments) -->
        <div class="pane-inner pane-top"
          :style="{ flex: flexTop + ' 1 0px' }"
          :class="{ 'pane-hidden': isTopHidden }">
          <div class="pane-header">
            <span>普通弹幕</span>
            <span class="badge">{{ normalList.length }}</span>
          </div>
          <div ref="normalScroller" class="scrollable-list" @scroll="onNormalScroll">
            <div :style="{ height: normalVirtualizer.getTotalSize() + 'px', width: '100%', position: 'relative' }">
              <div v-for="virtualItem in normalVirtualizer.getVirtualItems()"
                :key="String(virtualItem.key)"
                :ref="(el: any) => normalVirtualizer.measureElement(el as Element)"
                :data-index="virtualItem.index"
                :style="{ position: 'absolute', top: virtualItem.start + 'px', left: 0, width: '100%' }"
                class="danmaku-item">
                <span class="dm-time">{{ store.timeDisplayMode === 'absolute' ? formatAbsoluteTime(normalList[virtualItem.index].timestamp) : normalList[virtualItem.index].timeStr }}</span>
                <span v-if="normalList[virtualItem.index].wealthLevel" class="wealth-level">{{ normalList[virtualItem.index].wealthLevel }}</span>
                <FansMedal :item="normalList[virtualItem.index]" />
                <span class="dm-user" @click="openUserMenu($event, normalList[virtualItem.index])">{{ normalList[virtualItem.index].user }}</span>
                <span class="dm-message">{{ normalList[virtualItem.index].content }}</span>
              </div>
            </div>
          </div>
          <div v-if="normalList.length === 0" class="pane-empty">暂无普通弹幕</div>
        </div>

        <!-- Horizontal Resizer (left top / left bottom) -->
        <div class="resizer resizer-h"
          :class="{ 'resizer-hidden': isTopHidden || isBottomHidden }"
          @mousedown="startResize('h', $event)" @touchstart="startResize('h', $event)">
          <div class="resizer-handle"></div>
        </div>

        <!-- LEFT BOTTOM: INTERACT_WORD, ROOM_CHANGE, etc. -->
        <div class="pane-inner pane-bottom"
          :style="{ flex: flexBottom + ' 1 0px' }"
          :class="{ 'pane-hidden': isBottomHidden }">
          <div class="pane-header">
            <span>互动信息</span>
            <span class="badge">{{ infoList.length }}</span>
          </div>
          <div ref="infoScroller" class="scrollable-list" @scroll="onInfoScroll">
            <div :style="{ height: infoVirtualizer.getTotalSize() + 'px', width: '100%', position: 'relative' }">
              <div v-for="virtualItem in infoVirtualizer.getVirtualItems()"
                :key="String(virtualItem.key)"
                :ref="(el: any) => infoVirtualizer.measureElement(el as Element)"
                :data-index="virtualItem.index"
                :style="{ position: 'absolute', top: virtualItem.start + 'px', left: 0, width: '100%' }"
                class="danmaku-item info-item">
                <span class="dm-time">{{ store.timeDisplayMode === 'absolute' ? formatAbsoluteTime(infoList[virtualItem.index].timestamp) : infoList[virtualItem.index].timeStr }}</span>
                <span v-if="infoList[virtualItem.index].wealthLevel" class="wealth-level">{{ infoList[virtualItem.index].wealthLevel }}</span>
                <FansMedal :item="infoList[virtualItem.index]" />
                <span class="dm-user" @click="openUserMenu($event, infoList[virtualItem.index])">{{ infoList[virtualItem.index].user }}</span>
                <span class="info-type-label">{{ getEventTypeLabel(infoList[virtualItem.index]) }}</span>
                <span v-if="infoList[virtualItem.index].content" class="dm-message">{{ infoList[virtualItem.index].content }}</span>
              </div>
            </div>
          </div>
          <div v-if="infoList.length === 0" class="pane-empty">暂无互动信息</div>
        </div>

      </div>

      <!-- Vertical Resizer 1 (left-section / middle) -->
      <div class="resizer resizer-v"
        :class="{ 'resizer-hidden': isLeftHidden && isMiddleHidden }"
        @mousedown="startResize('v1', $event)" @touchstart="startResize('v1', $event)">
        <div class="resizer-handle"></div>
      </div>

      <!-- ─── MIDDLE: SUPER_CHAT_MESSAGE / JPN ─── -->
      <div class="column-side column-middle"
        :style="{ flex: flexMiddle + ' 1 0px' }"
        :class="{ 'pane-hidden': isMiddleHidden }">
        <div class="pane-header">
          <span>醒目留言 (SC)</span>
          <span class="badge">{{ scList.length }}</span>
        </div>
        <div ref="scScroller" class="scrollable-list" @scroll="onSCScroll">
          <div :style="{ height: scVirtualizer.getTotalSize() + 'px', width: '100%', position: 'relative' }">
            <div v-for="virtualItem in scVirtualizer.getVirtualItems()"
              :key="String(virtualItem.key)"
              :ref="(el: any) => scVirtualizer.measureElement(el as Element)"
              :data-index="virtualItem.index"
              :class="['danmaku-item', 'monetary-item', 'type-super_chat']"
              :style="{
                position: 'absolute', top: virtualItem.start + 'px', left: 0, width: '100%',
                borderLeftColor: getSCStyle(scList[virtualItem.index].price || 0).main,
                backgroundColor: getSCStyle(scList[virtualItem.index].price || 0).bg,
              }">
              <span class="dm-time">{{ store.timeDisplayMode === 'absolute' ? formatAbsoluteTime(scList[virtualItem.index].timestamp) : scList[virtualItem.index].timeStr }}</span>
              <span v-if="scList[virtualItem.index].wealthLevel" class="wealth-level">{{ scList[virtualItem.index].wealthLevel }}</span>
              <FansMedal :item="scList[virtualItem.index]" />
              <span class="dm-user" @click="openUserMenu($event, scList[virtualItem.index])">{{ scList[virtualItem.index].user }}</span>
              <span class="dm-meta">
                <span class="sc-price" :style="{ color: getSCStyle(scList[virtualItem.index].price || 0).main }">¥{{ formatPrice(scList[virtualItem.index].price || 0) }}</span>
              </span>
              <span class="dm-message">
                {{ scList[virtualItem.index].content }}
                <span v-if="scList[virtualItem.index].contentJpn" class="dm-jpn">{{ scList[virtualItem.index].contentJpn }}</span>
              </span>
            </div>
          </div>
        </div>
        <div v-if="scList.length === 0" class="pane-empty">暂无 SC</div>
      </div>

      <!-- Vertical Resizer 2 (middle / right) -->
      <div class="resizer resizer-v"
        :class="{ 'resizer-hidden': isMiddleHidden && isRightHidden }"
        @mousedown="startResize('v2', $event)" @touchstart="startResize('v2', $event)">
        <div class="resizer-handle"></div>
      </div>

      <!-- ─── RIGHT: SEND_GIFT, GUARD_BUY, COMBO_SEND ─── -->
      <div class="column-side column-right"
        :style="{ flex: flexRight + ' 1 0px' }"
        :class="{ 'pane-hidden': isRightHidden }">
        <div class="pane-header">
          <span>礼物 / 上舰</span>
          <span class="badge">{{ giftList.length }}</span>
        </div>
        <div ref="giftScroller" class="scrollable-list" @scroll="onGiftScroll">
          <div :style="{ height: giftVirtualizer.getTotalSize() + 'px', width: '100%', position: 'relative' }">
            <div v-for="virtualItem in giftVirtualizer.getVirtualItems()"
              :key="String(virtualItem.key)"
              :ref="(el: any) => giftVirtualizer.measureElement(el as Element)"
              :data-index="virtualItem.index"
              :class="['danmaku-item', 'monetary-item', `type-${getEventType(giftList[virtualItem.index])}`]"
              :style="{
                position: 'absolute', top: virtualItem.start + 'px', left: 0, width: '100%',
              }">

              <!-- GIFT: SEND_GIFT -->
              <template v-if="getEventType(giftList[virtualItem.index]) === 'give_gift'">
                <span class="dm-time">{{ store.timeDisplayMode === 'absolute' ? formatAbsoluteTime(giftList[virtualItem.index].timestamp) : giftList[virtualItem.index].timeStr }}</span>
                <span v-if="giftList[virtualItem.index].wealthLevel" class="wealth-level">{{ giftList[virtualItem.index].wealthLevel }}</span>
                <FansMedal :item="giftList[virtualItem.index]" />
                <span class="dm-user" @click="openUserMenu($event, giftList[virtualItem.index])">{{ giftList[virtualItem.index].user }}</span>
                <span class="dm-meta">
                  <span class="gift-name">{{ giftList[virtualItem.index].name }}</span>
                  <span class="gift-count">x{{ giftList[virtualItem.index].count || 1 }}</span>
                  <span v-if="giftList[virtualItem.index].price" class="gift-price">¥{{ formatPrice(giftList[virtualItem.index].price || 0) }}</span>
                </span>
              </template>

              <!-- GUARD: GUARD_BUY -->
              <template v-else-if="getEventType(giftList[virtualItem.index]) === 'guard'">
                <span class="dm-time">{{ store.timeDisplayMode === 'absolute' ? formatAbsoluteTime(giftList[virtualItem.index].timestamp) : giftList[virtualItem.index].timeStr }}</span>
                <span v-if="giftList[virtualItem.index].wealthLevel" class="wealth-level">{{ giftList[virtualItem.index].wealthLevel }}</span>
                <FansMedal :item="giftList[virtualItem.index]" />
                <span class="dm-user" @click="openUserMenu($event, giftList[virtualItem.index])">{{ giftList[virtualItem.index].user }}</span>
                <span class="dm-meta">
                  <span class="guard-badge-inline">{{ getGuardName(giftList[virtualItem.index].guardLevel) }}</span>
                  <span v-if="giftList[virtualItem.index].price" class="guard-price">¥{{ formatPrice(giftList[virtualItem.index].price || 0) }}</span>
                </span>
              </template>

              <!-- GIFT COMBO: COMBO_SEND -->
              <template v-else-if="getEventType(giftList[virtualItem.index]) === 'gift_combo'">
                <span class="dm-time">{{ store.timeDisplayMode === 'absolute' ? formatAbsoluteTime(giftList[virtualItem.index].timestamp) : giftList[virtualItem.index].timeStr }}</span>
                <span v-if="giftList[virtualItem.index].wealthLevel" class="wealth-level">{{ giftList[virtualItem.index].wealthLevel }}</span>
                <FansMedal :item="giftList[virtualItem.index]" />
                <span class="dm-user" @click="openUserMenu($event, giftList[virtualItem.index])">{{ giftList[virtualItem.index].user }}</span>
                <span class="dm-meta">
                  <span class="gift-name">{{ giftList[virtualItem.index].name }}</span>
                  <span class="gift-count combo-count">x{{ giftList[virtualItem.index].count || 1 }}</span>
                </span>
              </template>

              <!-- Fallback -->
              <template v-else>
                <span class="dm-time">{{ store.timeDisplayMode === 'absolute' ? formatAbsoluteTime(giftList[virtualItem.index].timestamp) : giftList[virtualItem.index].timeStr }}</span>
                <span v-if="giftList[virtualItem.index].wealthLevel" class="wealth-level">{{ giftList[virtualItem.index].wealthLevel }}</span>
                <FansMedal :item="giftList[virtualItem.index]" />
                <span class="dm-user" @click="openUserMenu($event, giftList[virtualItem.index])">{{ giftList[virtualItem.index].user }}</span>
                <span class="dm-meta">
                  <span class="generic-type">{{ getEventTypeLabel(giftList[virtualItem.index]) }}</span>
                </span>
              </template>
            </div>
          </div>
        </div>
        <div v-if="giftList.length === 0" class="pane-empty">暂无礼物 / 上舰</div>
      </div>
    </div>

    <!-- Shared user menu dropdown -->
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
          <el-dropdown-item command="filter"><span class="menu-icon">🔍</span><span>筛选此用户弹幕</span></el-dropdown-item>
          <el-dropdown-item command="profile"><span class="menu-icon">👤</span><span>打开用户主页</span></el-dropdown-item>
          <el-dropdown-item command="laplace"><span class="menu-icon">🧪</span><span>查成分 (Laplace)</span></el-dropdown-item>
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
import FansMedal from './FansMedal.vue';

const store = useDanmakuStore();
const {
  danmakuList,
  loading,
  danmakuLoading,
  isDanmakuLoaded,
  currentSession,
  totalDanmaku,
  searchText
} = storeToRefs(store);

// ==================== Helpers ====================

const getEventType = (d: Danmaku): string => {
  if (d.type) return d.type;
  return d.isSC ? 'super_chat' : 'comment';
};

const getGuardName = (level?: number) => {
  switch (level) {
    case 1: return '总督';
    case 2: return '提督';
    case 3: return '舰长';
    default: return '';
  }
};

const getEventTypeLabel = (d: Danmaku): string => {
  const t = getEventType(d);
  switch (t) {
    case 'comment': return '弹幕';
    case 'super_chat': return 'SC';
    case 'give_gift': return '礼物';
    case 'guard': return '上舰';
    case 'gift_combo': return '连击';
    case 'enter': return '入场';
    case 'follow': return '关注';
    case 'share': return '分享';
    case 'interact': return '互动';
    case 'room_change': return '房间变更';
    default: return t;
  }
};

const getEventTypeColor = (d: Danmaku): string => {
  const t = getEventType(d);
  switch (t) {
    case 'super_chat': return '#E54D4D';
    case 'give_gift': return '#E2B52B';
    case 'guard': return '#AB1A32';
    case 'gift_combo': return '#E09443';
    case 'enter': return '#427D9E';
    case 'follow': return '#95d475';
    case 'share': return '#79bbff';
    case 'interact': return '#a0cfff';
    case 'room_change': return '#c8c9cc';
    default: return '#409eff';
  }
};

const formatAbsoluteTime = (timestamp: number) => {
  if (!timestamp) return '';
  return new Date(timestamp).toLocaleTimeString('zh-CN', { hour12: false });
};

const getSCLevel = (price: number) => {
  if (price >= 2000) return 6;
  if (price >= 1000) return 5;
  if (price >= 500) return 4;
  if (price >= 100) return 3;
  if (price >= 50) return 2;
  return 1;
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

// ==================== Computed filtered lists ====================

const applySearch = (list: Danmaku[], searchFields: ('content' | 'user' | 'name')[]) => {
  if (!searchText.value) return list;
  const lower = searchText.value.toLowerCase();
  return list.filter(d =>
    searchFields.some(f => {
      const val = d[f];
      return val && val.toLowerCase().includes(lower);
    })
  );
};

/** Left top: DANMU_MSG → type === 'comment' */
const normalList = computed(() => {
  let list = danmakuList.value.filter(d => getEventType(d) === 'comment');
  return applySearch(list, ['content', 'user']);
});

/** Left bottom: INTERACT_WORD, ROOM_CHANGE, enter, follow, share */
const infoList = computed(() => {
  let list = danmakuList.value.filter(d => {
    const t = getEventType(d);
    return t === 'interact' || t === 'room_change' || t === 'enter' || t === 'follow' || t === 'share';
  });
  return applySearch(list, ['content', 'user']);
});

/** Middle: SUPER_CHAT_MESSAGE / JPN → type === 'super_chat' */
const scList = computed(() => {
  let list = danmakuList.value.filter(d => getEventType(d) === 'super_chat');
  return applySearch(list, ['content', 'user']);
});

/** Right: SEND_GIFT, GUARD_BUY, COMBO_SEND */
const giftList = computed(() => {
  let list = danmakuList.value.filter(d => {
    const t = getEventType(d);
    return t === 'give_gift' || t === 'guard' || t === 'gift_combo';
  });
  return applySearch(list, ['content', 'user', 'name']);
});

// ==================== Virtual Scrolling ====================

const ROW_HEIGHT = 32;

const normalScroller = ref<HTMLElement | null>(null);
const infoScroller = ref<HTMLElement | null>(null);
const scScroller = ref<HTMLElement | null>(null);
const giftScroller = ref<HTMLElement | null>(null);

const normalVirtualizer = useVirtualizer(computed(() => ({
  count: normalList.value.length,
  getScrollElement: () => normalScroller.value,
  estimateSize: () => ROW_HEIGHT,
  overscan: 10,
  measureElement,
})));

const infoVirtualizer = useVirtualizer(computed(() => ({
  count: infoList.value.length,
  getScrollElement: () => infoScroller.value,
  estimateSize: () => ROW_HEIGHT,
  overscan: 10,
  measureElement,
})));

const scVirtualizer = useVirtualizer(computed(() => ({
  count: scList.value.length,
  getScrollElement: () => scScroller.value,
  estimateSize: () => ROW_HEIGHT,
  overscan: 10,
  measureElement,
})));

const giftVirtualizer = useVirtualizer(computed(() => ({
  count: giftList.value.length,
  getScrollElement: () => giftScroller.value,
  estimateSize: () => ROW_HEIGHT,
  overscan: 10,
  measureElement,
})));

// ==================== Scroll / Load More ====================

const onNormalScroll = (e: Event) => {
  const t = e.target as HTMLElement;
  if (t.scrollTop + t.clientHeight >= t.scrollHeight - 50) store.loadMore();
};
const onInfoScroll = (e: Event) => {
  const t = e.target as HTMLElement;
  if (t.scrollTop + t.clientHeight >= t.scrollHeight - 50) store.loadMore();
};
const onSCScroll = (e: Event) => {
  const t = e.target as HTMLElement;
  if (t.scrollTop + t.clientHeight >= t.scrollHeight - 50) store.loadMore();
};
const onGiftScroll = (e: Event) => {
  const t = e.target as HTMLElement;
  if (t.scrollTop + t.clientHeight >= t.scrollHeight - 50) store.loadMore();
};

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
    if (userMenuDropdown.value) userMenuDropdown.value.handleOpen();
  });
};

const onUserMenuCommand = (command: string) => {
  if (!activeUser.value) return;
  const user = activeUser.value.user;
  const uid = activeUser.value.uid;
  switch (command) {
    case 'filter': store.searchText = user; break;
    case 'profile': if (uid) window.open(`https://space.bilibili.com/${uid}`, '_blank'); break;
    case 'laplace': if (uid) window.open(`https://laplace.live/user/${uid}`, '_blank'); break;
  }
};

// ==================== Resizer State ====================

const splitContainer = ref<HTMLElement | null>(null);
const leftPanel = ref<HTMLElement | null>(null);

const isResizing = ref(false);
const activeResizer = ref<string | null>(null);
const isMobile = ref(window.innerWidth <= 768);

// ==================== Pane Sizing (flex-grow values) ====================
// These are relative flex-grow values. The ratios determine space distribution.
// MIN_FLEX = below this, a pane auto-hides.

const MIN_FLEX = 0.3;

// Vertical panes
const flexLeft = ref(4);
const flexMiddle = ref(3);
const flexRight = ref(3);

// Within left section (horizontal split)
const flexTop = ref(6);
const flexBottom = ref(4);

// Computed hidden states
const isLeftHidden = computed(() => flexLeft.value < MIN_FLEX);
const isMiddleHidden = computed(() => flexMiddle.value < MIN_FLEX);
const isRightHidden = computed(() => flexRight.value < MIN_FLEX);
const isTopHidden = computed(() => flexTop.value < MIN_FLEX);
const isBottomHidden = computed(() => flexBottom.value < MIN_FLEX);

// On mobile, hide right (gifts) and left-bottom (info) by default
onMounted(() => {
  updateMobileStatus();
  if (isMobile.value) {
    flexRight.value = 0;
    flexBottom.value = 0;
  }
});

const updateMobileStatus = () => {
  isMobile.value = window.innerWidth <= 768;
};

// ==================== Resize Logic ====================

let rafId = 0;

const startResize = (id: string, e: MouseEvent | TouchEvent) => {
  activeResizer.value = id;
  isResizing.value = true;
  const cursorMap: Record<string, string> = {
    v1: 'col-resize', v2: 'col-resize', h: 'row-resize',
  };
  document.body.style.cursor = cursorMap[id] || 'col-resize';
  document.body.style.userSelect = 'none';
  if ('preventDefault' in e) e.preventDefault();
};

const stopResize = () => {
  if (!isResizing.value) return;
  isResizing.value = false;
  activeResizer.value = null;
  document.body.style.cursor = '';
  document.body.style.userSelect = '';
  if (rafId) { cancelAnimationFrame(rafId); rafId = 0; }
};

const onMouseMove = (e: MouseEvent) => {
  if (!isResizing.value || !activeResizer.value) return;
  if (rafId) return;
  rafId = requestAnimationFrame(() => {
    rafId = 0;
    handleResizeMove(e.clientX, e.clientY);
  });
};

const onTouchMove = (e: TouchEvent) => {
  if (!isResizing.value || !activeResizer.value || e.touches.length === 0) return;
  e.preventDefault();
  if (rafId) return;
  rafId = requestAnimationFrame(() => {
    rafId = 0;
    handleResizeMove(e.touches[0].clientX, e.touches[0].clientY);
  });
};

/**
 * Core resize handler.
 * Uses pointer position relative to splitContainer to directly set flex-grow
 * ratios for the two panes adjacent to the active resizer.
 * Panes that fall below MIN_FLEX auto-hide; hidden panes auto-restore when
 * the resizer is dragged to give them space.
 */
const handleResizeMove = (clientX: number, clientY: number) => {
  const container = splitContainer.value;
  if (!container || !activeResizer.value) return;

  // On mobile (column layout) or for the internal horizontal resizer, use Y axis.
  // On desktop (row layout) for v1/v2 resizers, use X axis.
  const useYAxis = isMobile.value || activeResizer.value === 'h';

  let relPos: number;
  let containerSize: number;

  if (useYAxis) {
    // For 'h' resizer, compute relative to left panel height.
    // For mobile v1/v2, compute relative to the main container.
    const parentEl = activeResizer.value === 'h' ? leftPanel.value : container;
    const parentRect = parentEl?.getBoundingClientRect();
    if (!parentRect) return;
    relPos = Math.max(0, Math.min(parentRect.height, clientY - parentRect.top));
    containerSize = parentRect.height;
  } else {
    const rect = container.getBoundingClientRect();
    relPos = Math.max(0, Math.min(rect.width, clientX - rect.left));
    containerSize = rect.width;
  }

  const fraction = containerSize > 0 ? relPos / containerSize : 0.5;

  if (activeResizer.value === 'v1') {
    // Between left and middle
    const lmTotal = flexLeft.value + flexMiddle.value;
    const totalFlex = flexLeft.value + flexMiddle.value + flexRight.value;
    let newLeft = fraction * totalFlex;
    newLeft = Math.max(0, Math.min(lmTotal, newLeft));

    if (newLeft < MIN_FLEX) {
      flexLeft.value = 0;
      flexMiddle.value = lmTotal;
    } else if (lmTotal - newLeft < MIN_FLEX) {
      flexMiddle.value = 0;
      flexLeft.value = lmTotal;
    } else {
      flexLeft.value = newLeft;
      flexMiddle.value = lmTotal - newLeft;
    }
  } else if (activeResizer.value === 'v2') {
    // Between middle and right
    const totalFlex = flexLeft.value + flexMiddle.value + flexRight.value;
    const mrTotal = flexMiddle.value + flexRight.value;
    let newMiddle = fraction * totalFlex - flexLeft.value;
    newMiddle = Math.max(0, Math.min(mrTotal, newMiddle));

    if (newMiddle < MIN_FLEX) {
      flexMiddle.value = 0;
      flexRight.value = mrTotal;
    } else if (mrTotal - newMiddle < MIN_FLEX) {
      flexRight.value = 0;
      flexMiddle.value = mrTotal;
    } else {
      flexMiddle.value = newMiddle;
      flexRight.value = mrTotal - newMiddle;
    }
  } else if (activeResizer.value === 'h') {
    // Horizontal split within left section
    const tbTotal = flexTop.value + flexBottom.value;
    let newTop = fraction * tbTotal;
    newTop = Math.max(0, Math.min(tbTotal, newTop));

    if (newTop < MIN_FLEX) {
      flexTop.value = 0;
      flexBottom.value = tbTotal;
    } else if (tbTotal - newTop < MIN_FLEX) {
      flexBottom.value = 0;
      flexTop.value = tbTotal;
    } else {
      flexTop.value = newTop;
      flexBottom.value = tbTotal - newTop;
    }
  }
};

// ==================== Lifecycle ====================

onMounted(() => {
  window.addEventListener('resize', updateMobileStatus);
  window.addEventListener('mouseup', stopResize);
  window.addEventListener('touchend', stopResize);
  window.addEventListener('touchcancel', stopResize);
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

.load-icon { font-size: 48px; margin-bottom: 16px; }
.load-card h3 { margin: 0 0 8px; color: var(--text-primary); font-size: 1.2rem; }
.load-card p { margin: 0 0 24px; color: var(--text-secondary); font-size: 0.9rem; }

/* ═══════ Split Container ═══════ */
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
.split-container.is-resizing .column-side,
.split-container.is-resizing .pane-inner {
  transition: none !important;
}

/* ═══════ Column Side (vertical panes) ═══════ */
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

.column-side.pane-hidden {
  display: none;
}

/* ═══════ Inner pane (within left section) ═══════ */
.pane-inner {
  display: flex;
  flex-direction: column;
  flex: 1 1 0px;
  min-height: 0;
  overflow: hidden;
  transition: flex 0.15s ease;
}
.pane-inner.pane-hidden {
  display: none;
}

/* ═══════ Pane Header ═══════ */
.pane-header {
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

/* ═══════ Scrollable List ═══════ */
.scrollable-list {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  position: relative;
  overscroll-behavior: contain;
}

.pane-empty {
  display: flex;
  justify-content: center;
  align-items: center;
  padding: 20px;
  color: var(--text-tertiary);
  font-size: 0.85rem;
}

/* ═══════ Resizer — vertical ═══════ */
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

.resizer-hidden {
  display: none;
}

.split-container:not(.is-mobile) .resizer.resizer-v {
  width: 1px;
  height: 100%;
  cursor: col-resize;
  background-color: var(--border);
}
.split-container:not(.is-mobile) .resizer.resizer-v::after {
  content: '';
  position: absolute;
  inset: 0;
  left: -4px;
  right: -4px;
  z-index: -1;
}
.split-container:not(.is-mobile) .resizer.resizer-v:hover,
.split-container:not(.is-mobile) .resizer.resizer-v[data-resize-active] {
  background-color: var(--el-color-primary);
}

.split-container:not(.is-mobile) .resizer.resizer-h {
  height: 1px;
  width: 100%;
  cursor: row-resize;
  background-color: var(--border);
}
.split-container:not(.is-mobile) .resizer.resizer-h::after {
  content: '';
  position: absolute;
  inset: 0;
  top: -4px;
  bottom: -4px;
  z-index: -1;
}
.split-container:not(.is-mobile) .resizer.resizer-h:hover,
.split-container:not(.is-mobile) .resizer.resizer-h[data-resize-active] {
  background-color: var(--el-color-primary);
}

.split-container:not(.is-mobile) .resizer .resizer-handle {
  display: none;
  border-radius: 4px;
  background-color: var(--el-color-primary);
  opacity: 0;
  transition: opacity 0.15s ease;
}
.split-container:not(.is-mobile) .resizer.resizer-v .resizer-handle {
  width: 4px;
  height: 48px;
}
.split-container:not(.is-mobile) .resizer.resizer-h .resizer-handle {
  width: 48px;
  height: 4px;
}
.split-container:not(.is-mobile) .resizer:hover .resizer-handle,
.split-container:not(.is-mobile) .resizer[data-resize-active] .resizer-handle {
  display: block;
  opacity: 1;
}

/* Mobile resizers (column layout → row-resize) */
.split-container.is-mobile .resizer.resizer-v,
.split-container.is-mobile .resizer.resizer-h {
  height: 1px;
  width: 100%;
  cursor: row-resize;
  background-color: var(--border);
}
.split-container.is-mobile .resizer::after {
  content: '';
  position: absolute;
  inset: 0;
  top: -4px;
  bottom: -4px;
  z-index: -1;
}
.split-container.is-mobile .resizer:hover {
  background-color: var(--el-color-primary);
}
.split-container.is-mobile .resizer .resizer-handle {
  display: none;
  width: 48px;
  height: 4px;
  border-radius: 4px;
  background-color: var(--el-color-primary);
  opacity: 0;
  transition: opacity 0.15s ease;
}
.split-container.is-mobile .resizer:hover .resizer-handle {
  display: block;
  opacity: 1;
}

/* ═══════ Danmaku Items ═══════ */
.danmaku-item {
  display: flex;
  align-items: baseline;
  gap: 3px;
  padding: 4px 10px;
  font-size: 0.9rem;
  line-height: 2;
  border: none !important;
  border-radius: 0 !important;
  margin: 0 !important;
  min-height: auto !important;
  box-sizing: border-box;
  border-bottom: 1px solid rgba(128, 128, 128, 0.06) !important;
}

.danmaku-item:last-child {
  border-bottom: none !important;
}

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

.dm-meta {
  display: inline-flex;
  align-items: baseline;
  gap: 2px;
  flex-shrink: 0;
  margin-right: 2px;
}

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

/* Info items (interact / room_change) */
.info-item .info-type-label {
  flex-shrink: 0;
  font-size: 0.75rem;
  font-weight: 600;
  padding: 1px 5px;
  border-radius: 3px;
  background-color: rgba(160, 207, 255, 0.15);
  color: #a0cfff;
  margin-right: 3px;
}

/* ═══════ Monetary Items ═══════ */
.monetary-item {
  border-left: 3px solid transparent;
  padding-left: 7px;
  line-height: 2.5;
}

.monetary-item.type-give_gift {
  border-left-color: #E2B52B;
  background-color: rgba(226, 181, 43, 0.06);
}
.monetary-item.type-guard {
  border-left-color: #AB1A32;
  background-color: rgba(171, 26, 50, 0.06);
}
.monetary-item.type-gift_combo {
  border-left-color: #E09443;
  background-color: rgba(224, 148, 67, 0.06);
}

.sc-price {
  font-weight: 700;
  font-size: 0.8rem;
  flex-shrink: 0;
}

.gift-name {
  font-weight: 600;
  color: #E2B52B;
  font-size: 0.8rem;
  flex-shrink: 0;
}

.gift-count {
  color: var(--text-secondary);
  font-size: 0.8rem;
  font-weight: 500;
}

.combo-count {
  color: #E09443;
  font-weight: 700;
}

.gift-price {
  color: var(--text-secondary);
  font-size: 0.75rem;
  opacity: 0.8;
}

.guard-badge-inline {
  display: inline-flex;
  align-items: center;
  padding: 1px 6px;
  border-radius: 4px;
  font-size: 0.75rem;
  font-weight: 700;
  background-color: rgba(171, 26, 50, 0.12);
  color: #AB1A32;
  flex-shrink: 0;
}

.guard-price {
  color: var(--text-secondary);
  font-size: 0.75rem;
  opacity: 0.8;
}

.generic-type {
  display: inline-flex;
  align-items: center;
  padding: 1px 6px;
  border-radius: 4px;
  font-size: 0.75rem;
  font-weight: 600;
  background-color: var(--el-color-primary-light-9);
  color: var(--el-color-primary);
  flex-shrink: 0;
}

/* ═══════ User Menu Dropdown ═══════ */
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

/* ═══════ Wealth Level (荣耀等级) ═══════ */
.wealth-level {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  padding: 0 4px;
  border-radius: 3px;
  font-size: 0.7rem;
  font-weight: 700;
  line-height: 1.4;
  background: linear-gradient(135deg, #e6a23c, #f0c060);
  color: #fff;
  text-shadow: 0 1px 1px rgba(0,0,0,0.2);
  white-space: nowrap;
}
</style>
