<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch, computed, provide } from 'vue';
import { useRouter } from 'vue-router';
import {
  adminApi, getBiliAccounts, getBiliAccountAssignments, reassignBiliRoom,
  importBiliCookie, startBiliQrLogin, pollBiliQrLogin, cancelBiliQrLogin,
  activateBiliAccount, refreshBiliAccountInfo, refreshBiliAccountAuth, deleteBiliAccount,
  getHealthCheckReport, type HealthCheckReport,
  type BiliAccount, type AccountAssignment
} from '../api/danmaku';
import { getAdminChangelog, addChangelog, updateChangelog, deleteChangelog, type ChangelogEntry } from '../api/danmaku';
import LogViewer from '../components/LogViewer.vue';
import { ElMessage, ElMessageBox } from 'element-plus';
import { 
  Refresh, Switch, Plus, VideoPlay, Delete, EditPen, 
  VideoCamera, DataLine, Fold, Expand, ArrowDown, House, User, Document,
  Sunny, Moon
} from '@element-plus/icons-vue';

// --- Interfaces ---
interface Room {
  id: number;
  room_id: number;
  name: string;
  uid: string;
  auto_record: number;
  process_status: string;
  process_uptime: number | string;
  live_status: number;
  live_start_time: number | null;
  pid: number | null;
  remark?: string;
  playlistUrl?: string;
}

interface AdminSession {
  id: number;
  uid?: string;
  roomId: string;
  title: string;
  userName: string;
  startTime?: number;
  endTime?: number;
  filePath?: string;
}

interface AdminSongRequest {
  id: number;
  sessionId?: number;
  roomId?: string;
  userName?: string;
  uid?: string;
  songName?: string;
  singer?: string;
  createdAt?: number;
}

// --- State Management ---

// Auth
const token = ref(localStorage.getItem('admin_token') || '');
const isAuthenticated = ref(false);
const router = useRouter();

// UI State
const loading = ref(false);
const error = ref('');
const activeSection = ref<'monitor' | 'sessions' | 'songRequests' | 'accounts' | 'changelog' | 'logs'>('monitor');
const isDarkMode = ref(localStorage.getItem('admin_dark_mode') === 'true');
provide('adminDarkMode', isDarkMode);
const isMobile = ref(window.innerWidth <= 768);
const sidebarCollapsed = ref(window.innerWidth <= 768);
const searchCollapsed = ref(window.innerWidth <= 768);

// Handle resize for responsive states
const handleResize = () => {
  const mobile = window.innerWidth <= 768;
  isMobile.value = mobile;
  if (mobile) {
    sidebarCollapsed.value = true;
    searchCollapsed.value = true;
  }
};

// Monitor Data
const rooms = ref<Room[]>([]);
const newRoom = ref({ roomId: '', name: '', uid: '', remark: '', playlistUrl: '' });
const adding = ref(false);
const editDialogVisible = ref(false);
const editForm = ref({ id: 0, remark: '', playlistUrl: '' });
const healthCheckLoading = ref(false);
const healthCheckReport = ref<HealthCheckReport | null>(null);

// Sessions Data
const sessions = ref<AdminSession[]>([]);
const sessionLoading = ref(false);
const sessionRecalcLoading = ref(false);
const sessionSearch = ref('');
const sessionFilterRoomId = ref('');
const sessionPage = ref(1);
const sessionPageSize = ref(20);
const sessionTotal = ref(0);
const sessionDialogVisible = ref(false);
const sessionFormMode = ref<'create' | 'edit'>('create');
const sessionForm = ref({
  id: 0,
  roomId: '',
  title: '',
  userName: '',
  startTime: '',
  endTime: '',
  filePath: ''
});

// Song Requests Data
const songRequests = ref<AdminSongRequest[]>([]);
const songLoading = ref(false);
const songSearch = ref('');
const songFilterUserName = ref('');
const songFilterRoomId = ref('');
const songPage = ref(1);
const songPageSize = ref(20);
const songTotal = ref(0);
const songDialogVisible = ref(false);
const songFormMode = ref<'create' | 'edit'>('create');
const songForm = ref({
  id: 0,
  sessionId: '',
  roomId: '',
  userName: '',
  uid: '',
  songName: '',
  singer: '',
  createdAt: ''
});

// BiliAccount Data
const biliAccounts = ref<BiliAccount[]>([]);
const accountLoading = ref(false);
const qrDialogVisible = ref(false);
const qrUrl = ref('');
const qrId = ref('');
const qrPolling = ref(false);
const importCookieDialogVisible = ref(false);
const importCookieForm = ref({ uid: '', cookie: '' });
const assignments = ref<AccountAssignment[]>([]);
const reassignDialogVisible = ref(false);
const reassignForm = ref({ roomUid: '', roomName: '', targetUid: 0 });

// Account rooms management dialog
const accountRoomsDialogVisible = ref(false);
const currentAccountRooms = ref<AccountAssignment[]>([]);
const currentAccountName = ref('');
const currentAccountUid = ref(0);

// Changelog Data
const changelogLoading = ref(false);
const changelogList = ref<ChangelogEntry[]>([]);
const changelogDialogVisible = ref(false);
const changelogForm = ref({ id: 0, version: '', date: '', content: '' });
const changelogIsEdit = ref(false);

// --- Selection State & Batch Actions ---

const selectedRooms = ref<Room[]>([]);
const selectedSessions = ref<AdminSession[]>([]);
const selectedSongRequests = ref<AdminSongRequest[]>([]);

const handleRoomSelectionChange = (val: Room[]) => {
  selectedRooms.value = val;
};

const handleSessionSelectionChange = (val: AdminSession[]) => {
  selectedSessions.value = val;
};

const handleSongSelectionChange = (val: AdminSongRequest[]) => {
  selectedSongRequests.value = val;
};

const goHome = () => {
  router.push({ name: 'home' });
};

const batchDeleteRooms = async () => {
  if (selectedRooms.value.length === 0) return;
  try {
    await ElMessageBox.confirm(`确定要删除选中的 ${selectedRooms.value.length} 个主播配置吗？这将停止录制并移除配置。`, '批量删除确认', {
      confirmButtonText: '删除',
      cancelButtonText: '取消',
      type: 'warning'
    });
    
    let successCount = 0;
    for (const room of selectedRooms.value) {
      try {
        await adminApi.delete(`/admin/rooms/${room.id}`, getAuthConfig());
        successCount++;
      } catch (e) {
        console.error(`Failed to delete room ${room.id}`, e);
      }
    }
    
    if (successCount > 0) {
      ElMessage.success(`成功删除 ${successCount} 个主播配置`);
      await fetchRooms();
      selectedRooms.value = []; 
    } else {
      ElMessage.warning('没有成功删除任何配置');
    }
  } catch (e: any) {
    if (e !== 'cancel') {
      ElMessage.error('批量删除操作异常: ' + (e.message || '未知错误'));
    }
  }
};

const batchRestartRooms = async () => {
  if (selectedRooms.value.length === 0) return;
  try {
    await ElMessageBox.confirm(`确定要重启选中的 ${selectedRooms.value.length} 个主播录制吗？`, '批量重启确认', {
      confirmButtonText: '重启',
      cancelButtonText: '取消',
      type: 'warning'
    });

    let successCount = 0;
    for (const room of selectedRooms.value) {
      try {
        await adminApi.post(`/admin/rooms/${room.id}/restart`, {}, getAuthConfig());
        successCount++;
      } catch (e) {
        console.error(`Failed to restart room ${room.id}`, e);
      }
    }

    if (successCount > 0) {
      ElMessage.success(`成功重启 ${successCount} 个录制任务`);
      // Wait a bit before refreshing
      setTimeout(fetchRooms, 2000);
    } else {
      ElMessage.warning('没有成功重启任何任务');
    }
  } catch (e: any) {
    if (e !== 'cancel') {
      ElMessage.error('批量重启操作异常: ' + (e.message || '未知错误'));
    }
  }
};

const openEditRoom = (row: Room) => {
  editForm.value = {
    id: row.id,
    remark: row.remark || '',
    playlistUrl: row.playlistUrl || ''
  };
  editDialogVisible.value = true;
};

const saveRoomEdit = async () => {
  try {
    await adminApi.put(`/admin/rooms/${editForm.value.id}`, {
      roomId: 0, // Not used
      name: 'Unknown', // Not used
      remark: editForm.value.remark,
      playlistUrl: editForm.value.playlistUrl
    }, getAuthConfig());
    ElMessage.success('更新成功');
    editDialogVisible.value = false;
    await fetchRooms();
  } catch (e: any) {
    ElMessage.error('更新失败: ' + (e.response?.data?.error || e.message));
  }
};

const batchDeleteSessions = async () => {
  if (selectedSessions.value.length === 0) return;
  try {
    await ElMessageBox.confirm(`确定要删除选中的 ${selectedSessions.value.length} 个直播场次吗？相关点歌记录也会删除。`, '批量删除确认', {
      confirmButtonText: '删除',
      cancelButtonText: '取消',
      type: 'warning'
    });
    
    let successCount = 0;
    for (const session of selectedSessions.value) {
      try {
        await adminApi.delete(`/admin/sessions/${session.id}`, getAuthConfig());
        successCount++;
      } catch (e) {
         console.error(`Failed to delete session ${session.id}`, e);
      }
    }
    
    if (successCount > 0) {
      ElMessage.success(`成功删除 ${successCount} 个直播场次`);
      await fetchSessions();
      selectedSessions.value = [];
    } else {
       ElMessage.warning('没有成功删除任何场次');
    }
  } catch (e: any) {
    if (e !== 'cancel') {
      ElMessage.error('批量删除操作异常: ' + (e.message || '未知错误'));
    }
  }
};

const batchRecalculateSessions = async () => {
  if (selectedSessions.value.length === 0) return;
  try {
    await ElMessageBox.confirm(`确定要重新统计选中的 ${selectedSessions.value.length} 个直播场次吗？`, '重新统计确认', {
      confirmButtonText: '开始',
      cancelButtonText: '取消',
      type: 'warning'
    });

    sessionRecalcLoading.value = true;
    const res = await adminApi.post('/admin/sessions/recalculate', {
      sessionIds: selectedSessions.value.map((s) => s.id)
    }, getAuthConfig());
    const data = res.data || {};
    const successCount = data.successCount ?? 0;
    const skippedCount = data.skippedCount ?? 0;
    const failedCount = data.failedCount ?? 0;

    if (failedCount > 0) {
      ElMessage.warning(`完成重新统计：成功 ${successCount}，跳过 ${skippedCount}，失败 ${failedCount}`);
    } else {
      ElMessage.success(`完成重新统计：成功 ${successCount}，跳过 ${skippedCount}`);
    }
    await fetchSessions();
  } catch (e: any) {
    if (e !== 'cancel') {
      ElMessage.error('重新统计操作异常: ' + (e.response?.data?.error || e.message || '未知错误'));
    }
  } finally {
    sessionRecalcLoading.value = false;
  }
};

