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
        <p v-if="summaryTotalPrice !== null" class="summary-price">营收总额 <span class="price-value">¥{{ formatPrice(summaryTotalPrice) }}</span></p>
        <el-button type="primary" size="large" :loading="danmakuLoading" @click="store.fetchDanmaku">
          查询弹幕列表
        </el-button>
      </div>
    </div>

    <!-- ═══════════════ FOUR-ZONE SPLIT LAYOUT ═══════════════ -->
    <div v-else ref="splitContainer" class="split-container" :class="{ 'is-mobile': isMobile, 'is-resizing': isResizing }">

      <!-- ─── LEFT SECTION (flex column: top / bottom) ─── -->
      <div class="column-side column-left-section"
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
            <div v-for="virtualItem in normalVirtualItems"
              :key="String(virtualItem.key)"
              :ref="(el: any) => normalVirtualizer.measureElement(el as Element)"
              :data-index="virtualItem.index"
              :style="{ position: 'absolute', top: virtualItem.start + 'px', left: 0, width: '100%' }"
              class="danmaku-item">
              <span v-if="store.timeDisplayMode !== 'hidden'" class="dm-time">{{ store.timeDisplayMode === 'absolute' ? formatAbsoluteTime(virtualItem.item.timestamp) : virtualItem.item.timeStr }}</span>
              <div class="dm-info">
                <img v-if="virtualItem.item.face && !isAvatarFailed(virtualItem.item.face)" class="dm-avatar" :src="getAvatarUrl(virtualItem.item.face)" referrerpolicy="no-referrer" @error="onAvatarError(virtualItem.item.face)" />
                <span v-else class="dm-avatar-placeholder" :style="{ backgroundColor: getAvatarColor(virtualItem.item.user) }">{{ virtualItem.item.user.charAt(0) }}</span>
                <img v-if="getWealthLevelUrl(virtualItem.item.wealthLevel)" class="wealth-level-img" :src="getWealthLevelUrl(virtualItem.item.wealthLevel)" :alt="'财' + virtualItem.item.wealthLevel" />
                <FansMedal :item="virtualItem.item" />
                <span class="dm-user" :class="getGuardClass(virtualItem.item.guardLevel)" @click="openUserMenu($event, virtualItem.item)">{{ virtualItem.item.user }}</span>
              </div>
              <span class="dm-message">{{ virtualItem.item.content }}</span>
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
          <div class="header-stats">
            <span class="badge">{{ filteredMonetaryList.length }}</span>
            <span class="total-price">¥{{ formatPrice(monetaryTotal) }}</span>
          </div>
        </div>
        <div ref="monetaryScroller" class="scrollable-list" @scroll="onMonetaryScroll">
          <div :style="{ height: monetaryVirtualizer.getTotalSize() + 'px', width: '100%', position: 'relative' }">
            <div v-for="virtualItem in monetaryVirtualItems"
              :key="String(virtualItem.key)"
              :ref="(el: any) => monetaryVirtualizer.measureElement(el as Element)"
              :data-index="virtualItem.index"
              :class="['danmaku-item', 'monetary-item', `type-${getEventType(virtualItem.item)}`]"
              :style="{
                position: 'absolute', top: virtualItem.start + 'px', left: 0, width: '100%',
              }">

              <!-- ═══ SC: Two-segment card ═══ -->
              <template v-if="getEventType(virtualItem.item) === 'super_chat'">
                <div class="sc-card"
                  :style="{
                    borderColor: getSCStyle(virtualItem.item.price || 0).borderColor,
                  }">
                  <div class="sc-card-header"
                    :style="{
                      backgroundColor: getSCStyle(virtualItem.item.price || 0).lightBg,
                      borderColor: getSCStyle(virtualItem.item.price || 0).borderColor,
                    }">
                    <div class="sc-avatar-wrap">
                      <img v-if="virtualItem.item.face && !isAvatarFailed(virtualItem.item.face)" class="sc-avatar-img" :src="getAvatarUrl(virtualItem.item.face)" referrerpolicy="no-referrer" @error="onAvatarError(virtualItem.item.face)" />
                      <div v-else class="sc-avatar" :style="{ backgroundColor: getAvatarColor(virtualItem.item.user) }">
                        {{ virtualItem.item.user.charAt(0) }}
                      </div>
                    </div>
                    <div class="sc-header-info">
                      <div class="sc-header-top">
                        <FansMedal :item="virtualItem.item" />
                        <span class="sc-username" @click="openUserMenu($event, virtualItem.item)">{{ virtualItem.item.user }}</span>
                      </div>
                      <div class="sc-header-bottom">
                        <span class="sc-price" :style="{ color: getSCStyle(virtualItem.item.price || 0).priceColor }">
                          ¥{{ formatPrice(virtualItem.item.price || 0) }}
                        </span>
                        <span v-if="store.timeDisplayMode !== 'hidden'" class="sc-time">{{ store.timeDisplayMode === 'absolute' ? formatAbsoluteTime(virtualItem.item.timestamp) : virtualItem.item.timeStr }}</span>
                      </div>
                    </div>
                  </div>
                  <div class="sc-card-body"
                    :style="{
                      backgroundColor: getSCStyle(virtualItem.item.price || 0).darkBg,
                      color: getSCStyle(virtualItem.item.price || 0).msgColor,
                    }">
                    {{ virtualItem.item.content }}
                    <span v-if="virtualItem.item.contentJpn" class="sc-jpn">{{ virtualItem.item.contentJpn }}</span>
                  </div>
                </div>
              </template>

              <!-- ═══ Guard: Toast-style card ═══ -->
              <template v-else-if="getEventType(virtualItem.item) === 'guard'">
                <div class="guard-card"
                  :style="{
                    backgroundColor: getSCStyle(virtualItem.item.price || 0).darkBg,
                  }">
                  <div class="guard-avatar-wrap">
                    <img v-if="virtualItem.item.face && !isAvatarFailed(virtualItem.item.face)" class="guard-avatar-img" :src="getAvatarUrl(virtualItem.item.face)" referrerpolicy="no-referrer" @error="onAvatarError(virtualItem.item.face)" />
                    <div v-else class="guard-avatar" :style="{ backgroundColor: getAvatarColor(virtualItem.item.user) }">
                      {{ virtualItem.item.user.charAt(0) }}
                    </div>
                  </div>
                  <div class="guard-content">
                    <div class="guard-top-row">
                      <span class="guard-username" @click="openUserMenu($event, virtualItem.item)">{{ virtualItem.item.user }}</span>
                    </div>
                    <div class="guard-price-row">
                      <span class="guard-price-currency">CN¥</span>
                      <span class="guard-price-figure">{{ formatPrice(virtualItem.item.price || 0) }}</span>
                    </div>
                    <div class="guard-message">
                      <span class="guard-action">{{ getGuardName(virtualItem.item.guardLevel) }}</span>
                      <span v-if="store.timeDisplayMode !== 'hidden'" class="guard-time">{{ store.timeDisplayMode === 'absolute' ? formatAbsoluteTime(virtualItem.item.timestamp) : virtualItem.item.timeStr }}</span>
                    </div>
                  </div>
                  <img v-if="getGuardIconUrl(virtualItem.item.guardLevel)" class="guard-icon" :src="getGuardIconUrl(virtualItem.item.guardLevel)" :alt="getGuardName(virtualItem.item.guardLevel)" />
                </div>
              </template>

              <!-- ═══ Gift: compact inline ═══ -->
              <template v-else>
                <span v-if="store.timeDisplayMode !== 'hidden'" class="dm-time">{{ store.timeDisplayMode === 'absolute' ? formatAbsoluteTime(virtualItem.item.timestamp) : virtualItem.item.timeStr }}</span>
                <div class="dm-info">
                <img v-if="virtualItem.item.face && !isAvatarFailed(virtualItem.item.face)" class="dm-avatar" :src="getAvatarUrl(virtualItem.item.face)" referrerpolicy="no-referrer" @error="onAvatarError(virtualItem.item.face)" />
                  <span v-else class="dm-avatar-placeholder" :style="{ backgroundColor: getAvatarColor(virtualItem.item.user) }">{{ virtualItem.item.user.charAt(0) }}</span>
                  <img v-if="getWealthLevelUrl(virtualItem.item.wealthLevel)" class="wealth-level-img" :src="getWealthLevelUrl(virtualItem.item.wealthLevel)" :alt="'财' + virtualItem.item.wealthLevel" />
                  <FansMedal :item="virtualItem.item" />
                  <span class="dm-user" :class="getGuardClass(virtualItem.item.guardLevel)" @click="openUserMenu($event, virtualItem.item)">{{ virtualItem.item.user }}</span>
                  <span class="dm-meta">
                    <span class="gift-name">{{ virtualItem.item.name }}</span>
                    <span class="gift-count" :class="{ 'combo-count': getEventType(virtualItem.item) === 'gift_combo' }">x{{ virtualItem.item.count || 1 }}</span>
                    <span v-if="virtualItem.item.price" class="gift-price">¥{{ formatPrice(unitPrice(virtualItem.item)) }}</span>
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
          <el-dropdown-item command="hide"><span class="menu-icon">🚫</span><span>不看此用户弹幕</span></el-dropdown-item>
          <el-dropdown-item command="profile"><span class="menu-icon">👤</span><span>打开用户主页</span></el-dropdown-item>
          <el-dropdown-item command="laplace"><span class="menu-icon">🧪</span><span>查成分 (Laplace)</span></el-dropdown-item>
        </el-dropdown-menu>
      </template>
    </el-dropdown>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted, nextTick } from 'vue';
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
  sessionSummary,
  searchText
} = storeToRefs(store);

