<template>
  <div class="log-viewer" :class="{ 'light-theme': !isDarkMode }">
    <!-- Toolbar -->
    <div class="log-toolbar">
      <div class="toolbar-left">
        <el-select v-model="currentFile" size="small" class="log-file-select" placeholder="选择日志文件" @change="switchLogFile">
          <el-option v-for="file in logFiles" :key="file.name" :label="`${file.name} (${formatSize(file.size)})`" :value="file.name" />
        </el-select>
        <el-tag size="small" :type="connectionStatus === 'connected' ? 'success' : connectionStatus === 'connecting' ? 'warning' : 'danger'" class="connection-badge">
          <span class="status-dot" :class="connectionStatus" />
          {{ connectionStatusText }}
        </el-tag>
      </div>
      <div class="toolbar-right">
        <el-select v-model="levelFilter" size="small" class="level-filter" placeholder="全部级别" clearable>
          <el-option label="全部" value="" />
          <el-option label="Error" value="error">
            <template #default>
              <span class="level-dot error-dot" /> Error {{ levelCounts.error > 0 ? `(${levelCounts.error})` : '' }}
            </template>
          </el-option>
          <el-option label="Warning" value="warn">
            <template #default>
              <span class="level-dot warn-dot" /> Warning {{ levelCounts.warn > 0 ? `(${levelCounts.warn})` : '' }}
            </template>
          </el-option>
          <el-option label="Information" value="info">
            <template #default>
              <span class="level-dot info-dot" /> Info {{ levelCounts.info > 0 ? `(${levelCounts.info})` : '' }}
            </template>
          </el-option>
          <el-option label="Debug" value="debug">
            <template #default>
              <span class="level-dot debug-dot" /> Debug {{ levelCounts.debug > 0 ? `(${levelCounts.debug})` : '' }}
            </template>
          </el-option>
        </el-select>
        <el-input v-model="searchQuery" size="small" placeholder="搜索日志..." clearable class="log-search" @clear="onSearchClear" @keyup.enter="onSearchEnter">
          <template #prefix><el-icon><Search /></el-icon></template>
        </el-input>
        <el-tooltip content="自动滚动" placement="top">
          <el-button size="small" :type="autoScroll ? 'primary' : 'default'" @click="toggleAutoScroll">
            <el-icon><Bottom /></el-icon>
          </el-button>
        </el-tooltip>
        <el-tooltip content="清屏" placement="top">
          <el-button size="small" @click="clearScreen">
            <el-icon><Delete /></el-icon>
          </el-button>
        </el-tooltip>
        <el-tooltip content="下载日志" placement="top">
          <el-button size="small" @click="downloadLog">
            <el-icon><Download /></el-icon>
          </el-button>
        </el-tooltip>
      </div>
    </div>

    <!-- Terminal -->
    <div ref="terminalRef" class="terminal-body" @scroll="handleScroll">
      <div v-if="filteredLines.length === 0" class="empty-state">
        <p v-if="!currentFile">请选择日志文件</p>
        <p v-else-if="levelFilter && !searchQuery">当前级别下无日志</p>
        <p v-else-if="searchQuery">未找到匹配 "{{ searchQuery }}" 的日志</p>
        <p v-else>等待日志数据...</p>
      </div>
      <template v-else>
        <div v-for="(line, index) in filteredLines" :key="index" class="log-line" :class="`level-${line.level}`" @click="copyLine(line.raw)">
          <span class="line-number">{{ index + 1 }}</span>
          <span class="line-content" v-html="line.html" />
        </div>
      </template>
    </div>

    <!-- Status Bar -->
    <div class="terminal-statusbar">
      <div class="status-left">
        <span class="status-item">{{ currentFile || '未选择' }}</span>
        <span class="status-item">{{ filteredLines.length }} / {{ totalLines }} 行</span>
        <span v-if="levelFilter" class="status-item level-badge" :class="`badge-${levelFilter}`">
          {{ { error: 'Error', warn: 'Warning', info: 'Info', debug: 'Debug' }[levelFilter] }}
        </span>
        <span v-if="searchQuery" class="status-item search-info">搜索: {{ searchQuery }}</span>
      </div>
      <div class="status-right">
        <span v-if="isAtBottom" class="status-item">
          <el-icon><Bottom /></el-icon> 已跟随
        </span>
        <span v-else class="status-item clickable" @click="scrollToBottom">
          <el-icon><Top /></el-icon> 继续跟随
        </span>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount, nextTick, watch, inject, type Ref } from 'vue'
