/**
 * Emoticon rendering utilities for Bilibili live danmaku.
 * Converts emoticon trigger text (e.g. "[热]") to inline images.
 */

import type { EmoticonInfo } from '../api/danmaku';

/**
 * Render text with emoticons replaced by inline images.
 * @param text The raw danmaku text containing emoticon triggers like "[热]"
 * @param emots Map of trigger text to emoticon info (from API)
 * @returns HTML string with emoticon triggers replaced by <img> tags
 */
export function renderTextWithEmoticons(
  text: string,
  emots?: Record<string, EmoticonInfo>
): string {
  if (!text || !emots || Object.keys(emots).length === 0) {
    return escapeHtml(text || '');
  }

  // Sort triggers by length (longest first) to avoid partial matches
  const triggers = Object.keys(emots).sort((a, b) => b.length - a.length);

  let result = '';
  let i = 0;

  while (i < text.length) {
    let matched = false;

    for (const trigger of triggers) {
      if (text.startsWith(trigger, i)) {
        const emot = emots[trigger];
        if (emot?.url) {
          const src = normalizeEmotUrl(emot.url);
          const size = emot.width && emot.height
            ? `width="${emot.width}" height="${emot.height}"`
            : 'width="20" height="20"';
          result += `<img class="emoticon-img" src="${escapeAttr(src)}" alt="${escapeAttr(trigger)}" ${size} loading="lazy" referrerpolicy="no-referrer" />`;
        } else {
          result += escapeHtml(trigger);
        }
        i += trigger.length;
        matched = true;
        break;
      }
    }

    if (!matched) {
      result += escapeHtml(text[i]);
      i++;
    }
  }

  return result;
}

function escapeHtml(str: string): string {
  return str
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#039;');
}

function escapeAttr(str: string): string {
  return str
    .replace(/&/g, '&amp;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#039;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;');
}

function normalizeEmotUrl(url: string): string {
  if (url.startsWith('//')) return `https:${url}`;
  if (url.startsWith('http://')) return url.replace(/^http:\/\//i, 'https://');
  return url;
}
