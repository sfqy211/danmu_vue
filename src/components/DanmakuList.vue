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
                <div class="dm-info">
                  <img v-if="getWealthLevelUrl(normalList[virtualItem.index].wealthLevel)" class="wealth-level-img" :src="getWealthLevelUrl(normalList[virtualItem.index].wealthLevel)" :alt="'财' + normalList[virtualItem.index].wealthLevel" />
                  <FansMedal :item="normalList[virtualItem.index]" />
                  <span class="dm-user" :class="getGuardClass(normalList[virtualItem.index].guardLevel)" @click="openUserMenu($event, normalList[virtualItem.index])">{{ normalList[virtualItem.index].user }}</span>
                </div>
                <span class="dm-message">{{ normalList[virtualItem.index].content }}</span>
              </div>
            </div>
          </div>
          <div v-if="normalList.length === 0" class="pane-empty">暂无普通弹幕</div>
        </div>

      </div>

      <!-- Vertical Resizer 1 (left / right) -->
      <div class="resizer resizer-v"
        :class="{ 'resizer-hidden': isLeftHidden && isRightHidden }"
        @mousedown="startResize('v1', $event)" @touchstart="startResize('v1', $event)">
        <div class="resizer-handle"></div>
      </div>

      <!-- ─── RIGHT: PAID INTERACTIONS (SC + GIFT + GUARD) ─── -->
      <div class="column-side column-right"
        :style="{ flex: flexRight + ' 1 0px' }"
        :class="{ 'pane-hidden': isRightHidden }">
        <div class="pane-header">
          <span>付费互动</span>
          <div class="filter-toggles">
            <span class="filter-btn" :class="{ active: showSC }" @click="showSC = !showSC">SC</span>
            <span class="filter-btn" :class="{ active: showGift }" @click="showGift = !showGift">礼物</span>
            <span class="filter-btn" :class="{ active: showGuard }" @click="showGuard = !showGuard">航海</span>
          </div>
          <span class="badge">{{ filteredMonetaryList.length }}</span>
        </div>
        <div ref="monetaryScroller" class="scrollable-list" @scroll="onMonetaryScroll">
          <div :style="{ height: monetaryVirtualizer.getTotalSize() + 'px', width: '100%', position: 'relative' }">
            <div v-for="virtualItem in monetaryVirtualizer.getVirtualItems()"
              :key="String(virtualItem.key)"
              :ref="(el: any) => monetaryVirtualizer.measureElement(el as Element)"
              :data-index="virtualItem.index"
              :class="['danmaku-item', 'monetary-item', `type-${getEventType(filteredMonetaryList[virtualItem.index])}`]"
              :style="{
                position: 'absolute', top: virtualItem.start + 'px', left: 0, width: '100%',
                borderLeftColor: (getEventType(filteredMonetaryList[virtualItem.index]) === 'super_chat' ? getSCStyle(filteredMonetaryList[virtualItem.index].price || 0).main : getGiftStyle(filteredMonetaryList[virtualItem.index].price).main),
                backgroundColor: (getEventType(filteredMonetaryList[virtualItem.index]) === 'super_chat' ? getSCStyle(filteredMonetaryList[virtualItem.index].price || 0).bg : getGiftStyle(filteredMonetaryList[virtualItem.index].price).bg),
              }">

              <template v-if="shouldUseSCLayout(filteredMonetaryList[virtualItem.index])">
                <div class="sc-layout">
                  <div class="sc-row sc-user-row">
                    <FansMedal :item="filteredMonetaryList[virtualItem.index]" />
                    <span class="dm-user" :class="getGuardClass(filteredMonetaryList[virtualItem.index].guardLevel)" @click="openUserMenu($event, filteredMonetaryList[virtualItem.index])">{{ filteredMonetaryList[virtualItem.index].user }}</span>
                  </div>
                  <div class="sc-row sc-meta-row">
                    <span class="sc-price" :style="{ color: (getEventType(filteredMonetaryList[virtualItem.index]) === 'super_chat' ? getSCStyle(filteredMonetaryList[virtualItem.index].price || 0).main : getGiftStyle(filteredMonetaryList[virtualItem.index].price).main) }">
                      <template v-if="getEventType(filteredMonetaryList[virtualItem.index]) === 'super_chat'">
                        <span>¥{{ formatPrice(filteredMonetaryList[virtualItem.index].price || 0) }}</span>
                      </template>
                      <template v-else-if="getEventType(filteredMonetaryList[virtualItem.index]) === 'guard'">
                        <span class="gift-name">{{ getGuardName(filteredMonetaryList[virtualItem.index].guardLevel) }}</span>
                        <span class="gift-count">x1</span>
                        <span v-if="filteredMonetaryList[virtualItem.index].price" class="gift-price">¥{{ formatPrice(filteredMonetaryList[virtualItem.index].price || 0) }}</span>
                      </template>
                      <template v-else>
                        <span class="gift-name">{{ filteredMonetaryList[virtualItem.index].name }}</span>
                        <span class="gift-count" :class="{ 'combo-count': getEventType(filteredMonetaryList[virtualItem.index]) === 'gift_combo' }">x{{ filteredMonetaryList[virtualItem.index].count || 1 }}</span>
                        <span v-if="filteredMonetaryList[virtualItem.index].price" class="gift-price">¥{{ formatPrice(filteredMonetaryList[virtualItem.index].price || 0) }}</span>
                      </template>
                    </span>
                    <span class="sc-time">{{ store.timeDisplayMode === 'absolute' ? formatAbsoluteTime(filteredMonetaryList[virtualItem.index].timestamp) : filteredMonetaryList[virtualItem.index].timeStr }}</span>
                  </div>
                  <div v-if="getEventType(filteredMonetaryList[virtualItem.index]) === 'super_chat'" class="sc-row sc-content-row">
                    {{ filteredMonetaryList[virtualItem.index].content }}
                    <span v-if="filteredMonetaryList[virtualItem.index].contentJpn" class="dm-jpn">{{ filteredMonetaryList[virtualItem.index].contentJpn }}</span>
                  </div>
                </div>
              </template>
              <template v-else>
                <span class="dm-time">{{ store.timeDisplayMode === 'absolute' ? formatAbsoluteTime(filteredMonetaryList[virtualItem.index].timestamp) : filteredMonetaryList[virtualItem.index].timeStr }}</span>
                <div class="dm-info">
                  <img v-if="getWealthLevelUrl(filteredMonetaryList[virtualItem.index].wealthLevel)" class="wealth-level-img" :src="getWealthLevelUrl(filteredMonetaryList[virtualItem.index].wealthLevel)" :alt="'财' + filteredMonetaryList[virtualItem.index].wealthLevel" />
                  <FansMedal :item="filteredMonetaryList[virtualItem.index]" />
                  <span class="dm-user" :class="getGuardClass(filteredMonetaryList[virtualItem.index].guardLevel)" @click="openUserMenu($event, filteredMonetaryList[virtualItem.index])">{{ filteredMonetaryList[virtualItem.index].user }}</span>
                  <span class="dm-meta">
                    <span class="gift-name">{{ filteredMonetaryList[virtualItem.index].name }}</span>
                    <span class="gift-count" :class="{ 'combo-count': getEventType(filteredMonetaryList[virtualItem.index]) === 'gift_combo' }">x{{ filteredMonetaryList[virtualItem.index].count || 1 }}</span>
                    <span v-if="filteredMonetaryList[virtualItem.index].price" class="gift-price">¥{{ formatPrice(filteredMonetaryList[virtualItem.index].price || 0) }}</span>
                  </span>
                </div>
              </template>
            </div>
          </div>
        </div>
        <div v-if="filteredMonetaryList.length === 0" class="pane-empty">暂无付费互动</div>
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