import { ElMessage } from 'element-plus'
import { Search, Bottom, Delete, Download, Top } from '@element-plus/icons-vue'
import { getLogFiles, downloadLogFile, type LogFileEntry } from '../api/danmaku'

const adminDarkMode = inject<Ref<boolean>>('adminDarkMode')
const isDarkMode = computed(() => adminDarkMode?.value ?? false)

interface LogLine {
  raw: string
  html: string
  level: 'info' | 'warn' | 'error' | 'debug' | 'default'
}

type ConnectionStatus = 'connected' | 'connecting' | 'disconnected'

const terminalRef = ref<HTMLElement>()
const logFiles = ref<LogFileEntry[]>([])
const currentFile = ref('')
const lines = ref<LogLine[]>([])
const searchQuery = ref('')
const levelFilter = ref<string>('') // '' = all, 'error' | 'warn' | 'info' | 'debug'
const autoScroll = ref(true)
const isAtBottom = ref(true)
const connectionStatus = ref<ConnectionStatus>('disconnected')
const maxLines = 5000

let eventSource: EventSource | null = null
let reconnectTimer: ReturnType<typeof setTimeout> | null = null
let searchDebounceTimer: ReturnType<typeof setTimeout> | null = null
let scrollScheduled = false

const connectionStatusText = computed(() => {
  const map: Record<ConnectionStatus, string> = { connected: '已连接', connecting: '连接中...', disconnected: '已断开' }
  return map[connectionStatus.value]
})

const totalLines = computed(() => lines.value.length)

const filteredLines = computed(() => {
  let result = lines.value
  // Level filter
  if (levelFilter.value) {
    result = result.filter(line => line.level === levelFilter.value)
  }
  // Search filter
  if (searchQuery.value.trim()) {
    const q = searchQuery.value.toLowerCase()
    result = result.filter(line => line.raw.toLowerCase().includes(q))
  }
  return result
})

const levelCounts = computed(() => {
  const counts = { error: 0, warn: 0, info: 0, debug: 0, default: 0 }
  for (const line of lines.value) {
    counts[line.level]++
  }
  return counts
})

function parseLogLevel(line: string): LogLine['level'] {
  const u = line.toUpperCase()
  // Serilog file format: "2026-05-03 14:11:56.123 [INF] message"
  // Level is always a 3-char bracketed tag [INF] / [ERR] / [WRN] / [DBG] / [FTL]
  const m = u.match(/^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:\s*[+-]\d{2}:?\d{2})?\s+\[(INF|ERR|WRN|DBG|FTL)\]/)
  if (m) {
    const tag = m[1]
    if (tag === 'ERR' || tag === 'FTL') return 'error'
    if (tag === 'WRN') return 'warn'
    if (tag === 'DBG') return 'debug'
    if (tag === 'INF') return 'info'
  }
  // Fallback: non-standard formats (console output without timestamp, custom logs, etc.)
  // Only match bracketed tags to avoid false positives from message body words
  if (u.includes('[ERR') || u.includes('[FTL')) return 'error'
  if (u.includes('[WRN')) return 'warn'
  if (u.includes('[DBG')) return 'debug'
  if (u.includes('[INF')) return 'info'
  return 'default'
}

function escapeHtml(s: string): string {
  return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
}

function highlightSearch(text: string): string {
  if (!searchQuery.value.trim()) return text
  const q = escapeHtml(searchQuery.value)
  const regex = new RegExp(`(${q.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')})`, 'gi')
  return text.replace(regex, '<mark class="search-highlight">$1</mark>')
}

function processLine(raw: string): LogLine {
  const level = parseLogLevel(raw)
  const escaped = escapeHtml(raw)
  const html = highlightSearch(escaped)
  return { raw, html, level }
}

function getAdminBaseUrl(): string {
  const envUrl = import.meta.env.VITE_ADMIN_API_BASE_URL || import.meta.env.VITE_API_BASE_URL
  if (envUrl) {
    const trimmed = envUrl.replace(/\/+$/, '')
    return trimmed.endsWith('/api') ? trimmed : `${trimmed}/api`
  }
  return '/api'
}

