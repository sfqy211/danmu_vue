import axios from 'axios';

const normalizeApiBase = (baseUrl: string) => {
  const trimmed = baseUrl.replace(/\/+$/, '');
  if (trimmed.endsWith('/api')) {
    return trimmed;
  }
  return `${trimmed}/api`;
};

const api = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL
    ? normalizeApiBase(import.meta.env.VITE_API_BASE_URL)
    : '/api'
});

export interface FileInfo {
  name: string;
  size: number;
  date: string;
  streamer: string;
}

export interface Danmaku {
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
  const res = await api.get<StreamerInfo[]>('/streamers');
  return res.data;
};

export const getSessions = async (params: { userName?: string; startTime?: number; endTime?: number }) => {
  const res = await api.get<SessionInfo[]>('/sessions', { params });
  return res.data;
};

export const getSessionDanmaku = async (sessionId: number, page: number = 1, pageSize: number = 100) => {
  const res = await api.get<{
    messages: {
      time: number;
      timestamp: number;
      sender: string;
      uid: string;
      text: string;
      isSC: boolean;
      price?: number;
    }[];
    total: number;
    page: number;
    pageSize: number;
    totalPages: number;
  }>(`/danmaku`, { params: { id: sessionId, page, pageSize } });

  const formatTime = (seconds: number) => {
    const h = Math.floor(seconds / 3600);
    const m = Math.floor((seconds % 3600) / 60);
    const s = seconds % 60;
    return h > 0 
      ? `${h}:${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`
      : `${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`;
  };

  const danmaku: Danmaku[] = res.data.messages.map(msg => ({
    user: msg.sender,
    uid: msg.uid,
    content: msg.text,
    timestamp: msg.timestamp,
    timeStr: formatTime(msg.time),
    isSC: msg.isSC,
    price: msg.price
  }));

  return {
    danmaku,
    total: res.data.total,
    page: res.data.page,
    pageSize: res.data.pageSize,
    totalPages: res.data.totalPages
  };
};

export interface SessionSummary extends SessionInfo {
  summary_json: string;
  summary?: any;
}

export const getSessionSummary = async (sessionId: number): Promise<SessionSummary> => {
  const res = await api.get<SessionInfo & { summary_json: string }>(`/summary`, { params: { id: sessionId } });
  const result: SessionSummary = { ...res.data };
  
  if (res.data.summary_json) {
    try {
      result.summary = JSON.parse(res.data.summary_json);
    } catch (e) {
      console.error('Failed to parse summary_json:', e);
    }
  }
  return result;
};

export interface AnalysisResult {
  analysis: string;
}

export const analyzeSession = async (sessionId: number): Promise<AnalysisResult> => {
  const res = await api.post<AnalysisResult>('/analyze', { id: sessionId });
  return res.data;
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
    return {
      list: res.data,
      total: res.data.length,
      page: 1,
      pageSize: res.data.length
    };
  }
  
  return res.data;
};
