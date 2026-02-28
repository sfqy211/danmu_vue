<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch, computed } from 'vue';
import { adminApi } from '../api/danmaku';
import { ElMessage, ElMessageBox } from 'element-plus';
import { 
  Refresh, SwitchButton, Plus, VideoPlay, Delete, EditPen, 
  VideoCamera, DataLine, Fold, Expand 
} from '@element-plus/icons-vue';

// --- Interfaces ---
interface Room {
  id: number;
  room_id: number;
  name: string;
  uid: string;
  is_active: number;
  process_status: string;
  process_uptime: number | string;
  live_status: number;
  live_start_time: number | null;
  pid: number | null;
  remark?: string;
}

interface AdminSession {
  id: number;
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

// UI State
const loading = ref(false);
const error = ref('');
const activeSection = ref<'monitor' | 'sessions' | 'songRequests'>('monitor');
const isMobile = ref(window.innerWidth <= 768);
const sidebarCollapsed = ref(window.innerWidth <= 768);

// Handle resize for responsive states
const handleResize = () => {
  const mobile = window.innerWidth <= 768;
  isMobile.value = mobile;
  if (mobile) {
    sidebarCollapsed.value = true;
  }
};

// Monitor Data
const rooms = ref<Room[]>([]);
const newRoom = ref({ roomId: '', name: '', uid: '', remark: '' });
const adding = ref(false);
const editDialogVisible = ref(false);
const editForm = ref({ id: 0, remark: '' });

// Sessions Data
const sessions = ref<AdminSession[]>([]);
const sessionLoading = ref(false);
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
    remark: row.remark || ''
  };
  editDialogVisible.value = true;
};

const saveRoomEdit = async () => {
  try {
    await adminApi.put(`/admin/rooms/${editForm.value.id}`, {
      roomId: 0, // Not used
      name: 'Unknown', // Not used
      remark: editForm.value.remark
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
  } else {
    items.push({ name: '数据库管理', path: '' });
    items.push({ name: '点歌记录', path: '' });
  }
  return items;
});

const isRefreshing = computed(() => {
  if (activeSection.value === 'monitor') return loading.value;
  if (activeSection.value === 'sessions') return sessionLoading.value;
  if (activeSection.value === 'songRequests') return songLoading.value;
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
  if (['monitor', 'sessions', 'songRequests'].includes(index)) {
    activeSection.value = index as any;
  }
  if (isMobile.value) {
    sidebarCollapsed.value = true;
  }
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
  }
};

const fetchRooms = async () => {
  loading.value = true;
  try {
    const res = await adminApi.get('/admin/rooms', getAuthConfig());
    rooms.value = res.data;
    error.value = '';
  } catch (e: any) {
    console.error('Fetch rooms failed:', e);
    // Don't throw here to prevent blocking UI
  } finally {
    loading.value = false;
  }
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

// --- Methods: Data Normalization ---

const normalizeSessionRow = (row: any): AdminSession => ({
  id: row.id ?? row.Id ?? 0,
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
      name: 'Unknown', // Backend will resolve this, but DTO requires it
      roomId: 0 // Backend will resolve this
    }, getAuthConfig());
    newRoom.value = { roomId: '', name: '', uid: '', remark: '' };
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
  if (liveStatus !== 1 || !liveStartTime) return '未开播';
  const startMs = liveStartTime < 1_000_000_000_000 ? liveStartTime * 1000 : liveStartTime;
  const diffSeconds = Math.floor((Date.now() - startMs) / 1000);
  if (diffSeconds < 0) return '未开播';
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
  if (val === 'sessions' || val === 'songRequests') {
    await refreshDatabaseData();
  }
});
</script>

<template>
  <div class="admin-wrapper">
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
            
            <el-sub-menu index="database">
              <template #title>
                <el-icon><DataLine /></el-icon>
                <span>数据库管理</span>
              </template>
              <el-menu-item index="sessions">直播回放</el-menu-item>
              <el-menu-item index="songRequests">点歌记录</el-menu-item>
            </el-sub-menu>
          </el-menu>
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
              <div class="search-section">
                <div class="search-form">
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
                  <el-table-column prop="room_id" label="房间号" align="center" />
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
              <div class="search-section">
                <div class="search-form">
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
                      :label="room.name + ' (' + room.room_id + ')'"
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
            <template v-else>
              <div class="search-section">
                <div class="search-form">
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
                      :label="room.name + ' (' + room.room_id + ')'"
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
          </div>
        </el-main>
      </el-container>
    </el-container>

    <!-- Dialogs -->
    <el-dialog v-model="editDialogVisible" title="修改主播配置" width="400px">
      <div class="dialog-form">
        <el-input v-model="editForm.remark" placeholder="备注" />
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

.search-section {
  background: #fff;
  padding: 18px;
  border-radius: 4px;
  margin-bottom: 16px;
}

.search-form {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
}

.search-form :deep(.el-input) {
  width: 200px;
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
</style>