function connectSSE() {
  if (eventSource) { eventSource.close(); eventSource = null }
  if (!currentFile.value) return

  connectionStatus.value = 'connecting'
  const base = getAdminBaseUrl()
  const token = localStorage.getItem('admin_token') || ''
  const url = `${base}/admin/logs/stream?file=${encodeURIComponent(currentFile.value)}&token=${encodeURIComponent(token)}`

  eventSource = new EventSource(url)

  eventSource.onopen = () => {
    connectionStatus.value = 'connected'
    if (reconnectTimer) { clearTimeout(reconnectTimer); reconnectTimer = null }
  }

  eventSource.onmessage = (event) => {
    const raw = event.data
    if (!raw) return
    const line = processLine(raw)
    lines.value.push(line)
    if (lines.value.length > maxLines) {
      lines.value = lines.value.slice(lines.value.length - maxLines)
    }
    if (autoScroll.value && isAtBottom.value) {
      scheduleScroll()
    }
  }

  eventSource.onerror = () => {
    connectionStatus.value = 'disconnected'
    eventSource?.close(); eventSource = null
    if (!reconnectTimer) {
      reconnectTimer = setTimeout(() => { reconnectTimer = null; connectSSE() }, 3000)
    }
  }
}

function disconnectSSE() {
  if (reconnectTimer) { clearTimeout(reconnectTimer); reconnectTimer = null }
  if (eventSource) { eventSource.close(); eventSource = null }
  connectionStatus.value = 'disconnected'
}

function scheduleScroll() {
  if (scrollScheduled) return
  scrollScheduled = true
  requestAnimationFrame(() => {
    scrollScheduled = false
    scrollToBottom()
  })
}

function scrollToBottom() {
  nextTick(() => {
    const el = terminalRef.value
    if (el) { el.scrollTop = el.scrollHeight; isAtBottom.value = true }
  })
}

function handleScroll() {
  const el = terminalRef.value
  if (!el) return
  isAtBottom.value = el.scrollHeight - el.scrollTop - el.clientHeight < 50
}

async function fetchLogFiles() {
  try {
    logFiles.value = await getLogFiles()
    if (logFiles.value.length > 0 && !currentFile.value) {
      currentFile.value = logFiles.value[0].name
      switchLogFile()
    }
  } catch { /* ignore */ }
}

function switchLogFile() {
  lines.value = []
  connectSSE()
}

function toggleAutoScroll() {
  autoScroll.value = !autoScroll.value
  if (autoScroll.value) { isAtBottom.value = true; scrollToBottom() }
}

function clearScreen() { lines.value = [] }

function onSearchClear() { searchQuery.value = '' }
function onSearchEnter() { /* search is reactive via computed */ }

async function downloadLog() {
  if (!currentFile.value) { ElMessage.warning('请先选择日志文件'); return }
  try {
    const blob = await downloadLogFile(currentFile.value)
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url; a.download = currentFile.value
    document.body.appendChild(a); a.click(); document.body.removeChild(a)
    URL.revokeObjectURL(url)
    ElMessage.success('下载成功')
  } catch (e: any) { ElMessage.error('下载失败: ' + (e.response?.data?.error || e.message)) }
}

