import axios from 'axios';

const normalizeApiBase = (baseUrl: string) => {
  const trimmed = baseUrl.replace(/\/+$/, '');
  if (trimmed.endsWith('/api')) {
    return trimmed;
  }
  return `${trimmed}/api`;
};

export const api = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL
    ? normalizeApiBase(import.meta.env.VITE_API_BASE_URL)
    : '/api'
});

const resolveAdminApiBase = () => {
  if (import.meta.env.VITE_ADMIN_API_BASE_URL) {
    return normalizeApiBase(import.meta.env.VITE_ADMIN_API_BASE_URL);
  }

  // Fallback to relative path for all cases, which is safest for reverse proxies
  return '/api';
};

export const adminApi = axios.create({
  baseURL: resolveAdminApiBase()
});

const toNumber = (value: unknown) => {
  if (typeof value === 'number' && Number.isFinite(value)) return value;
  if (typeof value === 'string' && value.trim() !== '' && Number.isFinite(Number(value))) return Number(value);
  return undefined;
};

const normalizeSession = (session: any): SessionInfo => {
  const roomId = toNumber(session.room_id ?? session.roomId ?? session.RoomId);
  const rawStartTime = toNumber(session.start_time ?? session.startTime ?? session.StartTime);
  const rawEndTime = toNumber(session.end_time ?? session.endTime ?? session.EndTime);
  const startTime = rawStartTime && rawStartTime < 1_000_000_000_000 ? rawStartTime * 1000 : rawStartTime;
  const endTime = rawEndTime && rawEndTime < 1_000_000_000_000 ? rawEndTime * 1000 : rawEndTime;
  return {
    id: toNumber(session.id ?? session.Id) ?? 0,
    room_id: roomId ?? 0,
    start_time: startTime ?? 0,
    end_time: endTime,
    title: session.title ?? session.Title,
    user_name: session.user_name ?? session.userName ?? session.UserName ?? ''
  };
};

const normalizeStreamer = (streamer: any): StreamerInfo => {
  return {
    user_name: streamer.user_name ?? streamer.userName ?? streamer.UserName ?? '',
    room_id: streamer.room_id ?? streamer.roomId ?? streamer.RoomId
  };
};

const normalizeSongRequest = (request: any): SongRequest => {
  return {
    id: toNumber(request.id ?? request.Id) ?? 0,
    user_name: request.user_name ?? request.userName ?? request.UserName ?? '',
    uid: request.uid ?? request.Uid ?? '',
    song_name: request.song_name ?? request.songName ?? request.SongName ?? '',
    singer: request.singer ?? request.Singer ?? '',
    created_at: toNumber(request.created_at ?? request.createdAt ?? request.CreatedAt) ?? 0,
    session_title: request.session_title ?? request.sessionTitle ?? request.SessionTitle,
    session_start_time: toNumber(request.session_start_time ?? request.sessionStartTime ?? request.SessionStartTime)
  };
};

export interface FileInfo {
  name: string;
  size: number;
  date: string;
  streamer: string;
}

export interface Danmaku {
  id: string; // 唯一 ID
  user: string;
  uid: string;
  content: string;
  timestamp: number;
  timeStr: string;
  isSC: boolean;
  price?: number;
}

export interface SessionInfo {
  id: number;
  room_id: number;
  start_time: number;
  end_time?: number;
  title?: string;
  user_name: string;
}

export const getFiles = async () => {
  const res = await api.get<FileInfo[]>('/files');
  return res.data;
};

export interface StreamerInfo {
  user_name: string;
  room_id?: string;
}

export const getStreamers = async () => {
  const res = await api.get<any[]>('/streamers');
  return res.data.map(normalizeStreamer);
};

export const getSessions = async (
  params: { userName?: string; startTime?: number; endTime?: number }
) => {
  const res = await api.get<any[]>('/sessions', { params });
  return res.data.map(normalizeSession);
};

export const getSessionsTotal = async (
  params: { userName?: string }
) => {
  const res = await api.get<{ total: number }>('/sessions/total', { params });
  return res.data;
};

