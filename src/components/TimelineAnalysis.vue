<template>
  <div class="chart-container" ref="chartRef"></div>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch, nextTick } from 'vue';
import * as echarts from 'echarts';
import { useDanmakuStore } from '../stores/danmakuStore';

const store = useDanmakuStore();
const chartRef = ref<HTMLElement | null>(null);
let chartInstance: echarts.ECharts | null = null;

const initChart = async () => {
  if (!chartRef.value) return;
  
  // Wait for DOM to be fully rendered (especially width)
  await nextTick();
  // Small delay to ensure dialog animation or layout is settled
  setTimeout(() => {
    if (!chartRef.value) return;
    chartInstance = echarts.init(chartRef.value);
    updateChart();
    
    // Force resize to fit container
    chartInstance.resize();
  }, 100);
};

const updateChart = () => {
  if (!chartInstance) return;

  let sortedKeys: string[] = [];
  let data: number[] = [];

  if (store.sessionSummary && store.sessionSummary.timeline) {
    // Use full session timeline from server
    store.sessionSummary.timeline.forEach(([ts, count]: [number, number]) => {
      const date = new Date(ts);
      const key = `${date.getHours().toString().padStart(2, '0')}:${date.getMinutes().toString().padStart(2, '0')}`;
      sortedKeys.push(key);
      data.push(count);
    });
  } else {
    // Fallback: Process partial data from store.danmakuList
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
      left: 'center'
    },
    tooltip: {
      trigger: 'axis'
    },
    xAxis: {
      type: 'category',
      data: sortedKeys
    },
    yAxis: {
      type: 'value'
    },
    series: [
      {
        data: data,
        type: 'line',
        smooth: true,
        areaStyle: {}
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
  chartInstance?.resize();
};

watch([() => store.danmakuList, () => store.sessionSummary], () => {
  updateChart();
});
</script>

<style scoped>
.chart-container {
  width: 100%;
  height: 400px;
}
</style>