// ==================== Debounced Search ====================
const debouncedSearchText = ref('');
let searchDebounceTimer: ReturnType<typeof setTimeout> | null = null;

watch(searchText, (val) => {
  if (searchDebounceTimer) clearTimeout(searchDebounceTimer);
  searchDebounceTimer = setTimeout(() => {
    debouncedSearchText.value = val;
  }, 300);
});

// Clear failed avatar URLs when session changes to prevent unbounded growth
watch(currentSession, () => {
  failedAvatarUrls.value = new Set();
  avatarUrlCache.clear();
});

// ==================== Helpers ====================

const summaryTotalPrice = computed(() => {
  const gs = sessionSummary.value?.giftSummary;
  if (!gs) return null;
  return gs.totalPrice ?? gs.TotalPrice ?? null;
});

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

interface SCStyleResult {
  main: string;
  bg: string;
  lightBg: string;
  darkBg: string;
  priceColor: string;
  msgColor: string;
  borderColor: string;
}

const getSCStyle = (price: number): SCStyleResult => {
  const level = getSCLevel(price);
  const styles: Record<number, SCStyleResult> = {
    1: { main: '#2A60B2', bg: 'rgba(42, 96, 178, 0.08)', lightBg: '#EDF5FF', darkBg: '#2A60B2', priceColor: '#7497CD', msgColor: '#FFFFFF', borderColor: '#2A60B2' },
    2: { main: '#427D9E', bg: 'rgba(66, 125, 158, 0.08)', lightBg: '#EBF4F8', darkBg: '#427D9E', priceColor: '#6DAABB', msgColor: '#FFFFFF', borderColor: '#427D9E' },
    3: { main: '#E2B52B', bg: 'rgba(226, 181, 43, 0.08)', lightBg: '#FFF8E1', darkBg: '#E2B52B', priceColor: '#C9A020', msgColor: '#FFFFFF', borderColor: '#E2B52B' },
    4: { main: '#E09443', bg: 'rgba(224, 148, 67, 0.08)', lightBg: '#FFF3E0', darkBg: '#E09443', priceColor: '#D08030', msgColor: '#FFFFFF', borderColor: '#E09443' },
    5: { main: '#E54D4D', bg: 'rgba(229, 77, 77, 0.08)', lightBg: '#FFEBEE', darkBg: '#E54D4D', priceColor: '#D43030', msgColor: '#FFFFFF', borderColor: '#E54D4D' },
    6: { main: '#AB1A32', bg: 'rgba(171, 26, 50, 0.08)', lightBg: '#FCE4EC', darkBg: '#AB1A32', priceColor: '#AB1A32', msgColor: '#FFFFFF', borderColor: '#AB1A32' }
  };
  return styles[level] || styles[1];
};