function copyLine(text: string) {
  navigator.clipboard.writeText(text).then(() => {
    ElMessage.success({ message: '已复制', duration: 1500 })
  }).catch(() => { /* ignore */ })
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`
}

watch(searchQuery, () => {
  if (searchDebounceTimer) clearTimeout(searchDebounceTimer)
  searchDebounceTimer = setTimeout(() => {
    searchDebounceTimer = null
    lines.value = lines.value.map(line => ({
      ...line,
      html: highlightSearch(escapeHtml(line.raw))
    }))
  }, 200)
})

onMounted(() => { fetchLogFiles() })
onBeforeUnmount(() => { disconnectSSE() })
</script>

<style scoped>
.log-viewer {
  display: flex;
  flex-direction: column;
  height: 100%;
  background-color: #0c0c0c;
  border-radius: 8px;
  border: 1px solid #262626;
  overflow: hidden;
  font-family: 'JetBrains Mono', 'Fira Code', 'Consolas', 'Monaco', 'Courier New', monospace;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.4);
}

.log-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 16px;
  background-color: #171717;
  border-bottom: 1px solid #262626;
  gap: 12px;
  flex-wrap: wrap;
}

.toolbar-left, .toolbar-right {
  display: flex;
  align-items: center;
  gap: 10px;
}

.log-file-select { width: 260px; }
.log-file-select :deep(.el-input__wrapper) { background-color: #1e1e1e; box-shadow: 0 0 0 1px #333 inset; }
.log-file-select :deep(.el-input__inner) { color: #e0e0e0; font-family: inherit; }

.connection-badge { background-color: transparent; border: 1px solid #333; font-family: inherit; }
.connection-badge :deep(.el-tag__content) { display: flex; align-items: center; gap: 6px; }

.status-dot {
  width: 8px; height: 8px; border-radius: 50%; display: inline-block;
}
.status-dot.connected { background-color: #4ade80; box-shadow: 0 0 8px #4ade80; }
.status-dot.connecting { background-color: #fbbf24; box-shadow: 0 0 8px #fbbf24; animation: pulse 1.5s infinite; }
.status-dot.disconnected { background-color: #f87171; box-shadow: 0 0 8px #f87171; }

@keyframes pulse { 0%, 100% { opacity: 1; } 50% { opacity: 0.4; } }

.log-search { width: 200px; }
.log-search :deep(.el-input__wrapper) { background-color: #1e1e1e; box-shadow: 0 0 0 1px #333 inset; }
.log-search :deep(.el-input__inner) { color: #e0e0e0; font-family: inherit; }
.log-search :deep(.el-input__icon) { color: #666; }

.terminal-body {
  flex: 1;
  overflow-y: auto;
  overflow-x: hidden;
  padding: 12px 0;
  background-color: #0c0c0c;
  line-height: 1.6;
  font-size: 13px;
}

.terminal-body::-webkit-scrollbar { width: 10px; }
.terminal-body::-webkit-scrollbar-track { background: #0c0c0c; }
.terminal-body::-webkit-scrollbar-thumb { background: #333; border-radius: 5px; border: 2px solid #0c0c0c; }
.terminal-body::-webkit-scrollbar-thumb:hover { background: #444; }

.empty-state {
  display: flex; align-items: center; justify-content: center;
  height: 100%; color: #4a4a4a; font-family: inherit;
}

.log-line {
  display: flex;
  padding: 1px 16px;
  color: #d4d4d4;
  transition: background-color 0.1s;
  cursor: pointer;
  animation: fadeInLine 0.15s ease-out;
}

@keyframes fadeInLine { from { opacity: 0; } to { opacity: 1; } }

.log-line:hover { background-color: rgba(255, 255, 255, 0.04); }

.log-line::before {
  content: '';
  position: absolute;
  left: 0; top: 2px; bottom: 2px;
  width: 2px; border-radius: 0 2px 2px 0; opacity: 0.6;
}

.log-line { position: relative; }
.log-line.level-info { color: #d4d4d4; }
.log-line.level-info::before { background-color: #60a5fa; }
.log-line.level-warn { color: #fbbf24; }
.log-line.level-warn::before { background-color: #fbbf24; }
.log-line.level-error { color: #f87171; }
.log-line.level-error::before { background-color: #f87171; }
.log-line.level-debug { color: #a78bfa; }
.log-line.level-debug::before { background-color: #a78bfa; }
.log-line.level-default { color: #a0a0a0; }
.log-line.level-default::before { background-color: #525252; }

.line-number {
  display: inline-block; width: 48px; flex-shrink: 0;
  color: #404040; text-align: right; padding-right: 16px;
  user-select: none; font-size: 12px; line-height: 1.6;
}

.line-content {
  flex: 1; white-space: pre-wrap; word-break: break-all; overflow-wrap: anywhere;
}

:deep(.search-highlight) {
  background-color: rgba(234, 179, 8, 0.25);
  color: #fbbf24; border-radius: 2px; padding: 0 1px; font-weight: bold;
}

.terminal-statusbar {
  display: flex; align-items: center; justify-content: space-between;
  padding: 6px 16px; background-color: #171717; border-top: 1px solid #262626;
  font-size: 12px; color: #808080; flex-wrap: wrap; gap: 8px;
}

.status-left, .status-right { display: flex; align-items: center; gap: 16px; }
.status-item { display: flex; align-items: center; gap: 6px; }
.status-item .el-icon { font-size: 14px; }
.status-item.clickable { cursor: pointer; color: #60a5fa; transition: color 0.2s; }
.status-item.clickable:hover { color: #93c5fd; }
.search-info { color: #fbbf24; }

@media (max-width: 768px) {
  .log-viewer { font-size: 12px; }
  .log-toolbar { padding: 8px; }
  .toolbar-left, .toolbar-right { width: 100%; justify-content: space-between; }
  .log-file-select { width: 160px; }
  .log-search { width: 140px; }
  .log-line { padding: 1px 8px; }
  .line-number { width: 36px; padding-right: 8px; font-size: 11px; }
}

:deep(.el-tag--success) { background-color: rgba(74, 222, 128, 0.1); border-color: rgba(74, 222, 128, 0.2); color: #4ade80; }
:deep(.el-tag--warning) { background-color: rgba(251, 191, 36, 0.1); border-color: rgba(251, 191, 36, 0.2); color: #fbbf24; }
:deep(.el-tag--danger) { background-color: rgba(248, 113, 113, 0.1); border-color: rgba(248, 113, 113, 0.2); color: #f87171; }

/* ─── Light Theme Overrides ─────────────────────────────────────── */

.log-viewer.light-theme {
  background-color: #fafafa;
  border-color: #dcdfe6;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.06);
}

.log-viewer.light-theme .log-toolbar {
  background-color: #f5f5f5;
  border-bottom-color: #e0e0e0;
}

.log-viewer.light-theme .log-file-select :deep(.el-input__wrapper) {
  background-color: #fff;
  box-shadow: 0 0 0 1px #dcdfe6 inset;
}
.log-viewer.light-theme .log-file-select :deep(.el-input__inner) {
  color: #333;
}

.log-viewer.light-theme .connection-badge {
  border-color: #dcdfe6;
}

.log-viewer.light-theme .log-search :deep(.el-input__wrapper) {
  background-color: #fff;
  box-shadow: 0 0 0 1px #dcdfe6 inset;
}
.log-viewer.light-theme .log-search :deep(.el-input__inner) {
  color: #333;
}
.log-viewer.light-theme .log-search :deep(.el-input__icon) {
  color: #999;
}

.log-viewer.light-theme .terminal-body {
  background-color: #fafafa;
}

.log-viewer.light-theme .terminal-body::-webkit-scrollbar-track {
  background: #fafafa;
}
.log-viewer.light-theme .terminal-body::-webkit-scrollbar-thumb {
  border: 2px solid #fafafa;
}

.log-viewer.light-theme .empty-state {
  color: #bbb;
}

.log-viewer.light-theme .log-line {
  color: #333;
}
.log-viewer.light-theme .log-line:hover {
  background-color: rgba(0, 0, 0, 0.03);
}

.log-viewer.light-theme .log-line.level-info {
  color: #333;
}
.log-viewer.light-theme .log-line.level-warn {
  color: #b45309;
}
.log-viewer.light-theme .log-line.level-error {
  color: #dc2626;
}
.log-viewer.light-theme .log-line.level-debug {
  color: #7c3aed;
}
.log-viewer.light-theme .log-line.level-default {
  color: #666;
}

.log-viewer.light-theme .line-number {
  color: #bbb;
}

.log-viewer.light-theme .terminal-statusbar {
  background-color: #f5f5f5;
  border-top-color: #e0e0e0;
  color: #999;
}

.log-viewer.light-theme .status-item.clickable {
  color: #2563eb;
}
.log-viewer.light-theme .status-item.clickable:hover {
  color: #3b82f6;
}

/* Level filter dropdown */
.level-filter {
  width: 150px;
}
.level-filter :deep(.el-input__wrapper) {
  background-color: #1e1e1e;
  box-shadow: 0 0 0 1px #333 inset;
}
.level-filter :deep(.el-input__inner) {
  color: #e0e0e0;
  font-family: inherit;
}

.log-viewer.light-theme .level-filter :deep(.el-input__wrapper) {
  background-color: #fff;
  box-shadow: 0 0 0 1px #dcdfe6 inset;
}
.log-viewer.light-theme .level-filter :deep(.el-input__inner) {
  color: #333;
}

/* Level dots in dropdown */
.level-dot {
  display: inline-block;
  width: 8px;
  height: 8px;
  border-radius: 50%;
  margin-right: 6px;
}
.error-dot { background-color: #f87171; }
.warn-dot { background-color: #fbbf24; }
.info-dot { background-color: #60a5fa; }
.debug-dot { background-color: #a78bfa; }

/* Level badge in status bar */
.level-badge {
  padding: 1px 8px;
  border-radius: 4px;
  font-size: 11px;
  font-weight: 600;
}
.badge-error { background-color: rgba(248, 113, 113, 0.15); color: #f87171; }
.badge-warn { background-color: rgba(251, 191, 36, 0.15); color: #fbbf24; }
.badge-info { background-color: rgba(96, 165, 250, 0.15); color: #60a5fa; }
.badge-debug { background-color: rgba(167, 139, 250, 0.15); color: #a78bfa; }

.log-viewer.light-theme .badge-error { background-color: rgba(220, 38, 38, 0.1); color: #dc2626; }
.log-viewer.light-theme .badge-warn { background-color: rgba(180, 83, 9, 0.1); color: #b45309; }
.log-viewer.light-theme .badge-info { background-color: rgba(37, 99, 235, 0.1); color: #2563eb; }
.log-viewer.light-theme .badge-debug { background-color: rgba(124, 58, 237, 0.1); color: #7c3aed; }
</style>