export const getSessionDanmaku = async (sessionId: number, page: number = 1, pageSize: number = 100) => {
  const res = await api.get<any>(`/danmaku`, { params: { id: sessionId, page, pageSize } });

  const formatTime = (seconds: number) => {
    if (!Number.isFinite(seconds)) return '00:00';
    const totalSeconds = Math.max(0, Math.floor(seconds));
    const h = Math.floor(totalSeconds / 3600);
    const m = Math.floor((totalSeconds % 3600) / 60);
    const s = totalSeconds % 60;
    return h > 0 
      ? `${h}:${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`
      : `${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`;
  };

  const sourceMessages = Array.isArray(res.data.messages)
    ? res.data.messages
    : Array.isArray(res.data.list)
      ? res.data.list
      : [];

  const danmaku: Danmaku[] = sourceMessages.map((msg: any, index: number) => ({
    // Generate unique ID on frontend: timestamp-uid-page-index
    id: `${msg.timestamp}-${msg.uid}-${page}-${index}`,
    user: msg.sender,
    uid: msg.uid,
    content: msg.text,
    timestamp: msg.timestamp,
    timeStr: formatTime(toNumber(msg.time) ?? 0),
    isSC: msg.isSC,
    price: msg.price
  }));

  const total = typeof res.data.total === 'number' ? res.data.total : danmaku.length;
  const resolvedPage = typeof res.data.page === 'number' ? res.data.page : page;
  const resolvedPageSize = typeof res.data.pageSize === 'number' ? res.data.pageSize : pageSize;
  const totalPages = typeof res.data.totalPages === 'number'
    ? res.data.totalPages
    : resolvedPageSize > 0
      ? Math.max(1, Math.ceil(total / resolvedPageSize))
      : 1;

  return {
    danmaku,
    total,
    page: resolvedPage,
    pageSize: resolvedPageSize,
    totalPages
  };
};

export interface SessionSummary extends SessionInfo {
  summary_json: string;
  gift_summary_json?: string;
  summary?: any;
}

const normalizeSummary = (summary: any) => {
  if (!summary || typeof summary !== 'object') return summary;
  const totalCount = summary.totalCount ?? summary.TotalCount;
  const rawUserStats = summary.userStats ?? summary.UserStats;
  const timeline = summary.timeline ?? summary.Timeline;
  const topKeywords = summary.topKeywords ?? summary.TopKeywords;
  const userStats = rawUserStats
    ? Object.entries(rawUserStats).reduce<Record<string, any>>((acc, [name, stats]) => {
        const data = stats as any;
        acc[name] = {
          count: data.count ?? data.Count ?? 0,
          scCount: data.scCount ?? data.ScCount ?? 0,
          uid: data.uid ?? data.Uid ?? ''
        };
        return acc;
      }, {})
    : undefined;
  return {
    ...summary,
    ...(totalCount !== undefined ? { totalCount } : {}),
    ...(userStats ? { userStats } : {}),
    ...(timeline ? { timeline } : {}),
    ...(topKeywords ? { topKeywords } : {})
  };
};

export const getSessionSummary = async (sessionId: number): Promise<SessionSummary> => {
  const res = await api.get<any>(`/summary`, { params: { id: sessionId } });
  const summaryJson = res.data.summary_json ?? res.data.summaryJson ?? res.data.SummaryJson ?? '';
  const giftSummaryJson = res.data.gift_summary_json ?? res.data.giftSummaryJson ?? res.data.GiftSummaryJson ?? '';
  const result: SessionSummary = {
    ...res.data,
    ...normalizeSession(res.data),
    summary_json: summaryJson,
    gift_summary_json: giftSummaryJson
  };
  
  if (summaryJson) {
    try {
      result.summary = normalizeSummary(JSON.parse(summaryJson));
    } catch (e) {
      console.error('Failed to parse summary_json:', e);
    }
  }
  return result;
};



export interface SongRequest {
  id: number;
  user_name: string;
  uid: string;
  song_name: string;
  singer: string;
  created_at: number;
  session_title?: string;
  session_start_time?: number;
}

export interface SongRequestsResponse {
  list: SongRequest[];
  total: number;
  page: number;
  pageSize: number;
}

export const getSongRequests = async (params: { id?: number; roomId?: string; page?: number; pageSize?: number; search?: string }): Promise<SongRequestsResponse> => {
  const res = await api.get<SongRequestsResponse | SongRequest[]>('/song-requests', { params });
  
  // 兼容处理：如果后端返回的是数组（旧接口），则手动封装为分页结构
  if (Array.isArray(res.data)) {
    const list = res.data.map(normalizeSongRequest);
    return {
      list,
      total: list.length,
      page: 1,
      pageSize: list.length
    };
  }
  
  return {
    ...res.data,
    list: res.data.list.map(normalizeSongRequest)
  };
};

export interface Pm2Process {
  name: string;
  status: string;
  id: number;
}

export interface Pm2StatusResponse {
  status: 'success' | 'error';
  processes?: Pm2Process[];
  error?: string;
}

export const getPm2Status = async (): Promise<Pm2StatusResponse> => {
  const res = await api.get<Pm2StatusResponse>('/pm2-status');
  return res.data;
};