const batchDeleteSongRequests = async () => {
  if (selectedSongRequests.value.length === 0) return;
  try {
    await ElMessageBox.confirm(`确定要删除选中的 ${selectedSongRequests.value.length} 条点歌记录吗？`, '批量删除确认', {
      confirmButtonText: '删除',
      cancelButtonText: '取消',
      type: 'warning'
    });
    
    let successCount = 0;
    for (const req of selectedSongRequests.value) {
      try {
        await adminApi.delete(`/admin/song-requests/${req.id}`, getAuthConfig());
        successCount++;
      } catch (e) {
        console.error(`Failed to delete song request ${req.id}`, e);
      }
    }
    
    if (successCount > 0) {
      ElMessage.success(`成功删除 ${successCount} 条点歌记录`);
      await fetchSongRequests();
      selectedSongRequests.value = [];
    } else {
       ElMessage.warning('没有成功删除任何记录');
    }
  } catch (e: any) {
    if (e !== 'cancel') {
      ElMessage.error('批量删除操作异常: ' + (e.message || '未知错误'));
    }
  }
};

// --- Computed Properties ---

const breadcrumbs = computed(() => {
  const items = [{ name: '首页', path: '/' }];
  if (activeSection.value === 'monitor') {
    items.push({ name: '监控录制管理', path: '' });
  } else if (activeSection.value === 'sessions') {
    items.push({ name: '数据库管理', path: '' });
    items.push({ name: '直播回放', path: '' });
  } else if (activeSection.value === 'songRequests') {
    items.push({ name: '数据库管理', path: '' });
    items.push({ name: '点歌记录', path: '' });
  } else if (activeSection.value === 'accounts') {
    items.push({ name: '账户管理', path: '' });
  } else if (activeSection.value === 'changelog') {
    items.push({ name: '更新日志', path: '' });
  }
  return items;
});

const isRefreshing = computed(() => {
  if (activeSection.value === 'monitor') return loading.value;
  if (activeSection.value === 'sessions') return sessionLoading.value;
  if (activeSection.value === 'songRequests') return songLoading.value;
  if (activeSection.value === 'accounts') return accountLoading.value;
  if (activeSection.value === 'changelog') return changelogLoading.value;
  return false;
});

// --- Methods: Auth & Layout ---

const getAuthConfig = () => {
  const value = (token.value || '').trim();
  if (!value) return {};

  const baseUrl = adminApi.defaults.baseURL || '/api';
  let isCrossOrigin = false;
  if (typeof window !== 'undefined') {
    try {
      const url = new URL(baseUrl, window.location.origin);
      isCrossOrigin = url.origin !== window.location.origin;
    } catch {
      isCrossOrigin = false;
    }
  }

  if (isCrossOrigin) {
    return { params: { token: value } };
  }

  return { headers: { Authorization: value }, params: { token: value } };
};

const checkAuth = async () => {
  if (!token.value) return;
  try {
    await fetchRooms();
    isAuthenticated.value = true;
    localStorage.setItem('admin_token', token.value);
    // If initially on database page, load data
    if (activeSection.value !== 'monitor') {
      await refreshDatabaseData();
    }
  } catch (e: any) {
    if (e.response && e.response.status === 401) {
      error.value = 'Token 无效';
      isAuthenticated.value = false;
    } else {
      error.value = '连接服务器失败';
    }
  }
};

const logout = () => {
  token.value = '';
  localStorage.removeItem('admin_token');
  isAuthenticated.value = false;
  rooms.value = [];
  sessions.value = [];
  songRequests.value = [];
};

const handleMenuSelect = (index: string) => {
  if (['monitor', 'sessions', 'songRequests', 'accounts', 'changelog', 'logs'].includes(index)) {
    activeSection.value = index as any;
  }
  if (isMobile.value) {
    sidebarCollapsed.value = true;
  }
};

const toggleTheme = () => {
  isDarkMode.value = !isDarkMode.value;
  localStorage.setItem('admin_dark_mode', String(isDarkMode.value));
};

const refreshCurrentSection = async () => {
  if (activeSection.value === 'monitor') {
    await fetchRooms();
  } else {
    await refreshDatabaseData();
  }
};

// --- Methods: Data Fetching ---

const refreshDatabaseData = async () => {
  if (!isAuthenticated.value) return;
  
  if (activeSection.value === 'sessions') {
    await fetchSessions();
  } else if (activeSection.value === 'songRequests') {
    await fetchSongRequests();
  } else if (activeSection.value === 'accounts') {
    await fetchAccounts();
  } else if (activeSection.value === 'changelog') {
    await loadChangelogList();
  }
};

const fetchRooms = async () => {
  loading.value = true;
  try {
    const [roomsRes, healthReport] = await Promise.all([
      adminApi.get('/admin/rooms', getAuthConfig()),
      fetchHealthCheckReport().catch(() => null)
    ]);
    rooms.value = Array.isArray(roomsRes.data) ? roomsRes.data.map(normalizeRoomRow) : [];
    if (healthReport) {
      healthCheckReport.value = healthReport;
    }
    error.value = '';
  } catch (e: any) {
    console.error('Fetch rooms failed:', e);
    // Don't throw here to prevent blocking UI
  } finally {
    loading.value = false;
  }
};

const fetchHealthCheckReport = async () => {
  healthCheckLoading.value = true;
  try {
    const report = await getHealthCheckReport();
    healthCheckReport.value = report;
    return report;
  } finally {
    healthCheckLoading.value = false;
  }
};

const healthIssueCount = computed(() => {
  if (!healthCheckReport.value) return 0;
  return healthCheckReport.value.staleHeartbeats.length + healthCheckReport.value.driftIssues.length;
});

const formatHealthCheckTime = (value?: string) => {
  if (!value) return '-';
  const d = new Date(value);
  if (isNaN(d.getTime())) return value;
  return d.toLocaleString();
};

const fetchSessions = async () => {
  sessionLoading.value = true;
  try {
    const res = await adminApi.get('/admin/sessions', {
      ...getAuthConfig(),
      params: {
        ...(getAuthConfig().params || {}),
        page: sessionPage.value,
        pageSize: sessionPageSize.value,
        search: (sessionSearch.value || '').trim() || undefined,
        roomId: (sessionFilterRoomId.value || '').trim() || undefined
      }
    });
    const data = res.data;
    sessions.value = Array.isArray(data.list) ? data.list.map(normalizeSessionRow) : [];
    sessionTotal.value = data.total ?? sessions.value.length;
  } catch (e: any) {
    console.error('Fetch sessions failed:', e);
    ElMessage.error('加载直播回放失败: ' + (e.response?.data?.error || e.message));
  } finally {
    sessionLoading.value = false;
  }
};

const fetchSongRequests = async () => {
  songLoading.value = true;
  try {
    const res = await adminApi.get('/admin/song-requests', {
      ...getAuthConfig(),
      params: {
        ...(getAuthConfig().params || {}),
        page: songPage.value,
        pageSize: songPageSize.value,
        search: (songSearch.value || '').trim() || undefined,
        userName: (songFilterUserName.value || '').trim() || undefined,
        roomId: (songFilterRoomId.value || '').trim() || undefined
      }
    });
    const data = res.data;
    songRequests.value = Array.isArray(data.list) ? data.list.map(normalizeSongRequestRow) : [];
    songTotal.value = data.total ?? songRequests.value.length;
  } catch (e: any) {
    console.error('Fetch song requests failed:', e);
    ElMessage.error('加载点歌记录失败: ' + (e.response?.data?.error || e.message));
  } finally {
    songLoading.value = false;
  }
};

// --- Methods: BiliAccount ---

const fetchAccounts = async () => {
  accountLoading.value = true;
  try {
    const [accounts, assignList] = await Promise.all([
      getBiliAccounts(),
      getBiliAccountAssignments().catch(() => [])
    ]);
    biliAccounts.value = accounts;
    assignments.value = assignList;
  } catch (e: any) {
    console.error('Fetch accounts failed:', e);
    ElMessage.error('加载账户失败: ' + (e.response?.data?.error || e.message));
  } finally {
    accountLoading.value = false;
  }
};

const openQrLogin = async () => {
  qrDialogVisible.value = true;
  qrUrl.value = '';
  qrId.value = '';
  qrPolling.value = false;
  try {
    const res = await startBiliQrLogin();
    qrUrl.value = res.url;
    qrId.value = res.id;
    qrPolling.value = true;
    pollQrLogin();
  } catch (e: any) {
    ElMessage.error('获取二维码失败: ' + (e.response?.data?.message || e.message));
    qrDialogVisible.value = false;
  }
};

let qrPollInterval: ReturnType<typeof setInterval> | null = null;
const pollQrLogin = () => {
  if (qrPollInterval) clearInterval(qrPollInterval);
  qrPollInterval = setInterval(async () => {
    if (!qrId.value || !qrDialogVisible.value) {
      if (qrPollInterval) clearInterval(qrPollInterval);
      return;
    }
    try {
      const res = await pollBiliQrLogin(qrId.value);
      if (res.status === 'completed') {
        if (qrPollInterval) clearInterval(qrPollInterval);
        qrPolling.value = false;
        qrDialogVisible.value = false;
        ElMessage.success('登录成功');
        fetchAccounts();
      } else if (res.status === 'error') {
        if (qrPollInterval) clearInterval(qrPollInterval);
        qrPolling.value = false;
        ElMessage.error('登录失败: ' + (res.fail_reason || '未知错误'));
      }
    } catch (e) {
      // ignore poll errors
    }
  }, 2000);
};

const closeQrLogin = () => {
  if (qrPollInterval) clearInterval(qrPollInterval);
  if (qrId.value) cancelBiliQrLogin(qrId.value).catch(() => {});
};

