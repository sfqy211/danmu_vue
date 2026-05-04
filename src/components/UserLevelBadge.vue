<template>
  <div
    v-if="level && level > 0"
    class="user-level"
    :class="`user-level--${tier}`"
    :style="{ borderColor: color, color: color }"
  >
    <span class="user-level-name">UL</span><span class="user-level-figure">{{ level }}</span>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue';

const props = defineProps<{ level?: number }>();

const tier = computed(() => {
  const lvl = props.level ?? 0;
  if (lvl <= 20) return 1;
  if (lvl <= 40) return 2;
  if (lvl <= 60) return 3;
  if (lvl <= 80) return 4;
  return 5;
});

const colorMap: Record<number, string> = {
  1: '#23ade5',
  2: '#61b057',
  3: '#ff86b2',
  4: '#ffa500',
  5: '#ff4444',
};

const color = computed(() => colorMap[tier.value] ?? '#23ade5');
</script>

<style scoped>
.user-level {
  display: inline-flex;
  align-items: center;
  border: 1px solid;
  border-radius: 3px;
  font-size: 0.65rem;
  line-height: 1;
  height: 16px;
  padding: 0 2px;
  flex-shrink: 0;
  font-weight: 600;
  white-space: nowrap;
}

.user-level-name {
  font-size: 0.6rem;
  opacity: 0.85;
}

.user-level-figure {
  font-size: 0.7rem;
  font-weight: 700;
}
</style>