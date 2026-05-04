<template>
  <div
    v-if="item.medalName && item.medalLevel != null"
    class="fans-medal"
    :class="{
      'fans-medal-light': item.medalIsLight,
      [`fans-medal-tier--${tier}`]: true,
    }"
  >
    <div class="fans-medal-content">
      <div
        v-if="item.medalGuardLevel"
        class="guard-badge-in-fans-medal"
        :class="`guard-level--${item.medalGuardLevel}`"
      />
      {{ item.medalName }}
    </div>
    <div class="fans-medal-level">{{ item.medalLevel }}</div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import type { Danmaku } from '../api/danmaku';

const props = defineProps<{ item: Danmaku }>();

const tier = computed(() => {
  const lvl = props.item.medalLevel ?? 0;
  if (lvl <= 4) return 1;
  if (lvl <= 8) return 2;
  if (lvl <= 12) return 3;
  if (lvl <= 16) return 4;
  if (lvl <= 20) return 5;
  if (lvl <= 24) return 6;
  if (lvl <= 28) return 7;
  if (lvl <= 32) return 8;
  if (lvl <= 36) return 9;
  return 10;
});
</script>

<style scoped>
.fans-medal {
  display: inline-flex;
  align-items: center;
  border-radius: 3px;
  overflow: hidden;
  font-size: 0.7rem;
  line-height: 1;
  height: 18px;
  flex-shrink: 0;
  cursor: default;
  user-select: none;
  font-weight: 600;
}

.fans-medal-content {
  display: inline-flex;
  align-items: center;
  gap: 2px;
  padding: 0 4px;
  height: 100%;
  color: #fff;
  white-space: nowrap;
}

.fans-medal-level {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 0 3px;
  height: 100%;
  background-color: #fff;
  color: #333;
  font-weight: 700;
  min-width: 14px;
  text-align: center;
}

/* Guard dot inside medal */
.guard-badge-in-fans-medal {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  flex-shrink: 0;
}
.guard-level--1 { background-color: #f25d8e; }
.guard-level--2 { background-color: #f25d8e; }
.guard-level--3 { background-color: #f25d8e; }

/* Tier colors (default / not lightened) */
.fans-medal-tier--1 .fans-medal-content { background: linear-gradient(90deg, #969696, #b0b0b0); }
.fans-medal-tier--2 .fans-medal-content { background: linear-gradient(90deg, #5d8ac4, #7aa3d6); }
.fans-medal-tier--3 .fans-medal-content { background: linear-gradient(90deg, #5dc4a6, #7dd6bc); }
.fans-medal-tier--4 .fans-medal-content { background: linear-gradient(90deg, #c4a65d, #d6bc7d); }
.fans-medal-tier--5 .fans-medal-content { background: linear-gradient(90deg, #c47d5d, #d69a7d); }
.fans-medal-tier--6 .fans-medal-content { background: linear-gradient(90deg, #c45d5d, #d67d7d); }
.fans-medal-tier--7 .fans-medal-content { background: linear-gradient(90deg, #8a5dc4, #a67dd6); }
.fans-medal-tier--8 .fans-medal-content { background: linear-gradient(90deg, #6a4ca3, #8a6dc4); }
.fans-medal-tier--9 .fans-medal-content { background: linear-gradient(90deg, #d4a017, #e8c050); }
.fans-medal-tier--10 .fans-medal-content { background: linear-gradient(90deg, #b8860b, #daa520); }

/* Lightened variant (golden) */
.fans-medal-light .fans-medal-content {
  background: linear-gradient(90deg, #e6a23c, #f0c060) !important;
}
.fans-medal-light .fans-medal-level {
  color: #b8741a !important;
}
</style>
