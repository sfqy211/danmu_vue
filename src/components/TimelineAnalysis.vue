<template>
  <div class="analysis-wrapper">
    <div class="chart-container" ref="chartRef"></div>
    <div class="mobile-hint" v-if="isMobile">横屏查看效果更佳</div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch, nextTick, computed } from 'vue';
import * as echarts from 'echarts';
import { useDanmakuStore } from '../stores/danmakuStore';

const store = useDanmakuStore();
const chartRef = ref<HTMLElement | null>(null);
let chartInstance: echarts.ECharts | null = null;
const isMobile = ref(window.innerWidth <= 768);

const initChart = async () => {
  if (!chartRef.value) return;
  
  // Wait for DOM to be fully rendered
  await nextTick();
  
  // Small delay to ensure layout is settled
  setTimeout(() => {
    if (!chartRef.value) return;
    chartInstance = echarts.init(chartRef.value);
    updateChart();
    chartInstance.resize();
  }, 200);
};

const updateChart = () => {
  if (!chartInstance) return;

  let sortedKeys: string[] = [];
  let data: number[] = [];

  if (store.sessionSummary && store.sessionSummary.timeline) {
    store.sessionSummary.timeline.forEach(([ts, count]: [number, number]) => {
      const date = new Date(ts);
      const key = `${date.getHours().toString().padStart(2, '0')}:${date.getMinutes().toString().padStart(2, '0')}`;
      sortedKeys.push(key);
      data.push(count);
    });
  } else {
    const timeMap = new Map<string, number>();
    store.danmakuList.forEach(d => {
      const date = new Date(d.timestamp);
      const key = `${date.getHours().toString().padStart(2, '0')}:${date.getMinutes().toString().padStart(2, '0')}`;
      timeMap.set(key, (timeMap.get(key) || 0) + 1);
    });

    sortedKeys = Array.from(timeMap.keys()).sort();
    data = sortedKeys.map(k => timeMap.get(k) || 0);
  }

  const option = {
    title: {
      text: '弹幕热度趋势',
      left: 'center',
      textStyle: {
        fontSize: isMobile.value ? 14 : 18
      }
    },
    grid: {
      left: '3%',
      right: '4%',
      bottom: isMobile.value ? '15%' : '10%',
      containLabel: true
    },
    tooltip: {
      trigger: 'axis',
      confine: true
    },
    xAxis: {
      type: 'category',
      data: sortedKeys,
      axisLabel: {
        fontSize: isMobile.value ? 10 : 12
      }
    },
    yAxis: {
      type: 'value',
      axisLabel: {
        fontSize: isMobile.value ? 10 : 12
      }
    },
    series: [
      {
        data: data,
        type: 'line',
        smooth: true,
        areaStyle: {
          opacity: 0.3
        },
        itemStyle: {
          color: '#0071e3'
        }
      }
    ]
  };

  chartInstance.setOption(option);
};

onMounted(() => {
  initChart();
  window.addEventListener('resize', handleResize);
});

onUnmounted(() => {
  window.removeEventListener('resize', handleResize);
  chartInstance?.dispose();
});

const handleResize = () => {
  isMobile.value = window.innerWidth <= 768;
  nextTick(() => {
    chartInstance?.resize();
  });
};

watch([() => store.danmakuList, () => store.sessionSummary], () => {
  updateChart();
});
</script>

<style scoped>
.analysis-wrapper {
  width: 100%;
  height: 100%;
  position: relative;
}

.chart-container {
  width: 100%;
  height: 500px;
}

.mobile-hint {
  display: none;
}

@media (max-width: 768px) {
  .analysis-wrapper {
    height: calc(100vh - 54px);
    overflow: hidden;
    display: flex;
    align-items: center;
    justify-content: center;
  }

  .chart-container {
    /* 强制旋转 90 度实现横屏效果 */
    position: absolute;
    width: calc(100vh - 100px) !important; /* 减去边距 */
    height: 100vw !important;
    transform: rotate(90deg);
    transform-origin: center;
    z-index: 10;
  }

  .mobile-hint {
    display: block;
    position: absolute;
    bottom: 10px;
    right: 10px;
    font-size: 10px;
    color: var(--text-tertiary);
    opacity: 0.5;
    pointer-events: none;
  }
}
</style>
