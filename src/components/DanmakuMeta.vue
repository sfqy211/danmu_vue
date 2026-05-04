<template>
  <span class="dm-meta-badges">
    <span v-if="item.medalLevel != null && item.medalName" class="meta-badge medal"
      :class="{ 'medal-light': item.medalIsLight }">
      {{ item.medalName }} {{ item.medalLevel }}
    </span>
    <span v-if="item.ulLevel" class="meta-badge ul">UL{{ item.ulLevel }}</span>
    <span v-if="item.wealthLevel" class="meta-badge wealth">财{{ item.wealthLevel }}</span>
    <span v-if="item.guardLevel" class="meta-badge guard">{{ getGuardName(item.guardLevel) }}</span>
    <span v-if="item.coinType" class="meta-badge coin">{{ item.coinType }}</span>
  </span>
</template>

<script setup lang="ts">
import type { Danmaku } from '../api/danmaku';

defineProps<{ item: Danmaku }>();

const getGuardName = (level?: number) => {
  switch (level) {
    case 1: return '总督';
    case 2: return '提督';
    case 3: return '舰长';
    default: return '';
  }
};
</script>

<style scoped>
.dm-meta-badges {
  display: inline-flex;
  align-items: center;
  gap: 3px;
  flex-shrink: 0;
  margin-left: 4px;
}

.meta-badge {
  display: inline-flex;
  align-items: center;
  padding: 0 4px;
  border-radius: 3px;
  font-size: 0.7rem;
  font-weight: 600;
  white-space: nowrap;
  line-height: 1.5;
}

.meta-badge.medal {
  background: linear-gradient(135deg, rgba(64, 158, 255, 0.12), rgba(121, 187, 255, 0.08));
  color: #409eff;
  border: 1px solid rgba(64, 158, 255, 0.25);
}

.meta-badge.medal.medal-light {
  background: linear-gradient(135deg, rgba(255, 193, 7, 0.15), rgba(255, 213, 79, 0.08)) !important;
  color: #e6a23c !important;
  border-color: rgba(230, 162, 60, 0.35) !important;
}

.meta-badge.ul {
  background-color: rgba(103, 194, 58, 0.12);
  color: #67c23a;
}

.meta-badge.wealth {
  background-color: rgba(230, 162, 60, 0.12);
  color: #e6a23c;
}

.meta-badge.guard {
  background-color: rgba(171, 26, 50, 0.12);
  color: #AB1A32;
}

.meta-badge.coin {
  background-color: rgba(144, 147, 153, 0.12);
  color: #909399;
}
</style>
