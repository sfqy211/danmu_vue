import { defineStore } from 'pinia';
import { ref, computed, shallowRef } from 'vue';
import { getSessionDanmaku, getSessionSummary, getVups, type Danmaku, type SessionInfo, type VupInfo } from '../api/danmaku';

export const useDanmakuStore = defineStore('danmaku', () => {
  const SELECTED_VUP_STORAGE_KEY = 'selectedStreamerUid';
  let vupLoadPromise: Promise<VupInfo[]> | null = null;

  // State
  const vups = ref<VupInfo[]>([]);
  const currentVupUid = ref('');
  const vupLoading = ref(false);
  const currentSession = ref<SessionInfo | null>(null);
  const sessionSummary = ref<any>(null);
  // Use shallowRef to avoid Vue deep reactivity proxy overhead on large arrays.
  // Items are never mutated in-place — the entire array is always replaced,
  // so deep reactivity provides zero benefit but costs ~5-10x memory per item.
  const danmakuList = shallowRef<Danmaku[]>([]);
  const scList = shallowRef<Danmaku[]>([]);
  const loading = ref(false);
  const danmakuLoading = ref(false);
  const isDanmakuLoaded = ref(false);
  const totalDanmaku = ref(0);
  const currentPage = ref(0);
  const totalPages = ref(0);
  
  // Filters
  const searchText = ref('');
  const showSC = ref(true);
  const showDanmaku = ref(true);
  const isSidebarCollapsed = ref(true);
  const zoomLevel = ref(110);
  const timeDisplayMode = ref<'relative' | 'absolute'>('relative');

  // Getters
  const currentVup = computed<VupInfo | null>(() => {
    if (currentVupUid.value) {
      const matched = vups.value.find(vup => vup.uid === currentVupUid.value);
      if (matched) {
        return matched;
      }
    }

    return vups.value[0] ?? null;
  });

  const getSavedVupUid = () => {
    const savedUid = localStorage.getItem(SELECTED_VUP_STORAGE_KEY);
    return savedUid ? savedUid.trim() : '';
  };

  const persistCurrentVup = (uid: string) => {
    currentVupUid.value = uid;
    localStorage.setItem(SELECTED_VUP_STORAGE_KEY, uid);
  };

  const syncCurrentVup = (preferredUid?: string) => {
    const resolvedPreferredUid = preferredUid?.trim();
    const currentUid = currentVupUid.value.trim();
    const savedUid = getSavedVupUid();
    const candidates = [resolvedPreferredUid, currentUid, savedUid].filter(Boolean) as string[];

    for (const candidate of candidates) {
      if (vups.value.some(vup => vup.uid === candidate)) {
        persistCurrentVup(candidate);
        return;
      }
    }

    const firstUid = vups.value[0]?.uid;
    if (firstUid) {
      persistCurrentVup(firstUid);
    } else {
      currentVupUid.value = '';
      localStorage.removeItem(SELECTED_VUP_STORAGE_KEY);
    }
  };

  const getVupByUid = (uid?: string | null) => {
    if (!uid) return undefined;
    return vups.value.find(vup => vup.uid === uid);
  };

  // Actions
  const loadVupsAction = async (preferredUid?: string, force: boolean = false) => {
    if (vupLoadPromise && !force) {
      await vupLoadPromise;
      syncCurrentVup(preferredUid);
      return vups.value;
    }

    vupLoading.value = true;
    vupLoadPromise = getVups()
      .then(list => {
        vups.value = list;
        syncCurrentVup(preferredUid);
        return list;
      })
      .finally(() => {
        vupLoading.value = false;
        vupLoadPromise = null;
      });

    return await vupLoadPromise;
  };

  const setCurrentVup = (uid: string) => {
    if (vups.value.some(vup => vup.uid === uid)) {
      persistCurrentVup(uid);
    }
  };

  const initVupSelection = async (preferredUid?: string) => {
    if (vups.value.length === 0) {
      await loadVupsAction(preferredUid);
      return;
    }

    syncCurrentVup(preferredUid);
  };

  const setZoomLevel = (val: number) => {
    zoomLevel.value = val;
  };
  const toggleSidebar = () => {
    isSidebarCollapsed.value = !isSidebarCollapsed.value;
  };

  const loadSession = async (session: SessionInfo | number) => {
    loading.value = true;
    try {
      const sessionId = typeof session === 'number' ? session : session.id;
      
      // Update current session info
      if (typeof session !== 'number') {
        currentSession.value = session;
      } else if (!currentSession.value || currentSession.value.id !== sessionId) {
        // If we only have ID, we might want to fetch it, but for now just set ID
        currentSession.value = { id: sessionId } as SessionInfo;
      }

      // Reset
      danmakuList.value = [];
      scList.value = [];
      sessionSummary.value = null;
      currentPage.value = 1;
      isDanmakuLoaded.value = false;
      
      // Load ONLY summary by default
      const summaryRes = await getSessionSummary(sessionId);
      
      // 合并摘要字段，确保保留 gift_summary_json 等顶级字段
      sessionSummary.value = {
        ...summaryRes,
        ...(summaryRes.summary || {})
      };
      
      // Get total count from summary if available
      if (sessionSummary.value && sessionSummary.value.totalCount) {
        totalDanmaku.value = sessionSummary.value.totalCount;
      }
    } catch (e) {
      console.error(e);
    } finally {
      loading.value = false;
    }
  };

  // 去重：基于 user + uid + timestamp + isSC + price 判断（忽略 content，因双语 SC 内容不同）
  // 如果相邻两条 SC 的 content 不同但其他字段相同，合并为一条并设置 contentJpn
  const deduplicateDanmaku = (list: Danmaku[]): Danmaku[] => {
    const result: Danmaku[] = [];
    for (const d of list) {
      if (d.isSC && result.length > 0) {
        const prev = result[result.length - 1];
        if (prev.isSC
          && prev.user === d.user
          && prev.uid === d.uid
          && Math.abs(prev.timestamp - d.timestamp) <= 2000
          && prev.price === d.price)
        {
          if (prev.content !== d.content && !prev.contentJpn) {
            prev.contentJpn = d.content;
          }
          continue;
        }
      }
      result.push(d);
    }
    return result;
  };

  const fetchDanmaku = async () => {
    if (!currentSession.value || danmakuLoading.value || isDanmakuLoaded.value) return;

    danmakuLoading.value = true;
    try {
      const res = await getSessionDanmaku(currentSession.value.id, 1, 5000);
      const deduplicated = deduplicateDanmaku(res.danmaku);
      danmakuList.value = deduplicated;
      scList.value = deduplicated.filter(d => d.isSC);
      totalDanmaku.value = res.total;
      totalPages.value = res.totalPages;
      currentPage.value = res.page;
      isDanmakuLoaded.value = true;
    } catch (e) {
      console.error(e);
    } finally {
      danmakuLoading.value = false;
    }
  };

  const loadMore = async () => {
    if (currentPage.value >= totalPages.value || loading.value) return;

    loading.value = true;
    try {
      const nextPage = currentPage.value + 1;
      const res = await getSessionDanmaku(currentSession.value!.id, nextPage, 5000);
      const deduplicated = deduplicateDanmaku([...danmakuList.value, ...res.danmaku]);
      danmakuList.value = deduplicated;
      scList.value = deduplicated.filter(d => d.isSC);
      currentPage.value = nextPage;
    } catch (e) {
      console.error(e);
    } finally {
      loading.value = false;
    }
  };

  const filteredDanmaku = computed(() => {
    let list = danmakuList.value;
    
    // Type filtering
    if (!showSC.value) {
      list = list.filter(d => !d.isSC);
    }
    if (!showDanmaku.value) {
      list = list.filter(d => d.isSC);
    }
    
    // Search filtering
    if (searchText.value) {
      const lower = searchText.value.toLowerCase();
      list = list.filter(d => 
        d.content.toLowerCase().includes(lower) || 
        d.user.toLowerCase().includes(lower)
      );
    }
    return list;
  });

  const clearSession = () => {
    currentSession.value = null;
    sessionSummary.value = null;
    danmakuList.value = [];
    scList.value = [];
    totalDanmaku.value = 0;
    currentPage.value = 0;
    totalPages.value = 0;
    isDanmakuLoaded.value = false;
  };

  return {
    currentSession,
    sessionSummary,
    danmakuList,
    scList,
    loading,
    danmakuLoading,
    isDanmakuLoaded,
    totalDanmaku,
    currentPage,
    totalPages,
    
    // Filters
    searchText,
    showSC,
    showDanmaku,
    isSidebarCollapsed,
    zoomLevel,
    timeDisplayMode,
    
    // Vup State
    vups,
    vupLoading,
    currentVupUid,
    currentVup,
    getVupByUid,
    
    // Actions
    loadVups: loadVupsAction,
    setCurrentVup,
    initVupSelection,
    setZoomLevel,
    toggleSidebar,
    loadSession,
    clearSession,
    fetchDanmaku,
    loadMore,
    filteredDanmaku
  };
});
