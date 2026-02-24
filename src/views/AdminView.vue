
<script setup lang="ts">
import { ref, onMounted, watch } from 'vue';
import { adminApi } from '../api/danmaku';
import { ElMessage, ElMessageBox } from 'element-plus';
import { Refresh, SwitchButton, Plus, VideoPlay, Delete, EditPen, Fold, Expand } from '@element-plus/icons-vue';

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

const token = ref(localStorage.getItem('admin_token') || '');
const isAuthenticated = ref(false);
const rooms = ref<Room[]>([]);
const loading = ref(false);
const error = ref('');
const activeSection = ref<'monitor' | 'database'>('monitor');
const activeDatabaseTab = ref<'sessions' | 'songRequests'>('sessions');
const sidebarCollapsed = ref(false);

// Form data
const newRoom = ref({
  roomId: '',
  name: '',
  uid: ''
});
const adding = ref(false);

const sessions = ref<AdminSession[]>([]);
const sessionLoading = ref(false);
const sessionSearch = ref('');
const sessionFilterUserName = ref('');
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

const getAuthConfig = () => {
  const value = token.value.trim();
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
    // Try to fetch rooms to verify token
    await fetchRooms();
    isAuthenticated.value = true;
    localStorage.setItem('admin_token', token.value);
    await refreshDatabaseData();
  } catch (e: any) {
    if (e.response && e.response.status === 401) {
      error.value = 'Token 无效';
      isAuthenticated.value = false;
    } else {
      error.value = '连接服务器失败';
    }
  }
};

const normalizeSessionRow = (row: any): AdminSession => {
  return {
    id: row.id ?? row.Id ?? 0,
    roomId: row.roomId ?? row.room_id ?? row.RoomId ?? '',
    title: row.title ?? row.Title ?? '',
    userName: row.userName ?? row.user_name ?? row.UserName ?? '',
    startTime: row.startTime ?? row.start_time ?? row.StartTime,
    endTime: row.endTime ?? row.end_time ?? row.EndTime,
    filePath: row.filePath ?? row.file_path ?? row.FilePath
  };
};

const normalizeSongRequestRow = (row: any): AdminSongRequest => {
  return {
    id: row.id ?? row.Id ?? 0,
    sessionId: row.sessionId ?? row.session_id ?? row.SessionId,
    roomId: row.roomId ?? row.room_id ?? row.RoomId,
    userName: row.userName ?? row.user_name ?? row.UserName,
    uid: row.uid ?? row.Uid,
    songName: row.songName ?? row.song_name ?? row.SongName,
    singer: row.singer ?? row.Singer,
    createdAt: row.createdAt ?? row.created_at ?? row.CreatedAt
  };
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
        search: sessionSearch.value.trim() || undefined,
        userName: sessionFilterUserName.value.trim() || undefined,
        roomId: sessionFilterRoomId.value.trim() || undefined
      }
    });
    const data = res.data;
    sessions.value = Array.isArray(data.list) ? data.list.map(normalizeSessionRow) : [];
    sessionTotal.value = data.total ?? sessions.value.length;
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
        search: songSearch.value.trim() || undefined,
        userName: songFilterUserName.value.trim() || undefined,
        roomId: songFilterRoomId.value.trim() || undefined
      }
    });
    const data = res.data;
    songRequests.value = Array.isArray(data.list) ? data.list.map(normalizeSongRequestRow) : [];
    songTotal.value = data.total ?? songRequests.value.length;
  } finally {
    songLoading.value = false;
  }
};

const refreshDatabaseData = async () => {
  if (!isAuthenticated.value) return;
  if (activeDatabaseTab.value === 'sessions') {
    await fetchSessions();
  } else {
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
    throw e;
  } finally {
    loading.value = false;
  }
};

const refreshCurrentSection = async () => {
  if (activeSection.value === 'monitor') {
    await fetchRooms();
  } else {
    await refreshDatabaseData();
  }
};

const applySessionFilters = async () => {
  sessionPage.value = 1;
  await fetchSessions();
};

const applySongFilters = async () => {
  songPage.value = 1;
  await fetchSongRequests();
};

const addRoom = async () => {
  if (!newRoom.value.roomId || !newRoom.value.name) return;
  adding.value = true;
  try {
    await adminApi.post('/admin/rooms', {
      roomId: parseInt(newRoom.value.roomId),
      name: newRoom.value.name,
      uid: newRoom.value.uid
    }, getAuthConfig());
    newRoom.value = { roomId: '', name: '', uid: '' };
    ElMessage.success('添加成功');
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
    // 延迟刷新以等待进程重启
    setTimeout(fetchRooms, 2000);
  } catch (e: any) {
    ElMessage.error('重启失败: ' + (e.response?.data?.error || e.message));
  }
};

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

