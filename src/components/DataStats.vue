<template>
  <StatsBase 
    :title="title"
    :sub-title="subTitle"
    :summary="summary"
    :file-name-prefix="fileNamePrefix"
  >
    <!-- 列表视图 -->
    <div v-if="viewMode === 'list'" class="stats-list">
      <div v-if="filteredData.length === 0" class="empty-tip">
        {{ emptyText }}
      </div>
      <div v-else v-for="(item) in filteredData" :key="item.name" class="user-stats-row">
        <div class="user-name-col" :title="`${item.name}${item.uid ? ' (ID: ' + item.uid + ')' : ''}`">
          <div class="user-name-text">{{ item.name }}</div>
          <div class="user-id-text" v-if="item.uid">ID: {{ item.uid }}</div>
        </div>
        <div class="bar-col">
          <div class="bar-bg">
            <div 
              class="bar-fill" 
              :style="{ width: (item.value / maxValue * 100) + '%' }"
            ></div>
          </div>
        </div>
        <div class="count-col">
          {{ formatValue(item.value) }}
        </div>
      </div>
    </div>
    
    <!-- 扇形图视图 -->
    <div v-else class="chart-wrapper">
      <div ref="pieChartRef" class="pie-chart"></div>
    </div>

    <template #filters>
      <div v-if="viewMode === 'list'" class="filter-item">
        <span class="label">{{ filterLabel }}:</span>
        <el-slider 
          v-model="minValue" 
          :min="0" 
          :max="maxSliderValue" 
          class="count-slider"
        />
        <el-input-number 
          v-model="minValue" 
          :min="0" 
          :max="maxSliderValue" 
          size="small" 
          controls-position="right"
          class="count-input"
        />
      </div>
      <div v-else class="filter-item">
        <span class="label">显示前 {{ topN }} 名:</span>
        <el-slider 
          v-model="topN" 
          :min="minTopN" 
          :max="maxTopN" 
          class="count-slider"
        />
        <el-input-number 
          v-model="topN" 
          :min="minTopN" 
          :max="maxTopN" 
          size="small" 
          controls-position="right"
          class="count-input"
        />
      </div>
    </template>

    <template #actions>
      <el-radio-group v-model="viewMode" size="small" @change="handleViewChange" class="view-toggle custom-toggle">
        <el-radio-button value="list" :title="listTitle">
          <el-icon><Menu v-if="type === 'danmaku'" /><User v-else /></el-icon>
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
import { Menu, User, PieChart } from '@element-plus/icons-vue';
import * as echarts from 'echarts';
import StatsBase from './StatsBase.vue';

interface StatsItem {
  name: string;
  value: number;
  uid?: string;
}

const props = defineProps<{
  type: 'danmaku' | 'revenue';
  title: string;
  subTitle: string;
  summary: string;
  fileNamePrefix: string;
  data: StatsItem[];
  emptyText?: string;
  filterLabel: string;
  listTitle?: string;
  unit?: string;
  defaultTopN?: number;
}>();

const viewMode = ref<'list' | 'pie'>('list');
const minValue = ref(props.type === 'danmaku' ? 1 : 0);
const topN = ref(props.defaultTopN || (props.type === 'danmaku' ? 5 : 5));
const pieChartRef = ref<HTMLElement | null>(null);
let chartInstance: echarts.ECharts | null = null;

const formatValue = (val: number) => {
  if (props.type === 'revenue') {
    return `¥${val.toFixed(1)}`;
  }
  return val.toString();
};

const maxValue = computed(() => props.data.length > 0 ? props.data[0].value : 0);

const filteredData = computed(() => {
  if (viewMode.value === 'list') {
    return props.data.filter(item => item.value >= minValue.value);
  } else {
    return props.data.slice(0, topN.value);
  }
});

const maxSliderValue = computed(() => {
  return props.data.length > 0 ? Math.ceil(props.data[0].value) : 100;
});

const minTopN = computed(() => props.type === 'danmaku' ? 5 : 1);
const maxTopN = computed(() => Math.max(props.data.length, 20));

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
  if (chartInstance) chartInstance.dispose();
  chartInstance = echarts.init(pieChartRef.value);
  updateChart();
};

