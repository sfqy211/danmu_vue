const IMG_BASE_URL = import.meta.env.VITE_COS_BASE_URL || '';

export const getVupAssetKey = (uid?: string | null, roomId?: string | number | null) => {
  const resolvedUid = typeof uid === 'string' ? uid.trim() : '';
  if (resolvedUid) {
    return resolvedUid;
  }

  if (typeof roomId === 'number' && Number.isFinite(roomId)) {
    return String(roomId);
  }

  if (typeof roomId === 'string' && roomId.trim()) {
    return roomId.trim();
  }

  return '';
};

export const buildHomepageUrl = (uid?: string | null) => {
  return uid ? `https://space.bilibili.com/${uid}` : 'https://www.bilibili.com';
};

export const buildLivestreamUrl = (roomId?: string | number | null) => {
  const key = getVupAssetKey(undefined, roomId);
  return key ? `https://live.bilibili.com/${key}` : 'https://live.bilibili.com';
};

export const buildAvatarUrl = (uid?: string | null, roomId?: string | number | null) => {
  const key = getVupAssetKey(uid, roomId);
  return key ? `${IMG_BASE_URL}/vup-avatar/${key}.webp` : '/vite.svg';
};

export const buildCoverUrl = (uid?: string | null, roomId?: string | number | null) => {
  const key = getVupAssetKey(uid, roomId);
  return key ? `${IMG_BASE_URL}/vup-cover/${key}.png` : '/vite.svg';
};