const logout = () => {
  token.value = '';
  localStorage.removeItem('admin_token');
  isAuthenticated.value = false;
  rooms.value = [];
  sessions.value = [];
  songRequests.value = [];
};

onMounted(() => {
  if (token.value) {
    checkAuth();
  }
});

watch(activeSection, async (val) => {
  if (val === 'database') {
    await refreshDatabaseData();
  }
});

watch(activeDatabaseTab, async () => {
  if (activeSection.value === 'database') {
    await refreshDatabaseData();
  }
});
</script>

<template>
  <div class="admin-container">
    <div class="header">
      <h1 class="page-title">录制管理后台</h1>
      <div v-if="isAuthenticated" class="header-actions">
        <el-button
          :icon="Refresh"
          @click="refreshCurrentSection"
          :loading="activeSection === 'monitor' ? loading : (activeDatabaseTab === 'sessions' ? sessionLoading : songLoading)"
        >
          刷新状态
        </el-button>
        <el-button type="danger" plain :icon="SwitchButton" @click="logout">退出登录</el-button>
      </div>
    </div>

    <!-- Login -->
    <div v-if="!isAuthenticated" class="login-box-wrapper">
      <el-card class="login-box">
        <template #header>
          <div class="card-header">
            <span>管理员登录</span>
          </div>
        </template>
        <div class="input-group">
          <el-input 
            v-model="token" 
            type="password" 
            placeholder="请输入管理员 Token"
            show-password
            @keyup.enter="checkAuth"
          />
          <el-button type="primary" @click="checkAuth" :loading="loading" class="login-btn">
            进入后台
          </el-button>
        </div>
        <p v-if="error" class="error-text">{{ error }}</p>
      </el-card>
    </div>

    <!-- Dashboard -->
    <div v-else class="dashboard">
      <div class="admin-layout" :class="{ collapsed: sidebarCollapsed }">
        <div class="admin-sidebar" :class="{ collapsed: sidebarCollapsed }">
          <div class="sidebar-header">
            <div class="sidebar-title" v-show="!sidebarCollapsed">功能</div>
            <el-button
              class="sidebar-toggle"
              link
              :icon="sidebarCollapsed ? Expand : Fold"
              @click="sidebarCollapsed = !sidebarCollapsed"
            />
          </div>
          <div
            class="sidebar-item"
            :class="{ active: activeSection === 'monitor' }"
            @click="activeSection = 'monitor'"
          >
            <span v-show="!sidebarCollapsed">监控录制管理</span>
            <span v-show="sidebarCollapsed">监控</span>
          </div>
          <div
            class="sidebar-item"
            :class="{ active: activeSection === 'database' }"
            @click="activeSection = 'database'"
          >
            <span v-show="!sidebarCollapsed">数据库管理</span>
            <span v-show="sidebarCollapsed">数据库</span>
          </div>
        </div>

        <div class="admin-content">
          <div v-if="activeSection === 'monitor'" class="section-panel">
            <el-card class="action-card">
              <template #header>
                <div class="card-header">
                  <span>添加直播间</span>
                </div>
              </template>
              <div class="form-row">
                <el-input v-model="newRoom.name" placeholder="主播名称 (如: 桃几OvO)" />
                <el-input v-model="newRoom.roomId" placeholder="房间号 (如: 22642754)" type="number" />
                <el-input v-model="newRoom.uid" placeholder="UID (可选, 用于头像)" />
                <el-button type="primary" :icon="Plus" @click="addRoom" :loading="adding">
                  添加并启动
                </el-button>
              </div>
            </el-card>

            <el-card class="list-card">
              <el-table :data="rooms" style="width: 100%" v-loading="loading">
                <el-table-column prop="name" label="主播" />
                <el-table-column prop="room_id" label="房间号" />
                <el-table-column label="状态" width="100">
                  <template #default="scope">
                    <el-tag 
                      :type="getMonitorStatusType(scope.row.process_status)"
                      effect="dark"
                    >
                      {{ getMonitorStatusLabel(scope.row.process_status) }}
                    </el-tag>
                  </template>
                </el-table-column>
                <el-table-column label="运行时长">
                  <template #default="scope">
                    {{ formatUptime(scope.row.process_uptime) }}
                  </template>
                </el-table-column>
                <el-table-column label="开播时长">
                  <template #default="scope">
                    {{ formatLiveDuration(scope.row.live_status, scope.row.live_start_time) }}
                  </template>
                </el-table-column>
                <el-table-column prop="pid" label="PID" width="80" />
                <el-table-column label="操作" width="200" align="right">
                  <template #default="scope">
                    <el-button-group>
                      <el-button type="primary" size="small" :icon="VideoPlay" @click="restartRoom(scope.row.id)">
                        重启
                      </el-button>
                      <el-button type="danger" size="small" :icon="Delete" @click="deleteRoom(scope.row.id, scope.row.name)">
                        删除
                      </el-button>
                    </el-button-group>
                  </template>
                </el-table-column>
              </el-table>
            </el-card>
          </div>

          <div v-else class="section-panel">
            <div class="db-tabs">
              <el-button
                :type="activeDatabaseTab === 'sessions' ? 'primary' : 'default'"
                @click="activeDatabaseTab = 'sessions'"
              >
                直播回放
              </el-button>
              <el-button
                :type="activeDatabaseTab === 'songRequests' ? 'primary' : 'default'"
                @click="activeDatabaseTab = 'songRequests'"
              >
                点歌记录
              </el-button>
            </div>

            <el-card v-if="activeDatabaseTab === 'sessions'" class="list-card">
              <div class="db-toolbar">
                <div class="db-filters">
                  <el-input
                    v-model="sessionFilterUserName"
                    placeholder="主播名称"
                    clearable
                    @keyup.enter="applySessionFilters"
                  />
                  <el-input
                    v-model="sessionFilterRoomId"
                    placeholder="房间号"
                    clearable
                    @keyup.enter="applySessionFilters"
                  />
                  <el-input
                    v-model="sessionSearch"
                    placeholder="搜索直播标题"
                    clearable
                    @keyup.enter="applySessionFilters"
                  />
                </div>
                <div class="db-actions">
                  <el-button :icon="Refresh" @click="applySessionFilters" :loading="sessionLoading">刷新</el-button>
                  <el-button type="primary" :icon="Plus" @click="openCreateSession">新增</el-button>
                </div>
              </div>
              <el-table :data="sessions" style="width: 100%" v-loading="sessionLoading">
                <el-table-column prop="id" label="ID" width="80" />
                <el-table-column prop="userName" label="主播" />
                <el-table-column prop="roomId" label="房间号" />
                <el-table-column prop="title" label="标题" />
                <el-table-column label="开始时间">
                  <template #default="scope">
                    {{ formatTimestamp(scope.row.startTime) }}
                  </template>
                </el-table-column>
                <el-table-column label="结束时间">
                  <template #default="scope">
                    {{ formatTimestamp(scope.row.endTime) }}
                  </template>
                </el-table-column>
                <el-table-column label="操作" width="160" align="right">
                  <template #default="scope">
                    <el-button-group>
                      <el-button size="small" :icon="EditPen" @click="openEditSession(scope.row)">编辑</el-button>
                      <el-button type="danger" size="small" :icon="Delete" @click="deleteSession(scope.row)">删除</el-button>
                    </el-button-group>
                  </template>
                </el-table-column>
              </el-table>
              <div class="pagination-row">
                <el-pagination
                  :current-page="sessionPage"
                  :page-size="sessionPageSize"
                  :total="sessionTotal"
                  layout="prev, pager, next, sizes, total"
                  @size-change="(val: number) => { sessionPageSize = val; sessionPage = 1; fetchSessions(); }"
                  @current-change="(val: number) => { sessionPage = val; fetchSessions(); }"
                />
              </div>
            </el-card>

            <el-card v-else class="list-card">
              <div class="db-toolbar">
                <div class="db-filters">
                  <el-input
                    v-model="songFilterUserName"
                    placeholder="点歌用户"
                    clearable
                    @keyup.enter="applySongFilters"
                  />
                  <el-input
                    v-model="songFilterRoomId"
                    placeholder="房间号"
                    clearable
                    @keyup.enter="applySongFilters"
                  />
                  <el-input
                    v-model="songSearch"
                    placeholder="搜索歌曲/歌手"
                    clearable
                    @keyup.enter="applySongFilters"
                  />
                </div>
                <div class="db-actions">
                  <el-button :icon="Refresh" @click="applySongFilters" :loading="songLoading">刷新</el-button>
                  <el-button type="primary" :icon="Plus" @click="openCreateSong">新增</el-button>
                </div>
              </div>
              <el-table :data="songRequests" style="width: 100%" v-loading="songLoading">
                <el-table-column prop="id" label="ID" width="80" />
                <el-table-column prop="userName" label="点歌用户" />
                <el-table-column prop="songName" label="歌曲" />
                <el-table-column prop="singer" label="歌手" />
                <el-table-column prop="roomId" label="房间号" />
                <el-table-column label="时间">
                  <template #default="scope">
                    {{ formatTimestamp(scope.row.createdAt) }}
                  </template>
                </el-table-column>
                <el-table-column label="操作" width="160" align="right">
                  <template #default="scope">
                    <el-button-group>
                      <el-button size="small" :icon="EditPen" @click="openEditSong(scope.row)">编辑</el-button>
                      <el-button type="danger" size="small" :icon="Delete" @click="deleteSongRequest(scope.row)">删除</el-button>
                    </el-button-group>
                  </template>
                </el-table-column>
              </el-table>
              <div class="pagination-row">
                <el-pagination
                  :current-page="songPage"
                  :page-size="songPageSize"
                  :total="songTotal"
                  layout="prev, pager, next, sizes, total"
                  @size-change="(val: number) => { songPageSize = val; songPage = 1; fetchSongRequests(); }"
                  @current-change="(val: number) => { songPage = val; fetchSongRequests(); }"
                />
              </div>
            </el-card>
          </div>
        </div>
      </div>
    </div>
  </div>

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
</template>