/** Generate a placeholder avatar color from username for visual consistency */
const getAvatarColor = (name: string): string => {
  let hash = 0;
  for (let i = 0; i < name.length; i++) {
    hash = name.charCodeAt(i) + ((hash << 5) - hash);
  }
  const hue = Math.abs(hash) % 360;
  return `hsl(${hue}, 45%, 55%)`;
};

const formatPrice = (price: number) => Number(price.toFixed(2)).toString();

const normalizeAvatarUrl = (face?: string) => {
  if (!face) return '';
  if (face.startsWith('//')) return `https:${face}`;
  if (face.startsWith('http://')) return face.replace(/^http:\/\//i, 'https://');
  return face;
};

/** Cached normalization to avoid triple calls in template (v-if, :src, @error) */
const avatarUrlCache = new Map<string, string>();
const getAvatarUrl = (face?: string): string => {
  if (!face) return '';
  const cached = avatarUrlCache.get(face);
  if (cached !== undefined) return cached;
  const normalized = normalizeAvatarUrl(face);
  avatarUrlCache.set(face, normalized);
  return normalized;
};

/** Track avatar URL failures so repeated rows do not re-request the same CDN image */
const failedAvatarUrls = ref(new Set<string>());
const onAvatarError = (face?: string) => {
  const normalized = getAvatarUrl(face);
  if (normalized) failedAvatarUrls.value.add(normalized);
};

/** Check if avatar has failed (uses cache) */
const isAvatarFailed = (face?: string): boolean => {
  const normalized = getAvatarUrl(face);
  return normalized ? failedAvatarUrls.value.has(normalized) : false;
};

// ==================== Hidden Users (Block List) ====================

const applyHiddenUsers = (list: Danmaku[]) => {
  if (store.hiddenUsers.size === 0) return list;
  return list.filter(d => !store.hiddenUsers.has(d.uid));
};

// ==================== Computed filtered lists ====================

const applySearch = (list: Danmaku[], searchFields: ('content' | 'user' | 'name')[]) => {
  if (!debouncedSearchText.value) return list;
  const lower = debouncedSearchText.value.toLowerCase();
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
  list = applyHiddenUsers(list);
  return applySearch(list, ['content', 'user']);
});

