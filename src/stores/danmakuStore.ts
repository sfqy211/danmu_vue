import { defineStore } from 'pinia';
import { ref, computed } from 'vue';
import { getSessionDanmaku, getSessionSummary, type Danmaku, type SessionInfo } from '../api/danmaku';

export const useDanmakuStore = defineStore('danmaku', () => {
  // State
  const currentSession = ref<SessionInfo | null>(null);
  const sessionSummary = ref<any>(null);
  const danmakuList = ref<Danmaku[]>([]);
  const scList = ref<Danmaku[]>([]);
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
  const zoomLevel = ref(100);

  // Actions
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

  const fetchDanmaku = async () => {
    if (!currentSession.value || danmakuLoading.value || isDanmakuLoaded.value) return;
    
    danmakuLoading.value = true;
    try {
      const res = await getSessionDanmaku(currentSession.value.id, 1, 5000);
      danmakuList.value = res.danmaku;
      scList.value = res.danmaku.filter(d => d.isSC);
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
      danmakuList.value = [...danmakuList.value, ...res.danmaku];
      scList.value = [...scList.value, ...res.danmaku.filter(d => d.isSC)];
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
    searchText,
    showSC,
    showDanmaku,
    isSidebarCollapsed,
    zoomLevel,
    setZoomLevel,
    toggleSidebar,
    loadSession,
    fetchDanmaku,
    loadMore,
    filteredDanmaku
  };
});
