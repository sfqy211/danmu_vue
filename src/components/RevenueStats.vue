<template>
  <DataStats
    type="revenue"
    :title="`营收统计 - ${streamerName}`"
    :sub-title="sessionTime"
    :summary="summaryText"
    :file-name-prefix="`营收统计_${streamerName}`"
    :data="userStatsList"
    filter-label="最少打赏金额"
    empty-text="暂无营收数据"
    list-title="用户排行"
    :default-top-n="5"
  />
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { useDanmakuStore } from '../stores/danmakuStore';
import DataStats from './DataStats.vue';

const store = useDanmakuStore();

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
      value: stats.totalPrice || 0,
      uid: stats.uid || ''
    }))
    .sort((a, b) => b.value - a.value);
});

const totalRevenue = computed(() => {
  return giftData.value?.totalPrice || 0;
});

const summaryText = computed(() => {
  if (!giftData.value) return '暂无营收数据';
  return `总营收 ¥${totalRevenue.value.toFixed(1)} | 参与人数 ${userStatsList.value.length} 人`;
});
</script>

<style scoped>
/* DataStats handles styles */
</style>