const updateChart = () => {
  if (!chartInstance || viewMode.value !== 'pie') return;

  const chartData = filteredData.value.map(item => ({
    name: item.name,
    value: item.value
  }));

  const othersValue = props.data
    .slice(topN.value)
    .reduce((sum, item) => sum + item.value, 0);

  if (othersValue > 0) {
    chartData.push({
      name: '其他',
      value: props.type === 'revenue' ? Math.round(othersValue * 10) / 10 : othersValue
    });
  }

  const isMobile = window.innerWidth <= 768;
  const valueFormatter = props.type === 'revenue' ? '¥{c}' : '{c}';

  const option: echarts.EChartsOption = {
    tooltip: {
      trigger: 'item',
      formatter: `{b}: ${valueFormatter} ({d}%)`
    },
    legend: {
      show: isMobile,
      bottom: 0,
      left: 'center',
      type: 'scroll',
      orient: 'horizontal',
      itemWidth: 10,
      itemHeight: 10,
      textStyle: { fontSize: 12 }
    },
    series: [
      {
        name: props.title,
        type: 'pie',
        radius: isMobile ? '60%' : '70%',
        center: isMobile ? ['50%', '40%'] : ['50%', '50%'],
        avoidLabelOverlap: true,
        itemStyle: {
          borderRadius: 4,
          borderColor: '#fff',
          borderWidth: 1
        },
        label: {
          show: true,
          position: isMobile ? 'inner' : 'outside',
          formatter: isMobile ? '{b}' : `{b}: ${valueFormatter} ({d}%)`,
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
            formatter: `{b}: ${valueFormatter} ({d}%)`
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

watch([filteredData, topN, minValue], () => {
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
.stats-list {
  height: 100%;
  overflow-y: auto;
  padding: 10px 5px;
}

.user-stats-row {
  display: flex;
  align-items: center;
  padding: 12px 16px;
  border-radius: 8px;
  transition: all 0.2s ease;
  gap: 16px;
}

.user-stats-row:hover {
  background: var(--el-fill-color-light);
}

.user-name-col {
  width: 120px;
  flex-shrink: 0;
  overflow: hidden;
}

.user-name-text {
  font-weight: 500;
  color: var(--el-text-color-primary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  font-size: 14px;
}

.user-id-text {
  font-size: 11px;
  color: var(--el-text-color-secondary);
  margin-top: 2px;
}

.bar-col {
  flex: 1;
  min-width: 0;
}

.bar-bg {
  height: 12px;
  background: var(--el-fill-color-lighter);
  border-radius: 6px;
  overflow: hidden;
}

.bar-fill {
  height: 100%;
  background: linear-gradient(90deg, var(--el-color-primary-light-3), var(--el-color-primary));
  border-radius: 6px;
  transition: width 0.6s cubic-bezier(0.34, 1.56, 0.64, 1);
}

.count-col {
  width: 80px;
  text-align: right;
  font-family: 'PingFang SC', 'Helvetica Neue', Arial, sans-serif;
  font-weight: 600;
  color: var(--el-color-primary);
  font-size: 14px;
}

.chart-wrapper {
  height: 100%;
  width: 100%;
  display: flex;
  flex-direction: column;
  padding: 20px;
  box-sizing: border-box;
}

.pie-chart {
  flex: 1;
  width: 100%;
  min-height: 300px;
}

.filter-item {
  display: flex;
  align-items: center;
  gap: 12px;
  flex: 1;
}

.label {
  font-size: 13px;
  color: var(--el-text-color-regular);
  white-space: nowrap;
  min-width: 80px;
}

.count-slider {
  flex: 1;
  max-width: 200px;
}

.count-input {
  width: 90px !important;
}

.empty-tip {
  text-align: center;
  padding: 40px;
  color: var(--el-text-color-secondary);
  font-size: 14px;
}

@media (max-width: 768px) {
  .user-stats-row {
    padding: 10px 8px;
    gap: 10px;
  }
  .user-name-col {
    width: 90px;
  }
  .count-col {
    width: 60px;
    font-size: 13px;
  }
  .label {
    min-width: auto;
  }
  .count-slider {
    display: none;
  }
}
</style>