import { getWealthLevelUrl } from '../constants/wealthLevel';
import { getGuardIconUrl } from '../constants/guardIcon';

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

const getGuardClass = (level?: number) => {
  switch (level) {
    case 1: return 'guard-governor';
    case 2: return 'guard-admiral';
    case 3: return 'guard-captain';
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

const formatPrice = (price: number) => Number(price.toFixed(2)).toString();

const getGiftStyle = (price?: number) => {
  if (price && price >= 30) return getSCStyle(price);
  return { main: 'transparent', bg: 'transparent' };
};

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

/** Group consecutive gifts with same user + name (give_gift & gift_combo treated as same) */
const groupedGiftList = computed(() => {
  const result: Danmaku[] = [];
  for (const d of giftList.value) {
    const prev = result[result.length - 1];
    const t = getEventType(d);
    const prevType = prev ? getEventType(prev) : '';
    if (prev && t !== 'guard' && prevType !== 'guard'
        && prev.user === d.user && prev.name === d.name) {
      const merged = { ...prev };
      const prevCount = merged.count || 1;
      const currCount = d.count || 1;
      merged.count = prevCount + currCount;
      merged.type = 'give_gift'; // normalize to give_gift for unified rendering
      // Infer unit price
      const prevUnitPrice = merged.price
        ? (merged.isPriceTotal ? merged.price / prevCount : merged.price)
        : 0;
      const currUnitPrice = d.price
        ? (d.isPriceTotal ? d.price / currCount : d.price)
        : 0;
      const unitPrice = currUnitPrice > 0 ? currUnitPrice : prevUnitPrice;
      merged.price = unitPrice * merged.count;
      merged.isPriceTotal = true;
      result[result.length - 1] = merged;
    } else {
      result.push({ ...d });
    }
  }
  return result;
});

// ==================== Monetary Filters ====================

const showSC = ref(true);
const showGift = ref(true);
const showGuard = ref(true);

const combinedMonetaryList = computed(() => {
  const combined = [...scList.value, ...groupedGiftList.value];
  return combined.sort((a, b) => a.timestamp - b.timestamp);
});

const filteredMonetaryList = computed(() => {
  return combinedMonetaryList.value.filter(d => {
    const t = getEventType(d);
    if (t === 'super_chat') return showSC.value;
    if (t === 'guard') return showGuard.value;
    if (t === 'give_gift' || t === 'gift_combo') return showGift.value;
    return true;
  });
});

const shouldUseSCLayout = (item: Danmaku) => {
  const t = getEventType(item);
  if (t === 'guard' || t === 'super_chat') return true;
  return (item.price || 0) >= 30;
};

// ==================== Virtual Scrolling ====================

const ROW_HEIGHT = 32;

const normalScroller = ref<HTMLElement | null>(null);
const monetaryScroller = ref<HTMLElement | null>(null);

const normalVirtualizer = useVirtualizer(computed(() => ({
  count: normalList.value.length,
  getScrollElement: () => normalScroller.value,
  estimateSize: () => ROW_HEIGHT,
  overscan: 10,
  measureElement,
})));

const monetaryVirtualizer = useVirtualizer(computed(() => ({
  count: filteredMonetaryList.value.length,
  getScrollElement: () => monetaryScroller.value,
  estimateSize: () => ROW_HEIGHT,
  overscan: 10,
  measureElement,
})));

// ==================== Scroll / Load More ====================

const onNormalScroll = (e: Event) => {
  const t = e.target as HTMLElement;
  if (t.scrollTop + t.clientHeight >= t.scrollHeight - 50) store.loadMore();
};
const onMonetaryScroll = (e: Event) => {
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

// Vertical panes (two-column: left / right)
const flexLeft = ref(5);
const flexRight = ref(5);

const flexTop = ref(6);

// Computed hidden states
const isLeftHidden = computed(() => flexLeft.value < MIN_FLEX);
const isRightHidden = computed(() => flexRight.value < MIN_FLEX);
const isTopHidden = computed(() => flexTop.value < MIN_FLEX);

// On mobile, hide right (gifts) by default
onMounted(() => {
  updateMobileStatus();
  if (isMobile.value) {
    flexRight.value = 0;
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
    v1: 'col-resize', v2: 'col-resize',
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

  // On mobile (column layout) use Y axis. On desktop for v1/v2 use X axis.
  const useYAxis = isMobile.value;

  let relPos: number;
  let containerSize: number;

  if (useYAxis) {
    // For mobile v1/v2, compute relative to the main container.
    const parentEl = container;
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
    // Between left and right
    const totalFlex = flexLeft.value + flexRight.value;
    let newLeft = fraction * totalFlex;
    newLeft = Math.max(0, Math.min(totalFlex, newLeft));

    if (newLeft < MIN_FLEX) {
      flexLeft.value = 0;
      flexRight.value = totalFlex;
    } else if (totalFlex - newLeft < MIN_FLEX) {
      flexRight.value = 0;
      flexLeft.value = totalFlex;
    } else {
      flexLeft.value = newLeft;
      flexRight.value = totalFlex - newLeft;
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
  font-size: 1.0rem;
}

.badge {
  background-color: var(--el-color-primary-light-9);
  padding: 1px 7px;
  border-radius: 10px;
  font-size: 0.85rem;
  color: var(--el-color-primary);
  font-weight: 500;
}

/* ═══════ Filter Toggles ═══════ */
.filter-toggles {
  display: inline-flex;
  gap: 4px;
  align-items: center;
  margin: 0 8px;
}

.filter-btn {
  font-size: 0.8rem;
  padding: 2px 8px;
  border-radius: 4px;
  cursor: pointer;
  color: var(--text-tertiary);
  background: var(--bg-secondary);
  border: 1px solid var(--border);
  user-select: none;
  transition: all 0.15s ease;
}

.filter-btn.active {
  color: #fff;
  background: var(--accent);
  border-color: var(--accent);
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
  font-size: 0.95rem;
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
.split-container:not(.is-mobile) .resizer:hover .resizer-handle,
.split-container:not(.is-mobile) .resizer[data-resize-active] .resizer-handle {
  display: block;
  opacity: 1;
}

/* Mobile resizers (column layout → row-resize) */
.split-container.is-mobile .resizer.resizer-v {
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
  display: block;
  padding: 3px 10px;
  font-size: 1.0rem;
  line-height: 1.5;
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
  display: inline-block;
  width: 42px;
  margin-right: 4px;
  font-size: 0.8rem;
  line-height: 1.5;
  color: var(--text-tertiary);
  opacity: 0.45;
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
  overflow: hidden;
  vertical-align: middle;
}

.dm-info {
  display: inline-flex;
  align-items: center;
  gap: 3px;
  vertical-align: middle;
}

.dm-meta {
  display: inline-flex;
  align-items: center;
  gap: 2px;
}

.dm-user {
  display: inline-block;
  max-width: 120px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-weight: 600;
  color: var(--text-primary);
  font-size: 0.95rem;
  cursor: pointer;
  line-height: 1.5;
  vertical-align: middle;
}

.dm-user:hover {
  text-decoration: underline;
}

.dm-user.guard-captain {
  color: var(--guard-captain);
}

.dm-user.guard-admiral {
  color: var(--guard-admiral);
}

.dm-user.guard-governor {
  color: var(--guard-governor);
}

.dm-message {
  display: inline;
  color: var(--text-primary);
  white-space: normal;
  word-break: break-all;
  line-height: 1.5;
  vertical-align: middle;
}

.dm-jpn {
  color: var(--text-secondary);
  font-size: 0.9rem;
  margin-left: 4px;
  opacity: 0.75;
}

/* ═══════ Monetary Items ═══════ */
.monetary-item {
  border-left: 3px solid transparent;
  padding-left: 7px;
  line-height: 1.5;
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
  font-size: 0.95rem;
  flex-shrink: 0;
}

/* ═══════ SC Layout ═══════ */
.monetary-item.type-super_chat {
  padding-top: 5px;
  padding-bottom: 5px;
}

.sc-layout {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.sc-row {
  display: flex;
  align-items: center;
  gap: 4px;
}

.sc-meta-row {
  justify-content: space-between;
}

.sc-time {
  font-size: 0.8rem;
  color: var(--text-tertiary);
  opacity: 0.55;
  white-space: nowrap;
}

.sc-content-row {
  line-height: 1.5;
  word-break: break-all;
  color: var(--text-primary);
}

.gift-name {
  font-weight: 600;
  color: var(--gift-name);
  font-size: 0.9rem;
  flex-shrink: 0;
}

.gift-count {
  color: var(--text-secondary);
  font-size: 0.9rem;
  font-weight: 500;
}

.combo-count {
  color: #E09443;
  font-weight: 700;
}

.gift-price {
  color: var(--text-secondary);
  font-size: 0.85rem;
  opacity: 0.8;
}

.guard-badge-inline {
  display: inline-flex;
  align-items: center;
  padding: 1px 6px;
  border-radius: 4px;
  font-size: 0.85rem;
  font-weight: 700;
  background-color: rgba(171, 26, 50, 0.12);
  color: #AB1A32;
  flex-shrink: 0;
}

.guard-price {
  color: var(--text-secondary);
  font-size: 0.85rem;
  opacity: 0.8;
}

.generic-type {
  display: inline-flex;
  align-items: center;
  padding: 1px 6px;
  border-radius: 4px;
  font-size: 0.85rem;
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
.wealth-level-img {
  display: inline-block;
  flex-shrink: 0;
  height: 18px;
  width: auto;
  object-fit: contain;
  vertical-align: middle;
}

</style>
