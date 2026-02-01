<template>
  <div class="stats-container" id="stats-content" ref="statsContent" :class="{ 'exporting': isExporting }">
    <div class="stats-header">
      <div class="title-row">
        <h2 class="stats-title">弹幕发送统计 - {{ streamerName }}</h2>
        <div class="session-time" v-if="sessionTime">{{ sessionTime }}</div>
      </div>
      <div class="stats-summary">
        显示 {{ filteredStats.length }} / {{ stats.length }} 位用户 | 总弹幕 {{ totalDanmaku }} 条 | 1 场直播
      </div>
    </div>
    
    <div class="stats-list-wrapper">
      <!-- 列表视图 -->
      <div v-if="viewMode === 'bar'" class="stats-list">
        <div v-if="filteredStats.length === 0" class="empty-tip">
          没有匹配的用户
        </div>
        <div v-else v-for="(item, index) in filteredStats" :key="item.name" class="user-stats-row">
          <div class="user-name-col" :title="`${item.name}${item.uid ? ' (ID: ' + item.uid + ')' : ''}`">
            <div class="user-name-text">{{ item.name }}</div>
            <div class="user-id-text" v-if="item.uid">ID: {{ item.uid }}</div>
          </div>
          <div class="bar-col">
            <div class="bar-bg">
              <div 
                class="bar-fill" 
                :style="{ width: (item.count / maxCount * 100) + '%' }"
              ></div>
            </div>
          </div>
          <div class="count-col">
            {{ item.count }}
          </div>
        </div>
      </div>
      
      <!-- 扇形图视图 -->
      <div v-else class="chart-wrapper">
        <div ref="pieChartRef" class="pie-chart"></div>
      </div>
    </div>

    <div class="stats-toolbar" v-if="!isExporting">
      <div class="filter-group">
        <template v-if="viewMode === 'bar'">
          <span class="label">最少弹幕数:</span>
          <el-slider 
            v-model="minCount" 
            :min="1" 
            :max="Math.max(1, maxCount)" 
            class="count-slider"
            :show-tooltip="false"
          />
          <el-input-number 
            v-model="minCount" 
            :min="1" 
            :max="Math.max(1, maxCount)" 
            size="small" 
            controls-position="right"
            class="count-input"
          />
        </template>
        <template v-else>
          <span class="label">显示前 N 名:</span>
          <el-slider 
            v-model="topN" 
            :min="5" 
            :max="20" 
            :step="1"
            class="count-slider"
            :show-tooltip="true"
          />
          <el-input-number 
            v-model="topN" 
            :min="5" 
            :max="20" 
            size="small" 
            controls-position="right"
            class="count-input"
          />
        </template>
      </div>
      <div class="action-group">
        <el-radio-group v-model="viewMode" size="small" @change="handleViewChange" class="view-toggle custom-toggle">
          <el-radio-button value="bar">
            <el-icon><Menu /></el-icon>
          </el-radio-button>
          <el-radio-button value="pie">
            <el-icon><PieChart /></el-icon>
          </el-radio-button>
        </el-radio-group>
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
import { ref, computed, nextTick, onMounted, onUnmounted, watch } from 'vue';
import { useDanmakuStore } from '../stores/danmakuStore';
import { ElMessage } from 'element-plus';
import { Menu, PieChart } from '@element-plus/icons-vue';
import html2canvas from 'html2canvas';
import * as echarts from 'echarts';

const store = useDanmakuStore();
const minCount = ref(1);
const topN = ref(5);
const statsContent = ref<HTMLElement | null>(null);
const isExporting = ref(false);
const exportLoading = ref(false);
const copyLoading = ref(false);
const viewMode = ref<'bar' | 'pie'>('bar');
const pieChartRef = ref<HTMLElement | null>(null);
let chartInstance: echarts.ECharts | null = null;

const streamerName = computed(() => store.currentSession?.user_name || '未知主播');

const sessionTime = computed(() => {
  const startTime = store.currentSession?.start_time;
  if (!startTime) return '';
  const date = new Date(startTime);
  return date.toLocaleString('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false
  }).replace(/\//g, '-');
});

