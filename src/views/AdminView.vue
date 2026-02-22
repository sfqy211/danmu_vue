
<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { adminApi } from '../api/danmaku';
import { ElMessage, ElMessageBox } from 'element-plus';
import { Refresh, SwitchButton, Plus, VideoPlay, Delete } from '@element-plus/icons-vue';

interface Room {
  id: number;
  room_id: number;
  name: string;
  uid: string;
  is_active: number;
  process_status: string; // 'online' | 'stopped' | 'errored'
  process_uptime: number;
  pid: number | null;
}

const token = ref(localStorage.getItem('admin_token') || '');
const isAuthenticated = ref(false);
const rooms = ref<Room[]>([]);
const loading = ref(false);
const error = ref('');

// Form data
const newRoom = ref({
  roomId: '',
  name: '',
  uid: ''
});
const adding = ref(false);

const checkAuth = async () => {
  if (!token.value) return;
  try {
    // Try to fetch rooms to verify token
    await fetchRooms();
    isAuthenticated.value = true;
    localStorage.setItem('admin_token', token.value);
  } catch (e: any) {
    if (e.response && e.response.status === 401) {
      error.value = 'Token 无效';
      isAuthenticated.value = false;
    } else {
      error.value = '连接服务器失败';
    }
  }
};

const fetchRooms = async () => {
  loading.value = true;
  try {
    const res = await adminApi.get('/admin/rooms', {
      headers: { Authorization: token.value }
    });
    rooms.value = res.data;
    error.value = '';
  } catch (e: any) {
    throw e;
  } finally {
    loading.value = false;
  }
};

const addRoom = async () => {
  if (!newRoom.value.roomId || !newRoom.value.name) return;
  adding.value = true;
  try {
    await adminApi.post('/admin/rooms', {
      roomId: parseInt(newRoom.value.roomId),
      name: newRoom.value.name,
      uid: newRoom.value.uid
    }, {
      headers: { Authorization: token.value }
    });
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
    
    await adminApi.delete(`/admin/rooms/${id}`, {
      headers: { Authorization: token.value }
    });
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
    await adminApi.post(`/admin/rooms/${id}/restart`, {}, {
      headers: { Authorization: token.value }
    });
    ElMessage.success('重启指令已发送');
    // 延迟刷新以等待进程重启
    setTimeout(fetchRooms, 2000);
  } catch (e: any) {
    ElMessage.error('重启失败: ' + (e.response?.data?.error || e.message));
  }
};

const formatUptime = (ms: number) => {
  if (!ms) return '-';
  const seconds = Math.floor((Date.now() - ms) / 1000);
  if (seconds < 60) return `${seconds}秒`;
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}分`;
  const hours = Math.floor(minutes / 60);
  return `${hours}小时`;
};

const logout = () => {
  token.value = '';
  localStorage.removeItem('admin_token');
  isAuthenticated.value = false;
  rooms.value = [];
};

onMounted(() => {
  if (token.value) {
    checkAuth();
  }
});
</script>

<template>
  <div class="admin-container">
    <div class="header">
      <h1 class="page-title">录制管理后台</h1>
      <div v-if="isAuthenticated" class="header-actions">
        <el-button :icon="Refresh" @click="fetchRooms" :loading="loading">刷新状态</el-button>
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
      <!-- Add Room Form -->
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

      <!-- Rooms List -->
      <el-card class="list-card">
        <el-table :data="rooms" style="width: 100%" v-loading="loading">
          <el-table-column prop="id" label="ID" width="60" />
          <el-table-column prop="name" label="主播" />
          <el-table-column prop="room_id" label="房间号" />
          <el-table-column label="状态" width="100">
            <template #default="scope">
              <el-tag 
                :type="scope.row.process_status === 'online' ? 'success' : (scope.row.process_status === 'stopped' ? 'info' : 'danger')"
                effect="dark"
              >
                {{ scope.row.process_status }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="运行时长">
            <template #default="scope">
              {{ formatUptime(scope.row.process_uptime) }}
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
  </div>
</template>

<style scoped>
.admin-container {
  max-width: 1200px;
  margin: 0 auto;
  padding: 2rem;
  background-color: var(--bg-primary);
  min-height: 100vh;
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
  display: flex;
  flex-direction: column;
  gap: 2rem;
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