const openImportCookie = () => {
  importCookieForm.value = { uid: '', cookie: '' };
  importCookieDialogVisible.value = true;
};

const submitImportCookie = async () => {
  if (!importCookieForm.value.uid || !importCookieForm.value.cookie) {
    ElMessage.warning('请输入 UID 和 Cookie');
    return;
  }
  try {
    await importBiliCookie(Number(importCookieForm.value.uid), importCookieForm.value.cookie);
    ElMessage.success('导入成功');
    importCookieDialogVisible.value = false;
    fetchAccounts();
  } catch (e: any) {
    ElMessage.error('导入失败: ' + (e.response?.data?.message || e.message));
  }
};

const activateAccount = async (uid: number) => {
  try {
    await activateBiliAccount(uid);
    ElMessage.success('已切换账户');
    fetchAccounts();
  } catch (e: any) {
    ElMessage.error('切换失败: ' + (e.response?.data?.message || e.message));
  }
};

const refreshAccountInfo = async (uid: number) => {
  try {
    await refreshBiliAccountInfo(uid);
    ElMessage.success('已刷新信息');
    fetchAccounts();
  } catch (e: any) {
    ElMessage.error('刷新失败: ' + (e.response?.data?.message || e.message));
  }
};

const refreshAccountAuth = async (uid: number) => {
  try {
    await refreshBiliAccountAuth(uid);
    ElMessage.success('已更新授权');
    fetchAccounts();
  } catch (e: any) {
    ElMessage.error('更新授权失败: ' + (e.response?.data?.message || e.message));
  }
};

const removeAccount = async (uid: number) => {
  try {
    await ElMessageBox.confirm('确定要删除该账户吗？', '删除确认', { type: 'warning' });
    await deleteBiliAccount(uid);
    ElMessage.success('已删除');
    fetchAccounts();
  } catch (e: any) {
    if (e !== 'cancel') {
      ElMessage.error('删除失败: ' + (e.response?.data?.message || e.message));
    }
  }
};

const getAccountRooms = (accountUid: number) => {
  return assignments.value.filter(a => a.account_uid === accountUid);
};

const openReassignDialog = (room: AccountAssignment) => {
  reassignForm.value = { roomUid: room.room_uid, roomName: room.room_name || room.room_uid, targetUid: 0 };
  reassignDialogVisible.value = true;
};

const submitReassign = async () => {
  if (!reassignForm.value.targetUid) {
    ElMessage.warning('请选择目标账户');
    return;
  }
  try {
    await reassignBiliRoom(reassignForm.value.roomUid, reassignForm.value.targetUid);
    ElMessage.success('房间已移动到目标账户');
    reassignDialogVisible.value = false;
    fetchAccounts();
  } catch (e: any) {
    ElMessage.error('移动失败: ' + (e.response?.data?.message || e.message));
  }
};

const openAccountRoomsDialog = (accountUid: number, accountName: string) => {
  currentAccountUid.value = accountUid;
  currentAccountName.value = accountName || '未知用户';
  currentAccountRooms.value = getAccountRooms(accountUid);
  accountRoomsDialogVisible.value = true;
};

const isExpiringSoon = (expiresAt: number) => {
  if (!expiresAt) return false;
  return expiresAt - Date.now() < 10 * 24 * 60 * 60 * 1000;
};

const formatDate = (ts: number) => {
  if (!ts) return '-';
  const d = new Date(ts);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
};

// --- Methods: Data Normalization ---

const normalizeRoomRow = (row: any): Room => ({
  id: row.id ?? row.Id ?? 0,
  room_id: row.room_id ?? row.roomId ?? row.RoomId ?? 0,
  name: row.name ?? row.Name ?? '',
  uid: row.uid ?? row.Uid ?? '',
  auto_record: row.auto_record ?? row.autoRecord ?? row.AutoRecord ?? 0,
  process_status: row.process_status ?? row.processStatus ?? row.ProcessStatus ?? 'stopped',
  process_uptime: row.process_uptime ?? row.processUptime ?? row.ProcessUptime ?? '0s',
  live_status: row.live_status ?? row.liveStatus ?? row.LiveStatus ?? 0,
  live_start_time: row.live_start_time ?? row.liveStartTime ?? row.LiveStartTime ?? null,
  pid: row.pid ?? row.Pid ?? null,
  remark: row.remark ?? row.Remark ?? '',
  playlistUrl: row.playlistUrl ?? row.playlist_url ?? row.PlaylistUrl ?? row.Playlist_url ?? ''
});

const normalizeSessionRow = (row: any): AdminSession => ({
  id: row.id ?? row.Id ?? 0,
  uid: row.uid ?? row.Uid ?? '',
  roomId: row.roomId ?? row.room_id ?? row.RoomId ?? '',
  title: row.title ?? row.Title ?? '',
  userName: row.userName ?? row.user_name ?? row.UserName ?? '',
  startTime: row.startTime ?? row.start_time ?? row.StartTime,
  endTime: row.endTime ?? row.end_time ?? row.EndTime,
  filePath: row.filePath ?? row.file_path ?? row.FilePath
});

const normalizeSongRequestRow = (row: any): AdminSongRequest => ({
  id: row.id ?? row.Id ?? 0,
  sessionId: row.sessionId ?? row.session_id ?? row.SessionId,
  roomId: row.roomId ?? row.room_id ?? row.RoomId,
  userName: row.userName ?? row.user_name ?? row.UserName,
  uid: row.uid ?? row.Uid,
  songName: row.songName ?? row.song_name ?? row.SongName,
  singer: row.singer ?? row.Singer,
  createdAt: row.createdAt ?? row.created_at ?? row.CreatedAt
});

// --- Methods: Actions (Room) ---

const addRoom = async () => {
  // 必须提供 UID
  if (!newRoom.value.uid) {
    ElMessage.warning('请提供 UID');
    return;
  }
  
  adding.value = true;
  try {
    await adminApi.post('/admin/rooms', {
      uid: newRoom.value.uid,
      remark: newRoom.value.remark,
      playlistUrl: newRoom.value.playlistUrl,
      name: 'Unknown', // Backend will resolve this, but DTO requires it
      roomId: 0 // Backend will resolve this
    }, getAuthConfig());
    newRoom.value = { roomId: '', name: '', uid: '', remark: '', playlistUrl: '' };
    ElMessage.success('添加成功，已自动获取直播间信息并启动录制');
    await fetchRooms();
  } catch (e: any) {
    ElMessage.error('添加失败: ' + (e.response?.data?.error || e.message));
  } finally {
    adding.value = false;
  }
};

const deleteRoom = async (id: number, name: string) => {
  try {
    await ElMessageBox.confirm(`确定要删除 "${name}" 吗？这将停止录制并移除配置。`, '删除确认', {
      confirmButtonText: '删除',
      cancelButtonText: '取消',
      type: 'warning'
    });
    
    await adminApi.delete(`/admin/rooms/${id}`, getAuthConfig());
    ElMessage.success('删除成功');
    await fetchRooms();
  } catch (e: any) {
    if (e !== 'cancel') {
      ElMessage.error('删除失败: ' + (e.response?.data?.error || e.message));
    }
  }
};

const restartRoom = async (id: number) => {
  try {
    await adminApi.post(`/admin/rooms/${id}/restart`, {}, getAuthConfig());
    ElMessage.success('重启指令已发送');
    setTimeout(fetchRooms, 2000);
  } catch (e: any) {
    ElMessage.error('重启失败: ' + (e.response?.data?.error || e.message));
  }
};

const toggleRoomMonitor = async (row: Room, active: boolean) => {
  try {
    await adminApi.post(`/admin/rooms/${row.id}/toggle-monitor`, { autoRecord: active }, getAuthConfig());
    ElMessage.success(active ? '已启用监控' : '已停用监控');
    // Delayed refresh to show updated process status
    setTimeout(fetchRooms, 1500);
  } catch (e: any) {
    row.auto_record = active ? 0 : 1; // Revert on error
    ElMessage.error('切换状态失败: ' + (e.response?.data?.error || e.message));
  }
};

// --- Methods: Actions (Sessions) ---

const applySessionFilters = async () => {
  sessionPage.value = 1;
  await fetchSessions();
};

const openCreateSession = () => {
  sessionFormMode.value = 'create';
  sessionForm.value = {
    id: 0,
    roomId: '',
    title: '',
    userName: '',
    startTime: '',
    endTime: '',
    filePath: ''
  };
  sessionDialogVisible.value = true;
};

const openEditSession = (row: AdminSession) => {
  sessionFormMode.value = 'edit';
  sessionForm.value = {
    id: row.id,
    roomId: row.roomId ?? '',
    title: row.title ?? '',
    userName: row.userName ?? '',
    startTime: row.startTime ? String(row.startTime) : '',
    endTime: row.endTime ? String(row.endTime) : '',
    filePath: row.filePath ?? ''
  };
  sessionDialogVisible.value = true;
};

const saveSession = async () => {
  const payload = {
    roomId: sessionForm.value.roomId || null,
    title: sessionForm.value.title || null,
    userName: sessionForm.value.userName || null,
    startTime: sessionForm.value.startTime ? Number(sessionForm.value.startTime) : null,
    endTime: sessionForm.value.endTime ? Number(sessionForm.value.endTime) : null,
    filePath: sessionForm.value.filePath || null
  };

  try {
    if (sessionFormMode.value === 'create') {
      await adminApi.post('/admin/sessions', payload, getAuthConfig());
      ElMessage.success('直播场次已添加');
    } else {
      await adminApi.put(`/admin/sessions/${sessionForm.value.id}`, payload, getAuthConfig());
      ElMessage.success('直播场次已更新');
    }
    sessionDialogVisible.value = false;
    await fetchSessions();
  } catch (e: any) {
    ElMessage.error('保存失败: ' + (e.response?.data?.error || e.message));
  }
};

const deleteSession = async (row: AdminSession) => {
  try {
    await ElMessageBox.confirm(`确定要删除直播场次 "${row.title || row.id}" 吗？相关点歌记录也会删除。`, '删除确认', {
      confirmButtonText: '删除',
      cancelButtonText: '取消',
      type: 'warning'
    });
    await adminApi.delete(`/admin/sessions/${row.id}`, getAuthConfig());
    ElMessage.success('删除成功');
    await fetchSessions();
  } catch (e: any) {
    if (e !== 'cancel') {
      ElMessage.error('删除失败: ' + (e.response?.data?.error || e.message));
    }
  }
};

