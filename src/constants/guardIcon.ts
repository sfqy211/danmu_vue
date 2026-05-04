/**
 * Guard icon paths (local assets in public/guard-icon/).
 * Shared between DanmakuList (JS) and FansMedal (CSS url).
 * CSS cannot import JS constants, so keep paths in sync manually.
 */
export const GUARD_ICON_URLS: Record<number, string> = {
  1: '/guard-icon/governor.webp',  // 总督
  2: '/guard-icon/admiral.webp',   // 提督
  3: '/guard-icon/captain.webp',   // 舰长
};

export const getGuardIconUrl = (level?: number): string | undefined => {
  if (!level || level < 1 || level > 3) return undefined;
  return GUARD_ICON_URLS[level];
};