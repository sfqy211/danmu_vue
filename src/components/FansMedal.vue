<template>
  <div
    v-if="item.medalName && item.medalLevel != null"
    class="fans-medal"
    :class="{
      'fans-medal-lightened': item.medalIsLight,
      'fans-medal-has-guard': !!item.medalGuardLevel,
    }"
    :style="{ '--medal-color': mainColor }"
  >
    <div class="fans-medal-content">
      <i v-if="item.medalGuardLevel" class="medal-deco medal-guard" :class="`medal-guard--${item.medalGuardLevel}`"></i>
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
  if (lvl <= 10) return 1;
  if (lvl <= 20) return 2;
  if (lvl <= 30) return 3;
  if (lvl <= 40) return 4;
  if (lvl <= 50) return 5;
  if (lvl <= 60) return 6;
  return 7;
});

const colorMap: Record<number, string> = {
  1: '#5963A5',
  2: '#C474A4',
  3: '#4AB3E7',
  4: '#5781E4',
  5: '#A779E6',
  6: '#E05673',
  7: '#F08632',
};

const mainColor = computed(() => colorMap[tier.value] ?? '#5963A5');
</script>

<style scoped>
/* ═══════ Fans Medal — Bilibili-style pill badge ═══════ */

.fans-medal {
  display: inline-flex;
  align-items: center;
  border-radius: 999px;
  overflow: hidden;
  font-size: 0.7rem;
  line-height: 1;
  height: 18px;
  flex-shrink: 0;
  cursor: default;
  user-select: none;
  font-weight: 600;
  color: #fff;
  /* Default: not lightened → grey */
  background-image: linear-gradient(45deg, rgba(145, 146, 152, 0.8), rgba(145, 146, 152, 0.8));
}

/* ─── Lightened (点亮态) — show actual tier color ─── */
.fans-medal-lightened {
  background-image: none !important;
  background-color: var(--medal-color);
}

/* ─── Left: guard icon + medal name ─── */
.fans-medal-content {
  display: inline-flex;
  align-items: center;
  gap: 2px;
  padding: 0 4px 0 3px;
  height: 100%;
  white-space: nowrap;
  color: #fff;
}

/* ─── Guard icon (local assets — keep paths in sync with constants/guardIcon.ts) ─── */
.medal-deco.medal-guard {
  display: inline-block;
  width: 10px;
  height: 10px;
  flex-shrink: 0;
  background-size: contain;
  background-repeat: no-repeat;
  background-position: center;
}

.medal-guard--1 { background-image: url('/guard-icon/governor.webp'); }
.medal-guard--2 { background-image: url('/guard-icon/admiral.webp'); }
.medal-guard--3 { background-image: url('/guard-icon/captain.webp'); }

/* ─── Right: level number ─── */
.fans-medal-level {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 0 4px 0 2px;
  height: 100%;
  font-weight: 700;
  color: #fff;
}
</style>