// --- Methods: Actions (Song Requests) ---

const applySongFilters = async () => {
  songPage.value = 1;
  await fetchSongRequests();
};

const openCreateSong = () => {
  songFormMode.value = 'create';
  songForm.value = {
    id: 0,
    sessionId: '',
    roomId: '',
    userName: '',
    uid: '',
    songName: '',
    singer: '',
    createdAt: ''
  };
  songDialogVisible.value = true;
};

const openEditSong = (row: AdminSongRequest) => {
  songFormMode.value = 'edit';
  songForm.value = {
    id: row.id,
    sessionId: row.sessionId ? String(row.sessionId) : '',
    roomId: row.roomId || '',
    userName: row.userName || '',
    uid: row.uid || '',
    songName: row.songName || '',
    singer: row.singer || '',
    createdAt: row.createdAt ? String(row.createdAt) : ''
  };
  songDialogVisible.value = true;
};

const saveSongRequest = async () => {
  const payload = {
    sessionId: songForm.value.sessionId ? Number(songForm.value.sessionId) : null,
    roomId: songForm.value.roomId || null,
    userName: songForm.value.userName || null,
    uid: songForm.value.uid || null,
    songName: songForm.value.songName || null,
    singer: songForm.value.singer || null,
    createdAt: songForm.value.createdAt ? Number(songForm.value.createdAt) : null
  };

  try {
    if (songFormMode.value === 'create') {
      await adminApi.post('/admin/song-requests', payload, getAuthConfig());
      ElMessage.success('点歌记录已添加');
    } else {
      await adminApi.put(`/admin/song-requests/${songForm.value.id}`, payload, getAuthConfig());
      ElMessage.success('点歌记录已更新');
    }
    songDialogVisible.value = false;
    await fetchSongRequests();
  } catch (e: any) {
    ElMessage.error('保存失败: ' + (e.response?.data?.error || e.message));
  }
};

const deleteSongRequest = async (row: AdminSongRequest) => {
  try {
    await ElMessageBox.confirm(`确定要删除点歌记录 "${row.songName || row.id}" 吗？`, '删除确认', {
      confirmButtonText: '删除',
      cancelButtonText: '取消',
      type: 'warning'
    });
    await adminApi.delete(`/admin/song-requests/${row.id}`, getAuthConfig());
    ElMessage.success('删除成功');
    await fetchSongRequests();
  } catch (e: any) {
    if (e !== 'cancel') {
      ElMessage.error('删除失败: ' + (e.response?.data?.error || e.message));
    }
  }
};

// --- Helpers ---

