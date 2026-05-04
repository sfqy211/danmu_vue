/**
 * Wealth level icon paths (local assets in public/wealth-level/).
 * Levels 1–79 use .png, level 80 uses .webp.
 */
// Level 80 uses .webp because B站 CDN serves it in WebP format;
// all other levels (1–79) are .png from the local public/wealth-level/ directory.
const WEALTH_LEVEL_EXT: Record<number, string> = {
  80: '.webp',
};

export const getWealthLevelUrl = (level?: number): string | undefined => {
  if (!level || level < 1 || level > 80) return undefined;
  const ext = WEALTH_LEVEL_EXT[level] ?? '.png';
  return `/wealth-level/${level}${ext}`;
};