const handleExport = async () => {
  if (!statsContent.value || exportLoading.value) return;
  
  exportLoading.value = true;
  isExporting.value = true;
  document.body.classList.add('exporting-active');
  
  try {
    // 等待 DOM 更新（隐藏工具栏）
    await nextTick();
    
    const canvas = await html2canvas(statsContent.value, {
      backgroundColor: getComputedStyle(document.body).getPropertyValue('--bg-primary'),
      scale: 2,
      logging: false,
      useCORS: true,
      allowTaint: true,
      windowWidth: 600,
    });
    
    const link = document.createElement('a');
    const timestamp = new Date().getTime();
    link.download = `弹幕统计_${streamerName.value}_${timestamp}.png`;
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
  if (!statsContent.value || copyLoading.value) return;
  
  copyLoading.value = true;
  isExporting.value = true;
  document.body.classList.add('exporting-active');
  
  try {
    await nextTick();
    
    const canvas = await html2canvas(statsContent.value, {
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

const stats = computed(() => {
  if (store.sessionSummary && store.sessionSummary.userStats) {
    // Use full session stats from server
    return Object.entries(store.sessionSummary.userStats)
      .map(([name, data]: [string, any]) => ({
        name,
        count: data.count,
        scCount: data.scCount,
        uid: data.uid || ''
      }))
      .sort((a, b) => b.count - a.count);
  }

  // Fallback: Process partial data from store.danmakuList
  const userStats: Record<string, { count: number; scCount: number; firstTime: number; uid: string }> = {};
  
  store.danmakuList.forEach(d => {
    if (!userStats[d.user]) {
      userStats[d.user] = {
        count: 0,
        scCount: 0,
        firstTime: d.timestamp,
        uid: d.uid || ''
      };
    }
    userStats[d.user].count++;
    if (d.isSC) {
      userStats[d.user].scCount++;
    }
  });

  const sorted = Object.entries(userStats)
    .map(([name, data]) => ({
      name,
      ...data
    }))
    .sort((a, b) => {
      if (b.count !== a.count) return b.count - a.count;
      return a.firstTime - b.firstTime;
    });

  return sorted;
});

const totalDanmaku = computed(() => {
  if (store.sessionSummary && store.sessionSummary.totalCount) {
    return store.sessionSummary.totalCount;
  }
  return store.danmakuList.length;
});
const maxCount = computed(() => stats.value.length > 0 ? stats.value[0].count : 0);

const filteredStats = computed(() => {
  return stats.value.filter(item => item.count >= minCount.value);
});

const handleViewChange = async () => {
  if (viewMode.value === 'pie') {
    await nextTick();
    initChart();
  } else {
    chartInstance?.dispose();
    chartInstance = null;
  }
};

const initChart = () => {
  if (!pieChartRef.value) return;
  
  if (chartInstance) {
    chartInstance.dispose();
  }
  
  chartInstance = echarts.init(pieChartRef.value);
  updateChart();
};

const updateChart = () => {
  if (!chartInstance || viewMode.value !== 'pie') return;

  // Clone and sort data
  const sortedData = [...stats.value].sort((a, b) => b.count - a.count);
  
  // Take top N
  const topData = sortedData.slice(0, topN.value);
  
  // Calculate others
  const othersCount = sortedData.slice(topN.value).reduce((sum, item) => sum + item.count, 0);
  
  const chartData = topData.map(item => ({
    name: item.name,
    value: item.count
  }));

  if (othersCount > 0) {
    chartData.push({
      name: '其他',
      value: othersCount
    });
  }

  const isMobile = window.innerWidth <= 768;

  const option: echarts.EChartsOption = {
    tooltip: {
      trigger: 'item',
      formatter: '{b}: {c} ({d}%)'
    },
    legend: {
      show: isMobile,
      bottom: 0,
      left: 'center',
      type: 'scroll',
      orient: 'horizontal',
      itemWidth: 10,
      itemHeight: 10,
      textStyle: {
        fontSize: 12
      }
    },
    series: [
      {
        name: '弹幕统计',
        type: 'pie',
        radius: isMobile ? ['30%', '60%'] : ['40%', '70%'],
        center: isMobile ? ['50%', '40%'] : ['50%', '50%'],
        avoidLabelOverlap: true,
        itemStyle: {
          borderRadius: 10,
          borderColor: '#fff',
          borderWidth: 2
        },
        label: {
          show: true,
          position: isMobile ? 'inner' : 'outside',
          formatter: isMobile ? '{b}' : '{b}: {c} ({d}%)',
          fontSize: isMobile ? 10 : 14,
          color: isMobile ? '#fff' : 'inherit',
          textBorderColor: isMobile ? 'rgba(0,0,0,0.5)' : 'none',
          textBorderWidth: isMobile ? 2 : 0
        },
        emphasis: {
          label: {
            show: true,
            fontSize: isMobile ? 12 : 18,
            fontWeight: 'bold',
            position: isMobile ? 'inner' : 'outside',
            formatter: '{b}: {c} ({d}%)'
          }
        },
        labelLine: {
          show: !isMobile,
          length: 15,
          length2: 10,
          smooth: true
        },
        data: chartData
      }
    ]
  };

  chartInstance.setOption(option);
};

watch([filteredStats, topN], () => {
  if (viewMode.value === 'pie') {
    updateChart();
  }
});

onMounted(() => {
  window.addEventListener('resize', handleResize);
});

onUnmounted(() => {
  window.removeEventListener('resize', handleResize);
  chartInstance?.dispose();
});

const handleResize = () => {
  chartInstance?.resize();
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

.stats-container.exporting .stats-list-wrapper {
  height: auto !important;
  max-height: none !important;
  overflow: visible !important;
}

.stats-container.exporting .stats-list {
  height: auto !important;
  max-height: none !important;
  overflow: visible !important;
  padding-right: 0 !important;
}

.stats-container.exporting .user-stats-row {
  border-bottom: 1px solid #eee; /* Ensure borders are visible in export */
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

.filter-group .label {
  font-size: 0.9rem;
  white-space: nowrap;
  color: var(--text-primary);
}

.count-slider {
  flex: 1;
  max-width: 200px;
}

.count-input {
  width: 90px;
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

.stats-list-wrapper {
  flex: 1;
  overflow: hidden;
  margin-top: 10px;
}

.stats-list {
  height: 100%;
  overflow-y: auto;
  padding-right: 8px;
}

.user-stats-row {
  display: flex;
  align-items: center;
  padding: 10px 0;
  border-bottom: 1px solid var(--border);
  gap: 16px;
}

.user-name-col {
  width: 120px;
  display: flex;
  flex-direction: column;
  justify-content: center;
  gap: 2px;
}

.user-name-text {
  font-size: 0.9rem;
  color: var(--text-primary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  line-height: 1.2;
}

.user-id-text {
  font-size: 0.75rem;
  color: var(--text-tertiary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  line-height: 1.2;
}

.bar-col {
  flex: 1;
  padding: 0 4px;
}

.bar-bg {
  height: 10px;
  background-color: var(--bg-hover);
  border-radius: 5px;
  overflow: hidden;
}

.bar-fill {
  height: 100%;
  background-color: #0071e3;
  border-radius: 5px;
  transition: width 0.4s cubic-bezier(0.4, 0, 0.2, 1);
}

.count-col {
  width: 50px;
  text-align: right;
  font-size: 0.95rem;
  color: var(--text-secondary);
  font-weight: 500;
}

.empty-tip {
  text-align: center;
  color: var(--text-tertiary);
  padding: 40px 0;
}

/* Custom Scrollbar */
.stats-list::-webkit-scrollbar {
  width: 6px;
}

.stats-list::-webkit-scrollbar-track {
  background: transparent;
}

.stats-list::-webkit-scrollbar-thumb {
  background: var(--scrollbar-thumb);
  border-radius: 3px;
}

/* Deep overrides for element-plus */
:deep(.el-slider__bar) {
  background-color: #0071e3;
}

:deep(.el-slider__button) {
  border-color: #0071e3;
}

:deep(.el-input-number.is-controls-right .el-input-number__increase),
:deep(.el-input-number.is-controls-right .el-input-number__decrease) {
  background-color: var(--bg-hover);
  border-left: 1px solid var(--border);
}

.chart-wrapper {
  height: 100%;
  width: 100%;
  display: flex;
  justify-content: center;
  align-items: center;
}

.pie-chart {
  width: 100%;
  height: 100%;
  min-height: 400px;
}

.view-toggle {
  margin-right: 10px;
}

.custom-toggle :deep(.el-radio-button__inner) {
  padding: 8px 12px;
}

@media (max-width: 768px) {
  .stats-container {
    height: calc(100vh - 54px); /* 减去对话框头部高度 */
    padding: 0 12px 10px;
  }

  .user-name-col {
    width: 90px;
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

  .count-slider {
    order: 3;
    width: 100%;
    margin: 5px 10px !important;
    flex: none;
  }

  .count-input {
    order: 2;
  }

  .action-group {
    margin-top: 5px;
    justify-content: center;
  }

  .export-btn,
  .copy-btn {
    display: none !important;
  }

  .custom-toggle :deep(.el-radio-button__inner) {
    padding: 6px 10px;
  }

  .chart-container {
    height: 350px !important;
  }
}
</style>
