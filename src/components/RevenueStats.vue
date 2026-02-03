<template>
  <StatsBase 
    :title="`营收统计 - ${streamerName}`"
    :sub-title="sessionTime"
    :summary="summaryText"
    :file-name-prefix="`营收统计_${streamerName}`"
  >
    <!-- 列表视图 -->
    <div v-if="viewMode === 'user'" class="stats-list">
      <div v-if="userStatsList.length === 0" class="empty-tip">
        暂无营收数据
      </div>
      <div v-else v-for="(item) in filteredUserStats" :key="item.name" class="user-stats-row">
        <div class="user-name-col" :title="`${item.name}${item.uid ? ' (ID: ' + item.uid + ')' : ''}`">
          <div class="user-name-text">{{ item.name }}</div>
          <div class="user-id-text" v-if="item.uid">ID: {{ item.uid }}</div>
        </div>
        <div class="bar-col">
          <div class="bar-bg">
            <div 
              class="bar-fill" 
              :style="{ width: (item.totalPrice / maxUserPrice * 100) + '%' }"
            ></div>
          </div>
        </div>
        <div class="count-col">
          ¥{{ item.totalPrice.toFixed(1) }}
        </div>
      </div>
    </div>

    <!-- 扇形图视图 -->
    <div v-else class="chart-wrapper">
      <div ref="pieChartRef" class="pie-chart"></div>
    </div>

    <template #filters>
      <div v-if="viewMode === 'user'" class="filter-item">
        <span class="label">最少打赏金额:</span>
        <el-slider 
          v-model="minPrice" 
          :min="0" 
          :max="maxPossiblePrice" 
          class="count-slider"
        />
        <el-input-number 
          v-model="minPrice" 
          :min="0" 
          :max="maxPossiblePrice" 
          size="small" 
          controls-position="right"
          class="count-input"
        />
      </div>
      <div v-else class="filter-item">
        <span class="label">显示前 {{ topN }} 名:</span>
        <el-slider 
          v-model="topN" 
          :min="1" 
          :max="maxTopN" 
          class="count-slider"
        />
        <el-input-number 
          v-model="topN" 
          :min="1" 
          :max="maxTopN" 
          size="small" 
          controls-position="right"
          class="count-input"
        />
      </div>
    </template>

    <template #actions>
      <el-radio-group v-model="viewMode" size="small" @change="handleViewChange" class="view-toggle custom-toggle">
        <el-radio-button value="user" title="用户排行">
          <el-icon><User /></el-icon>
        </el-radio-button>
        <el-radio-button value="pie" title="占比分布">
          <el-icon><PieChart /></el-icon>
        </el-radio-button>
      </el-radio-group>
    </template>
  </StatsBase>
</template>

<script setup lang="ts">
import { ref, computed, nextTick, onMounted, onUnmounted, watch } from 'vue';
import { useDanmakuStore } from '../stores/danmakuStore';
import { User, PieChart } from '@element-plus/icons-vue';
import * as echarts from 'echarts';
import StatsBase from './StatsBase.vue';

const store = useDanmakuStore();
const minPrice = ref(0);
const topN = ref(20);
const viewMode = ref<'user' | 'pie'>('user');
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

// 解析 gift_summary_json
const giftData = computed(() => {
  if (!store.sessionSummary || !store.sessionSummary.gift_summary_json) {
    return null;
  }
  try {
    const raw = store.sessionSummary.gift_summary_json;
    return typeof raw === 'string' ? JSON.parse(raw) : raw;
  } catch (e) {
    console.error('Failed to parse gift_summary_json', e);
    return null;
  }
});

const userStatsList = computed(() => {
  if (!giftData.value || !giftData.value.userStats) return [];
  
  return Object.entries(giftData.value.userStats)
    .map(([name, stats]: [string, any]) => ({
      name,
      totalPrice: stats.totalPrice || 0,
      giftPrice: stats.giftPrice || 0,
      scPrice: stats.scPrice || 0,
      uid: stats.uid || ''
    }))
    .sort((a, b) => b.totalPrice - a.totalPrice);
});

const totalRevenue = computed(() => {
  return giftData.value?.totalPrice || 0;
});

const summaryText = computed(() => {
  if (!giftData.value) return '暂无营收数据';
  return `总营收 ¥${totalRevenue.value.toFixed(1)} | 参与人数 ${userStatsList.value.length} 人`;
});

const maxUserPrice = computed(() => userStatsList.value.length > 0 ? userStatsList.value[0].totalPrice : 0);

const filteredUserStats = computed(() => {
  if (viewMode.value === 'user') {
    return userStatsList.value.filter(item => item.totalPrice >= minPrice.value);
  } else {
    return userStatsList.value.slice(0, topN.value);
  }
});

const maxPossiblePrice = computed(() => {
  return userStatsList.value.length > 0 ? Math.ceil(userStatsList.value[0].totalPrice) : 100;
});

const maxTopN = computed(() => {
  return Math.max(userStatsList.value.length, 10);
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

  const data = filteredUserStats.value.map(item => ({
    name: item.name,
    value: item.totalPrice
  }));

  const othersPrice = userStatsList.value
    .slice(topN.value)
    .reduce((sum, item) => sum + item.totalPrice, 0);

  if (othersPrice > 0) {
    data.push({
      name: '其他',
      value: Math.round(othersPrice * 10) / 10
    });
  }

  const isMobile = window.innerWidth <= 768;

  const option: echarts.EChartsOption = {
    tooltip: {
      trigger: 'item',
      formatter: '{b}: ¥{c} ({d}%)'
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
        name: '营收分布',
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
          formatter: isMobile ? '{b}' : '{b}: ¥{c} ({d}%)',
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
            formatter: '{b}: ¥{c} ({d}%)'
          }
        },
        labelLine: {
          show: !isMobile,
          length: 15,
          length2: 10,
          smooth: true
        },
        data: data
      }
    ]
  };

  chartInstance.setOption(option);
};

watch([filteredUserStats, minPrice], () => {
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
/* Reuse styles from DanmakuStats/StatsBase where possible */
/* Specific list styles */
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
  background-color: #ff9500; /* Orange for money/gold */
  border-radius: 5px;
  transition: width 0.4s cubic-bezier(0.4, 0, 0.2, 1);
}

.count-col {
  width: 100px; /* Wider for price */
  text-align: right;
  font-size: 0.95rem;
  color: var(--text-secondary);
  font-weight: 500;
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  justify-content: center;
  line-height: 1.2;
}

.sub-text {
  font-size: 0.75rem;
  color: var(--text-tertiary);
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
  background-color: #ff9500;
}

:deep(.el-slider__button) {
  border-color: #ff9500;
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
  .user-name-col {
    width: 90px;
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

  .custom-toggle :deep(.el-radio-button__inner) {
    padding: 6px 10px;
  }
}
</style>