const formatUptime = (val: number | string) => {
  if (!val) return '-';
  if (typeof val === 'string') return val;
  
  const ms = val;
  const seconds = Math.floor((Date.now() - ms) / 1000);
  if (seconds < 60) return `${seconds}秒`;
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}分`;
  const hours = Math.floor(minutes / 60);
  return `${hours}小时`;
};

const formatLiveDuration = (liveStatus: number, liveStartTime: number | null) => {
  if (liveStatus !== 1) return '未开播';
  if (!liveStartTime || liveStartTime === 0) return '直播中';
  const now = Date.now();
  const startMs = liveStartTime < 1_000_000_000_000 ? liveStartTime * 1000 : liveStartTime;
  
  // 增加日志辅助调试
  // console.log(`LiveStartTime: ${liveStartTime}, StartMs: ${startMs}, Now: ${now}, Diff: ${now - startMs}`);
  
  const diffSeconds = Math.floor((now - startMs) / 1000);
  if (diffSeconds < 0) return '直播中'; // 如果时间还没到（本地时差），显示直播中而非未开播
  const seconds = diffSeconds % 60;
  const totalMinutes = Math.floor(diffSeconds / 60);
  const minutes = totalMinutes % 60;
  const totalHours = Math.floor(totalMinutes / 60);
  const hours = totalHours % 24;
  const days = Math.floor(totalHours / 24);
  if (days > 0) return `${days}天${hours}小时${minutes}分${seconds}秒`;
  if (totalHours > 0) return `${totalHours}小时${minutes}分${seconds}秒`;
  if (totalMinutes > 0) return `${totalMinutes}分${seconds}秒`;
  return `${seconds}秒`;
};

const getMonitorStatusLabel = (status: string) => {
  if (status === 'stopped') return '已停止';
  if (status === 'errored') return '异常';
  return '正常';
};

const getMonitorStatusType = (status: string) => {
  if (status === 'stopped') return 'info';
  if (status === 'errored') return 'danger';
  return 'success';
};

const formatTimestamp = (value?: number) => {
  if (!value) return '-';
  const ts = value < 1_000_000_000_000 ? value * 1000 : value;
  return new Date(ts).toLocaleString('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false
  }).replace(/\//g, '-');
};

// --- Changelog Methods ---

const loadChangelogList = async () => {
  changelogLoading.value = true;
  try {
    changelogList.value = await getAdminChangelog();
  } catch (e: any) {
    ElMessage.error('加载更新日志失败: ' + (e.response?.data?.error || e.message));
  } finally {
    changelogLoading.value = false;
  }
};

const openAddChangelog = () => {
  changelogIsEdit.value = false;
  changelogForm.value = { id: 0, version: '', date: new Date().toISOString().slice(0, 10), content: '' };
  changelogDialogVisible.value = true;
};

const openEditChangelog = (row: ChangelogEntry) => {
  changelogIsEdit.value = true;
  changelogForm.value = { id: row.id, version: row.version, date: row.date.slice(0, 10), content: row.content };
  changelogDialogVisible.value = true;
};

const saveChangelog = async () => {
  if (!changelogForm.value.version || !changelogForm.value.content) {
    ElMessage.warning('版本号和内容不能为空');
    return;
  }
  try {
    if (changelogIsEdit.value) {
      await updateChangelog(changelogForm.value.id, changelogForm.value.version, changelogForm.value.date, changelogForm.value.content);
      ElMessage.success('更新成功');
    } else {
      await addChangelog(changelogForm.value.version, changelogForm.value.date, changelogForm.value.content);
      ElMessage.success('添加成功');
    }
    changelogDialogVisible.value = false;
    await loadChangelogList();
  } catch (e: any) {
    ElMessage.error('操作失败: ' + (e.response?.data?.error || e.message));
  }
};

const deleteChangelogEntry = async (row: ChangelogEntry) => {
  await ElMessageBox.confirm(`确定要删除 ${row.version} 的更新日志吗？`, '删除确认', { type: 'warning' });
  try {
    await deleteChangelog(row.id);
    ElMessage.success('删除成功');
    await loadChangelogList();
  } catch (e: any) {
    ElMessage.error('删除失败: ' + (e.response?.data?.error || e.message));
  }
};

// --- Lifecycle ---

onMounted(() => {
  window.addEventListener('resize', handleResize);
  if (token.value) {
    checkAuth();
  }
});

onUnmounted(() => {
  window.removeEventListener('resize', handleResize);
});

watch(activeSection, async (val) => {
  if (val === 'sessions' || val === 'songRequests' || val === 'accounts' || val === 'changelog') {
    await refreshDatabaseData();
  }
});
</script>

<template>
  <div class="admin-wrapper" :data-theme="isDarkMode ? 'dark' : 'light'">
    <!-- Login Screen -->
    <div v-if="!isAuthenticated" class="login-page">
      <div class="login-box">
        <div class="login-header">
          <h2>录制管理后台</h2>
          <p>管理员登录</p>
        </div>
        <div class="login-form">
          <el-input 
            v-model="token" 
            type="password" 
            placeholder="请输入管理员 Token"
            show-password
            size="large"
            @keyup.enter="checkAuth"
          />
          <el-button 
            type="primary" 
            size="large" 
            @click="checkAuth" 
            :loading="loading"
            class="login-btn"
          >
            进入后台
          </el-button>
        </div>
        <p v-if="error" class="error-text">{{ error }}</p>
      </div>
    </div>

    <!-- Main Dashboard -->
    <el-container v-else class="main-container">
      <el-header class="top-header">
        <div class="header-left">
          <div v-if="isMobile" class="mobile-toggle" @click="sidebarCollapsed = !sidebarCollapsed">
            <el-icon :size="20"><Expand v-if="sidebarCollapsed" /><Fold v-else /></el-icon>
          </div>
          <div class="logo-area">
            <h1 class="system-title">录制管理后台</h1>
          </div>
        </div>
        <div class="header-right">
          <el-button
            :icon="House"
            @click="goHome"
          >
            <span v-if="!isMobile">回到主页</span>
          </el-button>
          <el-button
            :icon="Refresh"
            @click="refreshCurrentSection"
            :loading="isRefreshing"
          >
            <span v-if="!isMobile">刷新状态</span>
          </el-button>
          <el-button 
            type="danger" 
            :icon="SwitchButton" 
            @click="logout"
          >
            <span v-if="!isMobile">退出登录</span>
          </el-button>
        </div>
      </el-header>
      
      <el-container class="content-container">
        <!-- Sidebar -->
        <el-aside 
          :width="sidebarCollapsed ? (isMobile ? '0' : '64px') : '220px'" 
          class="sidebar-container"
          :class="{ 'collapsed-mobile': isMobile && sidebarCollapsed }"
        >
          <div v-if="!isMobile" class="sidebar-toggle" @click="sidebarCollapsed = !sidebarCollapsed">
             <el-icon :size="20" class="toggle-icon">
               <component :is="sidebarCollapsed ? Expand : Fold" />
             </el-icon>
          </div>
          
          <el-menu
            :default-active="activeSection"
            class="sidebar-menu"
            :collapse="sidebarCollapsed"
            @select="handleMenuSelect"
            :collapse-transition="false"
            unique-opened
          >
            <el-menu-item index="monitor">
              <el-icon><VideoCamera /></el-icon>
              <template #title>监控录制管理</template>
            </el-menu-item>
            
            <el-menu-item index="accounts">
              <el-icon><User /></el-icon>
              <template #title>账户管理</template>
            </el-menu-item>
            <el-menu-item index="changelog">
              <el-icon><Document /></el-icon>
              <template #title>更新日志</template>
            </el-menu-item>
            <el-menu-item index="logs">
              <el-icon><DataLine /></el-icon>
              <template #title>系统日志</template>
            </el-menu-item>
            <el-sub-menu index="database">
              <template #title>
                <el-icon><DataLine /></el-icon>
                <span>数据库管理</span>
              </template>
              <el-menu-item index="sessions">直播回放</el-menu-item>
              <el-menu-item index="songRequests">点歌记录</el-menu-item>
            </el-sub-menu>
          </el-menu>

          <!-- Theme Toggle (Bottom Left) -->
          <div class="theme-toggle-area" @click="toggleTheme">
            <el-icon :size="16">
              <component :is="isDarkMode ? 'Sunny' : 'Moon'" />
            </el-icon>
            <span v-if="!sidebarCollapsed" class="theme-label">
              {{ isDarkMode ? '浅色模式' : '深色模式' }}
            </span>
          </div>
        </el-aside>

        <!-- Sidebar Overlay (Mobile Only) -->
        <div 
          v-if="isMobile && !sidebarCollapsed" 
          class="sidebar-overlay" 
          @click="sidebarCollapsed = true"
        ></div>

        <!-- Main Content -->
        <el-main class="main-content" @click="isMobile && !sidebarCollapsed ? sidebarCollapsed = true : null">
          <div class="breadcrumb-bar">
            <el-breadcrumb separator="/">
              <el-breadcrumb-item v-for="(item, index) in breadcrumbs" :key="index">
                {{ item.name }}
              </el-breadcrumb-item>
            </el-breadcrumb>
          </div>

          <div class="content-area">
            <!-- Monitor Section -->
            <template v-if="activeSection === 'monitor'">
              <div class="health-check-panel" v-loading="healthCheckLoading">
                <div class="health-check-header">
                  <div>
                    <div class="health-check-title">健康巡检</div>
                    <div class="health-check-meta">最近巡检：{{ formatHealthCheckTime(healthCheckReport?.checkedAt) }}</div>
                  </div>
                  <el-tag :type="healthIssueCount > 0 ? 'danger' : 'success'" effect="dark">
                    {{ healthIssueCount > 0 ? `异常 ${healthIssueCount}` : '正常' }}
                  </el-tag>
                </div>

                <div class="health-check-stats">
                  <div class="health-check-stat">
                    <span class="label">录制器</span>
                    <strong>{{ healthCheckReport?.recorderCount ?? 0 }}</strong>
                  </div>
                  <div class="health-check-stat">
                    <span class="label">健康</span>
                    <strong>{{ healthCheckReport?.healthyCount ?? 0 }}</strong>
                  </div>
                  <div class="health-check-stat">
                    <span class="label">心跳异常</span>
                    <strong>{{ healthCheckReport?.staleHeartbeats.length ?? 0 }}</strong>
                  </div>
                  <div class="health-check-stat">
                    <span class="label">状态漂移</span>
                    <strong>{{ healthCheckReport?.driftIssues.length ?? 0 }}</strong>
                  </div>
                </div>

                <div v-if="healthIssueCount > 0" class="health-check-issues">
                  <div v-for="issue in healthCheckReport?.staleHeartbeats ?? []" :key="`stale-${issue.uid}-${issue.roomId}-${issue.reason}`" class="health-issue-item danger">
                    心跳异常：UID {{ issue.uid }} / 房间 {{ issue.roomId }} / {{ issue.reason }}<span v-if="issue.ageSeconds != null"> / {{ issue.ageSeconds.toFixed(1) }}s</span>
                  </div>
                  <div v-for="issue in healthCheckReport?.driftIssues ?? []" :key="`drift-${issue.uid}-${issue.roomId}-${issue.reason}`" class="health-issue-item warning">
                    状态漂移：UID {{ issue.uid }} / 房间 {{ issue.roomId }} / {{ issue.reason }}
                  </div>
                </div>
              </div>

              <div class="search-section" :class="{ 'is-mobile': isMobile }">
                <div v-if="isMobile" class="mobile-search-toggle" @click="searchCollapsed = !searchCollapsed">
                  <div class="toggle-content">
                    <el-icon><Plus /></el-icon>
                    <span>添加主播与批量操作</span>
                  </div>
                  <el-icon :class="{ 'is-rotated': !searchCollapsed }"><ArrowDown /></el-icon>
                </div>
                
                <div class="search-form" v-show="!isMobile || !searchCollapsed">
                  <el-input 
                    v-model="newRoom.uid" 
                    placeholder="请输入 UID" 
                    clearable
                  />
                  <el-input 
                    v-model="newRoom.remark" 
                    placeholder="备注 (可选)" 
                    clearable
                  />
                  <el-input 
                    v-model="newRoom.playlistUrl" 
                    placeholder="歌单链接 (可选)" 
                    clearable
                  />
                  <el-button 
                    type="primary" 
                    :icon="Plus" 
                    @click="addRoom" 
                    :loading="adding"
                  >
                    通过 UID 添加
                  </el-button>
                  <el-button 
                    type="danger" 
                    :icon="Delete" 
                    @click="batchDeleteRooms" 
                    :disabled="selectedRooms.length === 0"
                  >
                    批量删除
                  </el-button>
                  <el-button 
                    type="warning" 
                    :icon="VideoPlay" 
                    @click="batchRestartRooms" 
                    :disabled="selectedRooms.length === 0"
                  >
                    批量重启
                  </el-button>
                </div>
              </div>

              <div class="table-section">
                <el-table 
                  :data="rooms" 
                  style="width: 100%" 
                  v-loading="loading"
                  border
                  stripe
                  @selection-change="handleRoomSelectionChange"
                >
                  <el-table-column type="selection" width="55" align="center" />
                  <el-table-column label="主播" align="center" show-overflow-tooltip>
                    <template #default="scope">
                      {{ scope.row.remark || scope.row.name }}
                    </template>
                  </el-table-column>
                  <el-table-column prop="uid" label="UID" min-width="130" align="center" show-overflow-tooltip />
                  <el-table-column prop="room_id" label="房间号" align="center" />
                  <el-table-column label="歌单" min-width="220" align="center" show-overflow-tooltip>
                    <template #default="scope">
                      <a
                        v-if="scope.row.playlistUrl"
                        :href="scope.row.playlistUrl"
                        target="_blank"
                        rel="noopener noreferrer"
                      >
                        {{ scope.row.playlistUrl }}
                      </a>
                      <span v-else>-</span>
                    </template>
                  </el-table-column>
                  <el-table-column label="开启监控" width="100" align="center">
                    <template #default="scope">
                      <el-switch
                        v-model="scope.row.auto_record"
                        :active-value="1"
                        :inactive-value="0"
                        @change="(val: number) => toggleRoomMonitor(scope.row, !!val)"
                      />
                    </template>
                  </el-table-column>
                  <el-table-column label="监控状态" width="100" align="center">
                    <template #default="scope">
                      <el-popover
                        placement="top"
                        :width="150"
                        trigger="click"
                      >
                        <template #reference>
                          <el-tag 
                            :type="getMonitorStatusType(scope.row.process_status)"
                            effect="dark"
                            size="small"
                            style="cursor: pointer"
                          >
                            {{ getMonitorStatusLabel(scope.row.process_status) }}
                          </el-tag>
                        </template>
                        <div style="text-align: center">
                          <p style="margin: 0; font-weight: bold; font-size: 12px; color: #909399; margin-bottom: 4px;">运行时长</p>
                          <p style="margin: 0; font-size: 14px;">{{ formatUptime(scope.row.process_uptime) }}</p>
                        </div>
                      </el-popover>
                    </template>
                  </el-table-column>
                  <el-table-column label="开播时长" align="center">
                    <template #default="scope">
                      {{ formatLiveDuration(scope.row.live_status, scope.row.live_start_time) }}
                    </template>
                  </el-table-column>
                  <el-table-column label="操作" width="280" align="center">
                    <template #default="scope">
                      <div class="action-btns">
                        <el-button 
                          type="primary" 
                          size="small" 
                          :icon="EditPen" 
                          @click="openEditRoom(scope.row)"
                        >
                          修改
                        </el-button>
                        <el-button 
                          type="primary" 
                          size="small" 
                          :icon="VideoPlay" 
                          @click="restartRoom(scope.row.id)"
                        >
                          重启
                        </el-button>
                        <el-button 
                          type="danger" 
                          size="small" 
                          :icon="Delete" 
                          @click="deleteRoom(scope.row.id, scope.row.name)"
                        >
                          删除
                        </el-button>
                      </div>
                    </template>
                  </el-table-column>
                </el-table>
              </div>
            </template>

            <!-- Sessions Section -->
            <template v-else-if="activeSection === 'sessions'">
              <div class="search-section" :class="{ 'is-mobile': isMobile }">
                <div v-if="isMobile" class="mobile-search-toggle" @click="searchCollapsed = !searchCollapsed">
                  <div class="toggle-content">
                    <el-icon><DataLine /></el-icon>
                    <span>筛选与管理</span>
                  </div>
                  <el-icon :class="{ 'is-rotated': !searchCollapsed }"><ArrowDown /></el-icon>
                </div>

                <div class="search-form" v-show="!isMobile || !searchCollapsed">
                  <el-select 
                    v-model="sessionFilterRoomId" 
                    placeholder="选择主播/房间" 
                    clearable 
                    filterable
                    @change="applySessionFilters"
                  >
                    <el-option
                      v-for="room in rooms"
                      :key="room.room_id"
                      :label="room.name + ' (UID: ' + room.uid + ' / 房间: ' + room.room_id + ')'"
                      :value="String(room.room_id)"
                    />
                  </el-select>
                  <el-input 
                    v-model="sessionSearch" 
                    placeholder="搜索直播标题"
                    clearable
                    @keyup.enter="applySessionFilters"
                  />
                  <el-button 
                    type="primary" 
                    :icon="Plus" 
                    @click="openCreateSession"
                  >
                    新增
                  </el-button>
                  <el-button 
                    type="danger" 
                    :icon="Delete" 
                    @click="batchDeleteSessions"
                    :disabled="selectedSessions.length === 0"
                  >
                    批量删除
                  </el-button>
                  <el-button
                    type="warning"
                    :icon="Refresh"
                    @click="batchRecalculateSessions"
                    :loading="sessionRecalcLoading"
                    :disabled="selectedSessions.length === 0"
                  >
                    重新统计
                  </el-button>
                </div>
              </div>

              <div class="table-section">
                <el-table 
                  :data="sessions" 
                  style="width: 100%" 
                  v-loading="sessionLoading"
                  border
                  stripe
                  @selection-change="handleSessionSelectionChange"
                >
                  <el-table-column type="selection" width="55" align="center" />
                  <el-table-column prop="id" label="ID" width="80" align="center" />
                  <el-table-column prop="userName" label="主播" align="center" />
                  <el-table-column prop="roomId" label="房间号" align="center" />
                  <el-table-column prop="title" label="标题" align="center" show-overflow-tooltip />
                  <el-table-column label="开始时间" align="center" width="160">
                    <template #default="scope">
                      {{ formatTimestamp(scope.row.startTime) }}
                    </template>
                  </el-table-column>
                  <el-table-column label="结束时间" align="center" width="160">
                    <template #default="scope">
                      {{ formatTimestamp(scope.row.endTime) }}
                    </template>
                  </el-table-column>
                  <el-table-column label="操作" width="180" align="center">
                    <template #default="scope">
                      <div class="action-btns">
                        <el-button 
                          size="small" 
                          :icon="EditPen" 
                          @click="openEditSession(scope.row)"
                        >
                          编辑
                        </el-button>
                        <el-button 
                          type="danger" 
                          size="small" 
                          :icon="Delete" 
                          @click="deleteSession(scope.row)"
                        >
                          删除
                        </el-button>
                      </div>
                    </template>
                  </el-table-column>
                </el-table>

                <div class="pagination-row">
                  <el-pagination
                    :current-page="sessionPage"
                    :page-size="sessionPageSize"
                    :total="sessionTotal"
                    :page-sizes="[10, 20, 50, 100]"
                    :layout="isMobile ? 'prev, pager, next' : 'prev, pager, next, sizes, total'"
                    @size-change="(val: number) => { sessionPageSize = val; sessionPage = 1; fetchSessions(); }"
                    @current-change="(val: number) => { sessionPage = val; fetchSessions(); }"
                  />
                </div>
              </div>
            </template>

            <!-- Song Requests Section -->
            <template v-else-if="activeSection === 'songRequests'">
              <div class="search-section" :class="{ 'is-mobile': isMobile }">
                <div v-if="isMobile" class="mobile-search-toggle" @click="searchCollapsed = !searchCollapsed">
                  <div class="toggle-content">
                    <el-icon><Refresh /></el-icon>
                    <span>筛选与管理</span>
                  </div>
                  <el-icon :class="{ 'is-rotated': !searchCollapsed }"><ArrowDown /></el-icon>
                </div>

                <div class="search-form" v-show="!isMobile || !searchCollapsed">
                  <el-select 
                    v-model="songFilterRoomId" 
                    placeholder="选择主播/房间" 
                    clearable 
                    filterable
                    @change="applySongFilters"
                  >
                    <el-option
                      v-for="room in rooms"
                      :key="room.room_id"
                      :label="room.name + ' (UID: ' + room.uid + ' / 房间: ' + room.room_id + ')'"
                      :value="String(room.room_id)"
                    />
                  </el-select>
                  <el-input 
                    v-model="songFilterUserName" 
                    placeholder="点歌用户"
                    clearable
                    @keyup.enter="applySongFilters"
                  />
                  <el-input 
                    v-model="songSearch" 
                    placeholder="搜索歌曲/歌手"
                    clearable
                    @keyup.enter="applySongFilters"
                  />
                  <el-button 
                    type="primary" 
                    :icon="Plus" 
                    @click="openCreateSong"
                  >
                    新增
                  </el-button>
                  <el-button 
                    type="danger" 
                    :icon="Delete" 
                    @click="batchDeleteSongRequests"
                    :disabled="selectedSongRequests.length === 0"
                  >
                    批量删除
                  </el-button>
                </div>
              </div>

              <div class="table-section">
                <el-table 
                  :data="songRequests" 
                  style="width: 100%" 
                  v-loading="songLoading"
                  border
                  stripe
                  @selection-change="handleSongSelectionChange"
                >
                  <el-table-column type="selection" width="55" align="center" />
                  <el-table-column prop="id" label="ID" width="80" align="center" />
                  <el-table-column prop="userName" label="点歌用户" align="center" />
                  <el-table-column prop="songName" label="歌曲" align="center" show-overflow-tooltip />
                  <el-table-column prop="singer" label="歌手" align="center" />
                  <el-table-column prop="roomId" label="房间号" align="center" />
                  <el-table-column label="时间" align="center" width="160">
                    <template #default="scope">
                      {{ formatTimestamp(scope.row.createdAt) }}
                    </template>
                  </el-table-column>
                  <el-table-column label="操作" width="180" align="center">
                    <template #default="scope">
                      <div class="action-btns">
                        <el-button 
                          size="small" 
                          :icon="EditPen" 
                          @click="openEditSong(scope.row)"
                        >
                          编辑
                        </el-button>
                        <el-button 
                          type="danger" 
                          size="small" 
                          :icon="Delete" 
                          @click="deleteSongRequest(scope.row)"
                        >
                          删除
                        </el-button>
                      </div>
                    </template>
                  </el-table-column>
                </el-table>

                <div class="pagination-row">
                  <el-pagination
                    :current-page="songPage"
                    :page-size="songPageSize"
                    :total="songTotal"
                    :page-sizes="[10, 20, 50, 100]"
                    :layout="isMobile ? 'prev, pager, next' : 'prev, pager, next, sizes, total'"
                    @size-change="(val: number) => { songPageSize = val; songPage = 1; fetchSongRequests(); }"
                    @current-change="(val: number) => { songPage = val; fetchSongRequests(); }"
                  />
                </div>
              </div>
            </template>

            <!-- Accounts Section -->
            <template v-else-if="activeSection === 'accounts'">
              <div class="account-section">
                <div class="account-toolbar">
                  <el-button type="primary" :icon="Plus" @click="openQrLogin">扫码登录</el-button>
                  <el-button @click="openImportCookie">导入 Cookie</el-button>
                  <el-button :icon="Refresh" @click="fetchAccounts">刷新</el-button>
                </div>
                <div class="table-section">
                  <el-table
                    :data="biliAccounts"
                    style="width: 100%"
                    v-loading="accountLoading"
                    border
                    stripe
                  >
                    <el-table-column prop="name" label="名称" min-width="100" align="center" show-overflow-tooltip>
                      <template #default="scope">
                        {{ scope.row.name || '未知用户' }}
                      </template>
                    </el-table-column>
                    <el-table-column prop="uid" label="UID" min-width="120" align="center" show-overflow-tooltip />
                    <el-table-column label="状态" min-width="80" align="center">
                      <template #default="scope">
                        <el-tag
                          :type="scope.row.is_active ? 'success' : 'info'"
                          effect="dark"
                          size="small"
                        >
                          {{ scope.row.is_active ? '使用中' : '未激活' }}
                        </el-tag>
                      </template>
                    </el-table-column>
                    <el-table-column label="过期时间" align="center" min-width="130">
                      <template #default="scope">
                        <span v-if="scope.row.expires_at" :class="{ 'expire-warn': isExpiringSoon(scope.row.expires_at) }">
                          {{ formatDate(scope.row.expires_at) }}
                        </span>
                        <span v-else>-</span>
                      </template>
                    </el-table-column>
                    <el-table-column label="录制房间" align="center" min-width="120">
                      <template #default="scope">
                        <div v-if="getAccountRooms(scope.row.uid).length === 0" style="color: #909399; font-size: 12px;">-</div>
                        <div v-else>
                          <el-button
                            size="small"
                            link
                            type="primary"
                            @click="openAccountRoomsDialog(scope.row.uid, scope.row.name || '')"
                          >
                            {{ getAccountRooms(scope.row.uid).filter(r => r.is_recording).length }} 录制中
                            / {{ getAccountRooms(scope.row.uid).length }} 个房间
                          </el-button>
                        </div>
                      </template>
                    </el-table-column>
                    <el-table-column label="操作" align="center" min-width="280">
                      <template #default="scope">
                        <div class="action-btns">
                          <el-button v-if="!scope.row.is_active" size="small" @click="activateAccount(scope.row.uid)">使用</el-button>
                          <el-button size="small" :icon="Refresh" @click="refreshAccountInfo(scope.row.uid)">刷新信息</el-button>
                          <el-button size="small" type="warning" @click="refreshAccountAuth(scope.row.uid)">更新授权</el-button>
                          <el-button size="small" type="danger" :icon="Delete" @click="removeAccount(scope.row.uid)">删除</el-button>
                        </div>
                      </template>
                    </el-table-column>
                  </el-table>
                </div>
              </div>
            </template>

            <!-- 更新日志管理 -->
            <template v-else-if="activeSection === 'changelog'">
              <div class="section-card">
                <div class="section-header">
                  <h3>更新日志</h3>
                  <el-button type="primary" @click="openAddChangelog">
                    <el-icon><Plus /></el-icon> 添加版本
                  </el-button>
                </div>
                <el-table :data="changelogList" v-loading="changelogLoading" stripe>
                  <el-table-column prop="version" label="版本" width="120" />
                  <el-table-column prop="date" label="日期" width="140">
                    <template #default="{ row }">
                      {{ row.date ? row.date.slice(0, 10) : '' }}
                    </template>
                  </el-table-column>
                  <el-table-column prop="content" label="内容" min-width="300">
                    <template #default="{ row }">
                      <div style="white-space: pre-line; max-height: 80px; overflow: hidden; text-overflow: ellipsis;">{{ row.content }}</div>
                    </template>
                  </el-table-column>
                  <el-table-column label="操作" width="160" fixed="right">
                    <template #default="{ row }">
                      <el-button size="small" @click="openEditChangelog(row)">编辑</el-button>
                      <el-button size="small" type="danger" @click="deleteChangelogEntry(row)">删除</el-button>
                    </template>
                  </el-table-column>
                </el-table>
              </div>
            </template>

            <template v-else-if="activeSection === 'logs'">
              <div class="logs-fullscreen">
                <LogViewer />
              </div>
            </template>
          </div>
        </el-main>
      </el-container>
    </el-container>

    <!-- Dialogs -->
    <el-dialog v-model="editDialogVisible" title="修改主播配置" width="400px">
      <div class="dialog-form">
        <el-input v-model="editForm.remark" placeholder="备注" />
        <el-input v-model="editForm.playlistUrl" placeholder="歌单链接" />
      </div>
      <template #footer>
        <el-button @click="editDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="saveRoomEdit">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="sessionDialogVisible" :title="sessionFormMode === 'create' ? '新增直播回放' : '编辑直播回放'" width="520px">
      <div class="dialog-form">
        <el-input v-model="sessionForm.title" placeholder="直播标题" />
        <el-input v-model="sessionForm.userName" placeholder="主播名称" />
        <el-input v-model="sessionForm.roomId" placeholder="房间号" />
        <el-input v-model="sessionForm.startTime" placeholder="开始时间戳 (毫秒)" type="number" />
        <el-input v-model="sessionForm.endTime" placeholder="结束时间戳 (毫秒)" type="number" />
        <el-input v-model="sessionForm.filePath" placeholder="文件路径 (可选)" />
      </div>
      <template #footer>
        <el-button @click="sessionDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="saveSession">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="songDialogVisible" :title="songFormMode === 'create' ? '新增点歌记录' : '编辑点歌记录'" width="520px">
      <div class="dialog-form">
        <el-input v-model="songForm.songName" placeholder="歌曲名称" />
        <el-input v-model="songForm.singer" placeholder="歌手" />
        <el-input v-model="songForm.userName" placeholder="点歌用户" />
        <el-input v-model="songForm.uid" placeholder="UID" />
        <el-input v-model="songForm.roomId" placeholder="房间号" />
        <el-input v-model="songForm.sessionId" placeholder="场次 ID" type="number" />
        <el-input v-model="songForm.createdAt" placeholder="时间戳 (毫秒)" type="number" />
      </div>
      <template #footer>
        <el-button @click="songDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="saveSongRequest">保存</el-button>
      </template>
    </el-dialog>

    <!-- QR Login Dialog -->
    <el-dialog v-model="qrDialogVisible" title="Bilibili 扫码登录" width="360px" :close-on-click-modal="false" @close="closeQrLogin">
      <div class="qr-login-body">
        <div v-if="qrUrl" class="qr-wrapper">
          <img :src="`https://api.qrserver.com/v1/create-qr-code/?size=200x200&data=${encodeURIComponent(qrUrl)}`" alt="QR Code" />
          <p class="qr-tip">请使用 Bilibili App 扫码登录</p>
        </div>
        <div v-else class="qr-loading">
          <el-icon class="is-loading"><Refresh /></el-icon>
          <span>正在获取二维码...</span>
        </div>
        <p v-if="qrPolling" class="qr-status">等待扫码...</p>
      </div>
    </el-dialog>

    <!-- Import Cookie Dialog -->
    <el-dialog v-model="importCookieDialogVisible" title="导入 Cookie" width="480px">
      <div class="dialog-form">
        <el-input v-model="importCookieForm.uid" placeholder="UID" type="number" />
        <el-input v-model="importCookieForm.cookie" type="textarea" :rows="4" placeholder="粘贴完整的 Cookie 字符串，例如: SESSDATA=xxx; bili_jct=yyy; DedeUserID=zzz" />
      </div>
      <template #footer>
        <el-button @click="importCookieDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="submitImportCookie">导入</el-button>
      </template>
    </el-dialog>

    <!-- Account Rooms Management Dialog -->
    <el-dialog
      v-model="accountRoomsDialogVisible"
      :title="currentAccountName + ' 的录制房间'"
      width="600px"
    >
      <div v-if="currentAccountRooms.length === 0" style="text-align: center; color: #909399; padding: 20px;">
        暂无录制房间
      </div>
      <el-table
        v-else
        :data="currentAccountRooms"
        style="width: 100%"
        border
        stripe
        size="small"
      >
        <el-table-column prop="room_name" label="房间" align="center" show-overflow-tooltip>
          <template #default="scope">
            {{ scope.row.room_name || scope.row.room_uid }}
          </template>
        </el-table-column>
        <el-table-column prop="room_id" label="房间号" width="100" align="center" />
        <el-table-column label="状态" width="90" align="center">
          <template #default="scope">
            <el-tag
              :type="scope.row.is_recording ? 'success' : 'info'"
              effect="dark"
              size="small"
            >
              {{ scope.row.is_recording ? '录制中' : '已分配' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="100" align="center">
          <template #default="scope">
            <el-button size="small" link type="primary" @click="openReassignDialog(scope.row)">
              移动
            </el-button>
          </template>
        </el-table-column>
      </el-table>
      <div v-if="currentAccountRooms.length > 0" style="margin-top: 12px; color: #909399; font-size: 12px; text-align: right;">
        共 {{ currentAccountRooms.length }} 个房间，{{ currentAccountRooms.filter(r => r.is_recording).length }} 个录制中
      </div>
    </el-dialog>

    <!-- Reassign Room Dialog -->
    <el-dialog v-model="reassignDialogVisible" title="移动录制房间" width="400px">
      <div class="dialog-form">
        <p style="margin-bottom: 12px; color: #606266;">
          房间: <strong>{{ reassignForm.roomName }}</strong>
        </p>
        <el-select v-model="reassignForm.targetUid" placeholder="选择目标账户" style="width: 100%">
          <el-option
            v-for="acc in biliAccounts.filter(a => a.uid !== currentAccountUid)"
            :key="acc.uid"
            :label="(acc.name || '未知用户') + ' (UID: ' + acc.uid + ')'"
            :value="acc.uid"
          />
        </el-select>
      </div>
      <template #footer>
        <el-button @click="reassignDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="submitReassign">移动</el-button>
      </template>
    </el-dialog>

    <!-- 更新日志编辑弹窗 -->
    <el-dialog v-model="changelogDialogVisible" :title="changelogIsEdit ? '编辑更新日志' : '添加更新日志'" width="600px" append-to-body>
      <el-form label-width="80px">
        <el-form-item label="版本号">
          <el-input v-model="changelogForm.version" placeholder="例如: v3.1" />
        </el-form-item>
        <el-form-item label="日期">
          <el-input v-model="changelogForm.date" placeholder="2026-05-02" />
        </el-form-item>
        <el-form-item label="内容">
          <el-input v-model="changelogForm.content" type="textarea" :rows="8" placeholder="每行一条更新，例如：&#10;虚拟滚动：引入 @tanstack/vue-virtual&#10;shallowRef 优化：消除大数组深度响应式" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="changelogDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="saveChangelog" :loading="changelogLoading">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.admin-wrapper {
  height: 100vh;
  width: 100vw;
  background: #f5f7fa;
  overflow: hidden;
}

/* Login Page Styles */
.login-page {
  height: 100%;
  display: flex;
  align-items: center;
  justify-content: center;
  background: #f5f5f5;
}

.login-box {
  background: #fff;
  padding: 40px;
  border-radius: 8px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
  width: 100%;
  max-width: 400px;
}

.login-header {
  text-align: center;
  margin-bottom: 30px;
}

.login-header h2 {
  margin: 0 0 8px 0;
  color: #333;
  font-size: 24px;
}

.login-header p {
  margin: 0;
  color: #999;
  font-size: 14px;
}

.login-form {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.login-btn {
  width: 100%;
}

.error-text {
  color: #f56c6c;
  text-align: center;
  margin-top: 16px;
  font-size: 14px;
}

/* Main Layout Styles */
.main-container {
  height: 100%;
  display: flex;
  flex-direction: column;
}

.top-header {
  height: 60px;
  background: #fff;
  border-bottom: 1px solid #dcdfe6;
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 20px;
  flex-shrink: 0;
}

.system-title {
  font-size: 18px;
  font-weight: 600;
  color: #303133;
  margin: 0;
}

.header-right {
  display: flex;
  gap: 12px;
}

.content-container {
  flex: 1;
  overflow: hidden;
  display: flex;
}

/* Sidebar Styles */
.sidebar-container {
  background-color: #fff;
  border-right: 1px solid #dcdfe6;
  display: flex;
  flex-direction: column;
  transition: width 0.3s;
  overflow: hidden;
}

.sidebar-toggle {
  height: 40px;
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  border-bottom: 1px solid #ebeef5;
  color: #606266;
}

.sidebar-toggle:hover {
  background-color: #f5f7fa;
  color: #409eff;
}

.sidebar-menu {
  border-right: none;
  overflow-y: auto;
  flex: 1;
}

.sidebar-menu:not(.el-menu--collapse) {
  width: 220px;
}

/* Main Content Styles */
.main-content {
  flex: 1;
  padding: 0;
  background-color: #f0f2f5;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.breadcrumb-bar {
  background: #fff;
  padding: 12px 20px;
  border-bottom: 1px solid #ebeef5;
  flex-shrink: 0;
}

.content-area {
  flex: 1;
  padding: 20px;
  overflow-y: auto;
}

.health-check-panel {
  background: #fff;
  padding: 18px;
  border-radius: 4px;
  margin-bottom: 16px;
}

.health-check-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 12px;
}

.health-check-title {
  font-size: 16px;
  font-weight: 700;
}

.health-check-meta {
  margin-top: 4px;
  color: #909399;
  font-size: 12px;
}

.health-check-stats {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 12px;
}

.health-check-stat {
  background: #f5f7fa;
  border-radius: 8px;
  padding: 12px;
}

.health-check-stat .label {
  display: block;
  color: #909399;
  font-size: 12px;
  margin-bottom: 6px;
}

.health-check-stat strong {
  font-size: 18px;
}

.health-check-issues {
  margin-top: 12px;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.health-issue-item {
  border-radius: 8px;
  padding: 10px 12px;
  font-size: 13px;
}

.health-issue-item.danger {
  background: rgba(245, 108, 108, 0.12);
  color: #c45656;
}

.health-issue-item.warning {
  background: rgba(230, 162, 60, 0.12);
  color: #b88230;
}

.search-section {
  background: #fff;
  padding: 18px;
  border-radius: 4px;
  margin-bottom: 16px;
  position: relative;
  z-index: 10;
}

.search-section.is-mobile {
  padding: 0;
  overflow: visible;
  margin-bottom: 12px;
  border: 1px solid #ebeef5;
}

.mobile-search-toggle {
  padding: 14px 16px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  cursor: pointer;
  background: #fff;
  border-radius: 4px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.05);
}

.mobile-search-toggle .toggle-content {
  display: flex;
  align-items: center;
  gap: 8px;
  color: #409eff;
  font-weight: 500;
  font-size: 14px;
}

.mobile-search-toggle .el-icon {
  transition: transform 0.3s;
  color: #909399;
}

.mobile-search-toggle .is-rotated {
  transform: rotate(180deg);
}

.search-form {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
}

.search-section.is-mobile .search-form {
  padding: 16px;
  background: #fafafa;
}

.search-form :deep(.el-input) {
  width: 200px;
}

@media screen and (max-width: 768px) {
  .search-form :deep(.el-input),
  .search-form :deep(.el-select) {
    width: 100% !important;
  }
  
  .search-form .el-button {
    width: 100%;
    margin-left: 0 !important;
  }
}

.table-section {
  background: #fff;
  padding: 18px;
  border-radius: 4px;
}

.action-btns {
  display: flex;
  gap: 8px;
  justify-content: center;
}

.pagination-row {
  margin-top: 20px;
  display: flex;
  justify-content: flex-end;
}

.dialog-form {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

/* Mobile Responsive Styles */
@media (max-width: 768px) {
  .sidebar-container {
    position: fixed;
    top: 60px;
    bottom: 0;
    left: 0;
    z-index: 1000;
    width: 220px !important;
    box-shadow: 4px 0 10px rgba(0,0,0,0.1);
  }

  .sidebar-container.collapsed-mobile {
    transform: translateX(-100%);
    width: 0 !important;
  }

  .sidebar-overlay {
    position: fixed;
    top: 60px;
    left: 0;
    right: 0;
    bottom: 0;
    background: rgba(0, 0, 0, 0.3);
    z-index: 999;
  }

  .main-content {
    margin-left: 0 !important;
  }

  .mobile-toggle {
    margin-right: 12px;
    cursor: pointer;
    display: flex;
    align-items: center;
    color: #606266;
  }

  .header-left {
    display: flex;
    align-items: center;
  }

  .top-header {
    padding: 0 10px;
  }
  
  .system-title {
    font-size: 16px;
  }
  
  .header-right {
    gap: 8px;
  }
  
  .header-right :deep(.el-button) {
    padding: 8px;
  }

  .content-area {
    padding: 10px;
  }

  .logs-fullscreen {
    height: calc(100vh - 90px);
    margin: -10px;
  }

  .search-section {
    padding: 12px;
  }

  .search-form {
    flex-direction: column;
    gap: 8px;
  }

  .search-form :deep(.el-input),
  .search-form :deep(.el-select),
  .search-form :deep(.el-button) {
    width: 100% !important;
    margin: 0;
  }

  .table-section {
    padding: 10px;
    overflow-x: auto;
  }

  .breadcrumb-bar {
    padding: 8px 12px;
  }

  /* Table responsive adjustments */
  :deep(.el-table) {
    font-size: 13px;
  }
  
  :deep(.el-table .cell) {
    padding-left: 5px;
    padding-right: 5px;
  }

  .action-btns {
    flex-direction: column;
    gap: 4px;
  }

  .action-btns :deep(.el-button) {
    width: 100%;
    margin-left: 0 !important;
  }
}

/* Account Section Styles */
.account-section {
  padding: 0;
}

.account-toolbar {
  display: flex;
  gap: 8px;
  margin-bottom: 16px;
  flex-wrap: wrap;
}

.expire-warn {
  color: #e6a23c;
}

.account-rooms {
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  max-width: 200px;
  margin: 0 auto;
}

.account-toolbar {
  display: flex;
  gap: 10px;
  margin-bottom: 20px;
  flex-wrap: wrap;
}

.qr-login-body {
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 10px;
}

.qr-wrapper img {
  width: 200px;
  height: 200px;
  border-radius: 4px;
}

.qr-tip {
  margin-top: 10px;
  color: #606266;
  font-size: 14px;
}

.qr-loading {
  display: flex;
  align-items: center;
  gap: 8px;
  color: #909399;
  padding: 40px;
}

.qr-status {
  margin-top: 8px;
  color: #409eff;
  font-size: 13px;
}

/* Changelog Section Styles */
.section-card {
  padding: 20px;
}

.logs-fullscreen {
  height: calc(100vh - 100px);
  margin: -20px;
}

.section-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
}

.section-header h3 {
  margin: 0;
  font-size: 16px;
  color: var(--text-primary);
}

/* Theme Toggle */
.theme-toggle-area {
  height: 48px;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  cursor: pointer;
  border-top: 1px solid #ebeef5;
  color: #606266;
  font-size: 14px;
  flex-shrink: 0;
  transition: all 0.2s;
}

.theme-toggle-area:hover {
  background-color: #f5f7fa;
  color: #409eff;
}

.theme-label {
  white-space: nowrap;
  overflow: hidden;
  transition: opacity 0.3s;
}

/* ─── Dark Theme Overrides ──────────────────────────────────────── */

.admin-wrapper[data-theme="dark"] {
  background: #0a0a0a;
}

.admin-wrapper[data-theme="dark"] .login-page {
  background: #0a0a0a;
}

.admin-wrapper[data-theme="dark"] .login-box {
  background: #1e1e1e;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.4);
}

.admin-wrapper[data-theme="dark"] .login-header h2 {
  color: #e0e0e0;
}

.admin-wrapper[data-theme="dark"] .top-header {
  background: #1e1e1e;
  border-bottom-color: #333;
}

.admin-wrapper[data-theme="dark"] .system-title {
  color: #e0e0e0;
}

.admin-wrapper[data-theme="dark"] .sidebar-container {
  background-color: #1e1e1e;
  border-right-color: #333;
}

.admin-wrapper[data-theme="dark"] .sidebar-toggle {
  border-bottom-color: #333;
  color: #a0a0a0;
}

.admin-wrapper[data-theme="dark"] .sidebar-toggle:hover {
  background-color: #2a2a2a;
}

.admin-wrapper[data-theme="dark"] .theme-toggle-area {
  border-top-color: #333;
  color: #a0a0a0;
}

.admin-wrapper[data-theme="dark"] .theme-toggle-area:hover {
  background-color: #2a2a2a;
  color: #60a5fa;
}

.admin-wrapper[data-theme="dark"] .main-content {
  background-color: #0a0a0a;
}

.admin-wrapper[data-theme="dark"] .breadcrumb-bar {
  background: #1e1e1e;
  border-bottom-color: #333;
}

.admin-wrapper[data-theme="dark"] .search-section {
  background: #1e1e1e;
  border: 1px solid #333;
}

.admin-wrapper[data-theme="dark"] .search-section.is-mobile {
  border-color: #333;
}

.admin-wrapper[data-theme="dark"] .mobile-search-toggle {
  background: #1e1e1e;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.2);
}

.admin-wrapper[data-theme="dark"] .search-section.is-mobile .search-form {
  background: #1a1a1a;
}

.admin-wrapper[data-theme="dark"] .table-section {
  background: #1e1e1e;
}

/* Element Plus overrides in dark mode */
.admin-wrapper[data-theme="dark"] :deep(.el-table) {
  background-color: #1e1e1e;
  color: #d0d0d0;
}

.admin-wrapper[data-theme="dark"] :deep(.el-table th.el-table__cell) {
  background-color: #262626;
  color: #e0e0e0;
}

.admin-wrapper[data-theme="dark"] :deep(.el-table tr) {
  background-color: #1e1e1e;
}

.admin-wrapper[data-theme="dark"] :deep(.el-table--striped .el-table__body tr.el-table__row--striped td.el-table__cell) {
  background-color: #252525;
}

.admin-wrapper[data-theme="dark"] :deep(.el-table--enable-row-hover .el-table__body tr:hover > td.el-table__cell) {
  background-color: #2a2a2a;
}

.admin-wrapper[data-theme="dark"] :deep(.el-table td.el-table__cell) {
  border-bottom-color: #333;
}

.admin-wrapper[data-theme="dark"] :deep(.el-pagination) {
  color: #d0d0d0;
}

.admin-wrapper[data-theme="dark"] :deep(.el-pagination .el-pagination__total) {
  color: #a0a0a0;
}

.admin-wrapper[data-theme="dark"] :deep(.el-input__wrapper) {
  background-color: #262626;
  box-shadow: 0 0 0 1px #444 inset;
}

.admin-wrapper[data-theme="dark"] :deep(.el-input__inner) {
  color: #e0e0e0;
}

.admin-wrapper[data-theme="dark"] :deep(.el-input__inner::placeholder) {
  color: #666;
}

.admin-wrapper[data-theme="dark"] :deep(.el-select .el-input__wrapper) {
  background-color: #262626;
  box-shadow: 0 0 0 1px #444 inset;
}

.admin-wrapper[data-theme="dark"] :deep(.el-dialog) {
  background: #1e1e1e;
}

.admin-wrapper[data-theme="dark"] :deep(.el-dialog__title) {
  color: #e0e0e0;
}

.admin-wrapper[data-theme="dark"] :deep(.el-dialog__body) {
  color: #d0d0d0;
}

.admin-wrapper[data-theme="dark"] :deep(.el-form-item__label) {
  color: #d0d0d0;
}

.admin-wrapper[data-theme="dark"] :deep(.el-tag) {
  background-color: #2a2a2a;
  border-color: #444;
  color: #d0d0d0;
}

.admin-wrapper[data-theme="dark"] :deep(.el-breadcrumb__inner) {
  color: #a0a0a0;
}

.admin-wrapper[data-theme="dark"] :deep(.el-breadcrumb__separator) {
  color: #666;
}

.admin-wrapper[data-theme="dark"] :deep(.el-menu) {
  background-color: #1e1e1e;
}

.admin-wrapper[data-theme="dark"] :deep(.el-menu-item) {
  color: #b0b0b0;
}

.admin-wrapper[data-theme="dark"] :deep(.el-menu-item:hover) {
  background-color: #2a2a2a;
}

.admin-wrapper[data-theme="dark"] :deep(.el-menu-item.is-active) {
  color: #60a5fa;
  background-color: #1e3a5f;
}

.admin-wrapper[data-theme="dark"] :deep(.el-sub-menu__title) {
  color: #b0b0b0;
}

.admin-wrapper[data-theme="dark"] :deep(.el-sub-menu__title:hover) {
  background-color: #2a2a2a;
}

/* Mobile dark overrides */
@media (max-width: 768px) {
  .admin-wrapper[data-theme="dark"] .sidebar-container {
    box-shadow: 4px 0 10px rgba(0,0,0,0.4);
  }

  .admin-wrapper[data-theme="dark"] .sidebar-overlay {
    background: rgba(0, 0, 0, 0.6);
  }
}
</style>