<style scoped>
.admin-container {
  display: flex;
  flex-direction: column;
  padding: 2rem;
  background-color: var(--bg-primary);
  min-height: 100vh;
  height: 100vh;
  color: var(--text-primary);
}

.header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 2rem;
}

.page-title {
  font-size: 2rem;
  font-weight: 600;
  color: var(--text-primary);
  margin: 0;
}

.login-box-wrapper {
  display: flex;
  justify-content: center;
  margin-top: 4rem;
}

.login-box {
  width: 100%;
  max-width: 400px;
  background-color: var(--bg-card);
  border: 1px solid var(--border);
}

.card-header {
  font-weight: 600;
  color: var(--text-primary);
}

.input-group {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.login-btn {
  width: 100%;
}

.error-text {
  color: var(--danger);
  text-align: center;
  margin-top: 1rem;
  font-size: 0.9rem;
}

.dashboard {
  display: block;
  flex: 1;
  min-height: 0;
}

.action-card, .list-card {
  background-color: var(--bg-card);
  border: 1px solid var(--border);
  color: var(--text-primary);
}

.form-row {
  display: flex;
  gap: 1rem;
  align-items: center;
}

.admin-layout {
  display: grid;
  grid-template-columns: 220px 1fr;
  gap: 1.5rem;
  flex: 1;
  min-height: 0;
}

.admin-layout.collapsed {
  grid-template-columns: 84px 1fr;
}

.admin-sidebar {
  background-color: var(--bg-card);
  border: 1px solid var(--border);
  border-radius: 12px;
  padding: 1.5rem 1rem;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.admin-sidebar.collapsed {
  padding: 1rem 0.5rem;
  align-items: center;
}

.sidebar-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
  width: 100%;
}

.admin-sidebar.collapsed .sidebar-header {
  justify-content: center;
}

.sidebar-toggle {
  color: var(--text-secondary);
}

.sidebar-title {
  font-size: 0.95rem;
  color: var(--text-secondary);
  letter-spacing: 0.08em;
}

.sidebar-item {
  padding: 0.85rem 1rem;
  border-radius: 10px;
  cursor: pointer;
  font-weight: 500;
  background-color: transparent;
  color: var(--text-primary);
  transition: all 0.2s ease;
  text-align: left;
  width: 100%;
}

.admin-sidebar.collapsed .sidebar-item {
  padding: 0.85rem 0.5rem;
  text-align: center;
}

.sidebar-item:hover {
  background-color: var(--bg-hover);
}

.sidebar-item.active {
  background-color: var(--bg-secondary);
  color: var(--accent);
  box-shadow: inset 0 0 0 1px var(--accent);
}

.admin-content {
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
  min-height: 0;
  overflow: auto;
}

.section-panel {
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
  min-height: 0;
}

.db-tabs {
  display: flex;
  gap: 0.75rem;
}

.db-toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 1rem;
  margin-bottom: 1rem;
  flex-wrap: wrap;
}

.db-filters {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(140px, 1fr));
  gap: 0.5rem;
  flex: 1;
  min-width: 260px;
}

.db-actions {
  display: flex;
  gap: 0.5rem;
}

.pagination-row {
  margin-top: 1rem;
  display: flex;
  justify-content: flex-end;
}

.dialog-form {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

@media (max-width: 1024px) {
  .admin-layout {
    grid-template-columns: 1fr;
  }
}

:deep(.el-card__header) {
  border-bottom-color: var(--border);
}

:deep(.el-table) {
  --el-table-bg-color: var(--bg-card);
  --el-table-tr-bg-color: var(--bg-card);
  --el-table-header-bg-color: var(--bg-secondary);
  --el-table-text-color: var(--text-primary);
  --el-table-header-text-color: var(--text-secondary);
  --el-table-border-color: var(--border);
}

:deep(.el-input__wrapper) {
  background-color: var(--bg-primary);
}

:deep(.el-button--primary) {
  --el-button-bg-color: var(--accent);
  --el-button-border-color: var(--accent);
  --el-button-hover-bg-color: var(--accent-hover);
}
</style>