/** Left bottom: INTERACT_WORD, ROOM_CHANGE, enter, follow, share */
/** Middle: SUPER_CHAT_MESSAGE / JPN → type === 'super_chat' */
const scList = computed(() => {
  let list = danmakuList.value.filter(d => getEventType(d) === 'super_chat');
  list = applyHiddenUsers(list);
  return applySearch(list, ['content', 'user']);
});

/** Right: SEND_GIFT, GUARD_BUY, COMBO_SEND */
const giftList = computed(() => {
  let list = danmakuList.value.filter(d => {
    const t = getEventType(d);
    return t === 'give_gift' || t === 'guard' || t === 'gift_combo';
  });
  list = applyHiddenUsers(list);
  return applySearch(list, ['content', 'user', 'name']);
});

/** Compute the actual total price for a single gift record */
const actualTotalPrice = (d: Danmaku): number => {
  if (!d.price) return 0;
  if (d.isPriceTotal) return d.price;
  return d.price * (d.count || 1);
};

/** Compute the unit price for display (original per-unit price, not accumulated total) */
const unitPrice = (d: Danmaku): number => {
  if (!d.price) return 0;
  if (d.isPriceTotal) return d.price / (d.count || 1);
  return d.price;
};

/** Group consecutive gifts with same user + name (give_gift & gift_combo treated as same) */
const groupedGiftList = computed(() => {
  const result: Danmaku[] = [];
  for (const d of giftList.value) {
    const prev = result[result.length - 1];
    const t = getEventType(d);
    const prevType = prev ? getEventType(prev) : '';
    if (prev && t !== 'guard' && prevType !== 'guard'
        && prev.user === d.user && prev.name === d.name) {
      const prevTotal = actualTotalPrice(prev);
      const currTotal = actualTotalPrice(d);
      const prevCount = prev.count || 1;
      const currCount = d.count || 1;
      const totalCount = prevCount + currCount;
      const total = prevTotal + currTotal;
      const merged = { ...prev };
      merged.count = totalCount;
      merged.type = 'give_gift'; // normalize to give_gift for unified rendering
      merged.price = total;
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

const monetaryTotal = computed(() => {
  return filteredMonetaryList.value.reduce((sum, d) => sum + actualTotalPrice(d), 0);
});

// ==================== Virtual Scrolling ====================

const NORMAL_ROW_HEIGHT = 32;
const SC_CARD_HEIGHT = 90;
const GUARD_CARD_HEIGHT = 80;

const normalScroller = ref<HTMLElement | null>(null);
const monetaryScroller = ref<HTMLElement | null>(null);

const normalVirtualizer = useVirtualizer(computed(() => ({
  count: normalList.value.length,
  getScrollElement: () => normalScroller.value,
  estimateSize: () => NORMAL_ROW_HEIGHT,
  overscan: 10,
  measureElement,
})));

const monetaryVirtualizer = useVirtualizer(computed(() => ({
  count: filteredMonetaryList.value.length,
  getScrollElement: () => monetaryScroller.value,
  estimateSize: (index: number) => {
    const item = filteredMonetaryList.value[index];
    if (!item) return NORMAL_ROW_HEIGHT;
    const t = getEventType(item);
    if (t === 'super_chat') return SC_CARD_HEIGHT;
    if (t === 'guard') return GUARD_CARD_HEIGHT;
    return NORMAL_ROW_HEIGHT;
  },
  overscan: 10,
  measureElement,
})));

const normalVirtualItems = computed(() => {
  const items = normalVirtualizer.value.getVirtualItems();
  const list = normalList.value;
  return items.map((vi: any) => ({ ...vi, item: list[vi.index] }));
});

const monetaryVirtualItems = computed(() => {
  const items = monetaryVirtualizer.value.getVirtualItems();
  const list = filteredMonetaryList.value;
  return items.map((vi: any) => ({ ...vi, item: list[vi.index] }));
});

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
    case 'hide': store.toggleHideUser(uid, user); break;
    case 'profile': if (uid) window.open(`https://space.bilibili.com/${uid}`, '_blank'); break;
    case 'laplace': if (uid) window.open(`https://laplace.live/user/${uid}`, '_blank'); break;
  }
};

// ==================== Resizer State ====================

const splitContainer = ref<HTMLElement | null>(null);


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
  window.addEventListener('resize', updateMobileStatus);
  window.addEventListener('mouseup', stopResize);
  window.addEventListener('touchend', stopResize);
  window.addEventListener('touchcancel', stopResize);
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
    v1: 'col-resize',
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

onUnmounted(() => {
  window.removeEventListener('resize', updateMobileStatus);
  window.removeEventListener('mouseup', stopResize);
  window.removeEventListener('touchend', stopResize);
  window.removeEventListener('touchcancel', stopResize);
  if (rafId) cancelAnimationFrame(rafId);
  if (searchDebounceTimer) clearTimeout(searchDebounceTimer);
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
.load-card p { margin: 0 0 8px; color: var(--text-secondary); font-size: 0.9rem; }
.load-card .summary-price { margin-bottom: 24px; }
.load-card .price-value { font-weight: 700; color: #E2B52B; font-size: 1.1rem; }

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

.header-stats {
  display: inline-flex;
  align-items: center;
  gap: 8px;
}

.total-price {
  font-weight: 700;
  font-size: 0.9rem;
  color: #E2B52B;
  white-space: nowrap;
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

/* ─── Avatar (normal danmaku & gift) ─── */
.dm-avatar {
  width: 20px;
  height: 20px;
  min-width: 20px;
  border-radius: 50%;
  object-fit: cover;
  flex-shrink: 0;
  vertical-align: middle;
}

.dm-avatar-placeholder {
  width: 20px;
  height: 20px;
  min-width: 20px;
  border-radius: 50%;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  color: #fff;
  font-weight: 700;
  font-size: 0.65rem;
  flex-shrink: 0;
  vertical-align: middle;
  line-height: 1;
}

.dm-time {
  display: inline-block;
  margin-right: 4px;
  font-size: 0.9rem;
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
  font-size: 0.9rem;
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
  margin-left: 6px;
}

.dm-jpn {
  color: var(--text-secondary);
  font-size: 0.9rem;
  margin-left: 4px;
  opacity: 0.75;
}

/* ═══════ Monetary Items ═══════ */
.monetary-item {
  padding: 4px 8px;
  line-height: 1.5;
}

/* ═══════ SC Two-Segment Card ═══════ */
.sc-card {
  border-radius: 8px;
  overflow: hidden;
  border: 1.5px solid transparent;
  margin: 2px 0;
}

.sc-card-header {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px;
  border-bottom: 1.5px solid transparent;
}

.sc-card-body {
  padding: 8px 10px;
  line-height: 1.6;
  word-break: break-all;
  font-size: 0.9rem;
}

/* Avatar placeholder (shared between SC & Guard) */
.sc-avatar-wrap {
  width: 36px;
  height: 36px;
  min-width: 36px;
  border-radius: 50%;
  overflow: hidden;
  flex-shrink: 0;
}

.sc-avatar-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  border-radius: 50%;
}

.sc-avatar {
  width: 100%;
  height: 100%;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  color: #fff;
  font-weight: 700;
  font-size: 0.9rem;
}

.sc-header-info {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
  flex: 1;
}

.sc-header-top {
  display: flex;
  align-items: center;
  gap: 4px;
  min-width: 0;
}

.sc-header-bottom {
  display: flex;
  align-items: center;
  gap: 8px;
}

.sc-username {
  font-weight: 600;
  font-size: 0.9rem;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  cursor: pointer;
  color: var(--text-primary);
}

.sc-username:hover {
  text-decoration: underline;
}

.sc-price {
  font-weight: 700;
  font-size: 0.9rem;
  flex-shrink: 0;
}

.sc-time {
  font-size: 0.9rem;
  opacity: 0.55;
  white-space: nowrap;
}

.sc-jpn {
  opacity: 0.7;
  font-size: 0.9rem;
  margin-left: 4px;
}

/* ═══════ Guard Toast-Style Card ═══════ */
.guard-card {
  border-radius: 8px;
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 12px;
  margin: 2px 0;
  color: rgba(0, 0, 0, 0.85);
}

html.dark-mode .guard-card,
html.dark .guard-card {
  color: #fff;
}

.guard-avatar-wrap {
  width: 40px;
  height: 40px;
  min-width: 40px;
  border-radius: 50%;
  overflow: hidden;
  flex-shrink: 0;
  border: 2px solid rgba(255, 255, 255, 0.3);
}

html.dark-mode .guard-avatar-wrap,
html.dark .guard-avatar-wrap {
  border-color: rgba(255, 255, 255, 0.3);
}

html:not(.dark-mode):not(.dark) .guard-avatar-wrap {
  border-color: rgba(0, 0, 0, 0.15);
}

.guard-avatar-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  border-radius: 50%;
}

.guard-avatar {
  width: 100%;
  height: 100%;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  color: #fff;
  font-weight: 700;
  font-size: 0.9rem;
}

.guard-content {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
  flex: 1;
}

.guard-top-row {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}

.guard-username {
  font-weight: 600;
  font-size: 0.9rem;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  cursor: pointer;
  color: var(--text-primary);
}

.guard-username:hover {
  text-decoration: underline;
  opacity: 0.9;
}

.guard-price-row {
  display: flex;
  align-items: baseline;
  gap: 1px;
}

.guard-price-currency {
  font-size: 0.9rem;
  font-weight: 500;
  opacity: 0.8;
}

.guard-price-figure {
  font-size: 1.1rem;
  font-weight: 800;
  letter-spacing: -0.02em;
}

.guard-message {
  font-size: 0.9rem;
  opacity: 0.8;
  line-height: 1.4;
  display: flex;
  align-items: center;
  gap: 6px;
}

.guard-action {
  font-weight: 600;
}

.guard-time {
  font-size: 0.9rem;
  opacity: 0.5;
  white-space: nowrap;
}

.guard-icon {
  height: 60px;
  min-height: 60px;
  width: auto;
  flex-shrink: 0;
  object-fit: contain;
  align-self: center;
  opacity: 0.9;
  filter: drop-shadow(0 1px 2px rgba(0, 0, 0, 0.2));
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
  font-size: 0.9rem;
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
