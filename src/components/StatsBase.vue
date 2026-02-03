<template>
  <div class="stats-container" ref="containerRef" :class="{ 'exporting': isExporting }">
    <div class="stats-header">
      <div class="title-row">
        <h2 class="stats-title">{{ title }}</h2>
        <div class="session-time" v-if="subTitle">{{ subTitle }}</div>
      </div>
      <div class="stats-summary" v-if="summary">
        {{ summary }}
      </div>
    </div>
    
    <div class="stats-list-wrapper">
      <slot></slot>
    </div>

    <div class="stats-toolbar" v-if="!isExporting">
      <div class="filter-group">
        <slot name="filters"></slot>
      </div>
      <div class="action-group">
        <slot name="actions"></slot>
        <el-button type="primary" class="action-btn export-btn" size="default" @click="handleExport" :loading="exportLoading">
          <template #icon v-if="!exportLoading">
            <span class="btn-icon">📸</span>
          </template>
          {{ exportLoading ? '正在生成...' : '导出图片' }}
        </el-button>
        <el-button class="action-btn copy-btn" size="default" @click="handleCopy" :loading="copyLoading">
          <template #icon v-if="!copyLoading">
            <span class="btn-icon">📋</span>
          </template>
          {{ copyLoading ? '正在处理...' : '复制图片' }}
        </el-button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, nextTick } from 'vue';
import { ElMessage } from 'element-plus';
import html2canvas from 'html2canvas';

const props = defineProps<{
  title: string;
  subTitle?: string;
  summary?: string;
  fileNamePrefix?: string;
}>();

const containerRef = ref<HTMLElement | null>(null);
const isExporting = ref(false);
const exportLoading = ref(false);
const copyLoading = ref(false);

const handleExport = async () => {
  if (!containerRef.value || exportLoading.value) return;
  
  exportLoading.value = true;
  isExporting.value = true;
  document.body.classList.add('exporting-active');
  
  try {
    await nextTick();
    
    const canvas = await html2canvas(containerRef.value, {
      backgroundColor: getComputedStyle(document.body).getPropertyValue('--bg-primary'),
      scale: 2,
      logging: false,
      useCORS: true,
      allowTaint: true,
      windowWidth: 600,
    });
    
    const link = document.createElement('a');
    const timestamp = new Date().getTime();
    link.download = `${props.fileNamePrefix || '统计'}_${timestamp}.png`;
    link.href = canvas.toDataURL('image/png');
    link.click();
    
    ElMessage.success('图片已生成并开始下载');
  } catch (err) {
    console.error('导出失败:', err);
    ElMessage.error('导出失败，请重试');
  } finally {
    isExporting.value = false;
    exportLoading.value = false;
    document.body.classList.remove('exporting-active');
  }
};

const handleCopy = async () => {
  if (!containerRef.value || copyLoading.value) return;
  
  copyLoading.value = true;
  isExporting.value = true;
  document.body.classList.add('exporting-active');
  
  try {
    await nextTick();
    
    const canvas = await html2canvas(containerRef.value, {
      backgroundColor: getComputedStyle(document.body).getPropertyValue('--bg-primary'),
      scale: 2,
      logging: false,
      useCORS: true,
      allowTaint: true,
      windowWidth: 600,
    });
    
    await new Promise<void>((resolve, reject) => {
      canvas.toBlob(async (blob) => {
        try {
          if (!blob) throw new Error('Canvas to Blob conversion failed');
          const data = [new ClipboardItem({ 'image/png': blob })];
          await navigator.clipboard.write(data);
          ElMessage.success('图片已成功复制到剪贴板');
          resolve();
        } catch (err) {
          reject(err);
        }
      }, 'image/png');
    });
  } catch (err) {
    console.error('处理失败:', err);
    if ((err as Error).name === 'NotAllowedError' || (err as Error).name === 'SecurityError') {
      ElMessage.error('复制失败，写入剪贴板权限被拒绝');
    } else {
      ElMessage.error('处理失败，请重试');
    }
  } finally {
    isExporting.value = false;
    copyLoading.value = false;
    document.body.classList.remove('exporting-active');
  }
};
</script>

<style scoped>
.stats-container {
  display: flex;
  flex-direction: column;
  height: 70vh;
  background-color: var(--bg-primary);
  color: var(--text-primary);
  font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
  padding: 0 20px;
}

/* Exporting state styles */
.stats-container.exporting {
  height: auto !important;
  max-height: none !important;
  padding: 20px !important;
  width: 600px !important;
  overflow: visible !important;
}

.stats-container.exporting :deep(.stats-list-wrapper) {
  height: auto !important;
  max-height: none !important;
  overflow: visible !important;
}

.stats-container.exporting :deep(.stats-list) {
  height: auto !important;
  max-height: none !important;
  overflow: visible !important;
  padding-right: 0 !important;
}

.stats-container.exporting :deep(.user-stats-row) {
  border-bottom: 1px solid #eee;
}

.stats-header {
  padding: 8px 0 16px;
  flex-shrink: 0;
}

.title-row {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  margin-bottom: 8px;
}

.stats-title {
  font-size: 1.25rem;
  font-weight: 600;
  margin: 0;
  color: var(--text-primary);
}

.session-time {
  font-size: 0.9rem;
  color: var(--text-tertiary);
  font-variant-numeric: tabular-nums;
}

.stats-summary {
  font-size: 0.85rem;
  color: var(--text-tertiary);
}

.stats-list-wrapper {
  flex: 1;
  overflow: hidden;
  margin-top: 10px;
}

.stats-toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 12px 16px;
  background-color: var(--bg-secondary);
  border-radius: 12px;
  margin-top: 20px;
  gap: 20px;
  border-top: 1px solid var(--border);
}

.filter-group {
  display: flex;
  align-items: center;
  gap: 12px;
  flex: 1;
}

:deep(.filter-group .label) {
  font-size: 0.9rem;
  white-space: nowrap;
  color: var(--text-primary);
}

.action-group {
  display: flex;
  gap: 10px;
}

.action-btn {
  border-radius: 8px;
  font-weight: 500;
  display: flex;
  align-items: center;
  gap: 6px;
  border: none;
}

.export-btn {
  background-color: #0071e3;
}

.copy-btn {
  background-color: #8e8e93;
  color: white;
}

.copy-btn:hover {
  background-color: #7a7a7e;
  color: white;
}

.btn-icon {
  font-size: 1.1rem;
}

@media (max-width: 768px) {
  .stats-container {
    height: calc(100vh - 54px);
    padding: 0 12px 10px;
  }

  .stats-header {
    margin-bottom: 10px;
  }

  .stats-header h2 {
    font-size: 1.25rem;
  }

  .stats-summary {
    margin-bottom: 8px;
    padding-bottom: 5px;
    font-size: 0.85rem;
    color: #909399;
  }

  .stats-toolbar {
    padding: 10px;
    margin-top: 10px;
    margin-bottom: 0;
    gap: 8px;
    background: var(--bg-secondary);
    border-radius: 8px;
    flex-shrink: 0;
  }

  .filter-group {
    flex-wrap: wrap;
    gap: 5px;
  }

  .filter-group .label {
    font-size: 0.85rem;
    min-width: auto;
  }

  .action-group {
    margin-top: 5px;
    justify-content: center;
  }

  .export-btn,
  .copy-btn {
    display: none !important;
  }
}
</style>
