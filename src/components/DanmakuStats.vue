<template>
  <DataStats
    type="danmaku"
    :title="`弹幕发送统计 - ${streamerName}`"
    :sub-title="sessionTime"
    :summary="`显示 ${stats.length} 位用户 | 总弹幕 ${totalDanmaku} 条 | 1 场直播`"
    :file-name-prefix="`弹幕统计_${streamerName}`"
    :data="stats"
    filter-label="最少弹幕数"
    empty-text="没有匹配的用户"
    list-title="用户排行"
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

const stats = computed(() => {
  if (store.sessionSummary && store.sessionSummary.userStats) {
    return Object.entries(store.sessionSummary.userStats)
      .map(([name, data]: [string, any]) => ({
        name,
        value: data.count,
        uid: data.uid || ''
      }))
      .sort((a, b) => b.value - a.value);
  }

  // Fallback: Process partial data from store.danmakuList
  const userStats: Record<string, { count: number; uid: string }> = {};
  
  store.danmakuList.forEach(d => {
    if (!userStats[d.user]) {
      userStats[d.user] = {
        count: 0,
        uid: d.uid || ''
      };
    }
    userStats[d.user].count++;
  });

  return Object.entries(userStats)
    .map(([name, data]) => ({
      name,
      value: data.count,
      uid: data.uid
    }))
    .sort((a, b) => b.value - a.value);
});

const totalDanmaku = computed(() => {
  if (store.sessionSummary && store.sessionSummary.totalCount) {
    return store.sessionSummary.totalCount;
  }
  return store.danmakuList.length;
});
</script>

<style scoped>
/* DataStats handles styles */
</style>
