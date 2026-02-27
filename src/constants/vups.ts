import VUP_COLORS from './vup-colors.json';

export const GROUPS = {
  OVO_FAMILY: 'OVO家族',
  TYBK_SISTERS: '桃圆布蔻',
  FEIENDS: '桃几的好朋友们'
};

// 图片直接从前端静态资源（COS/CDN）加载，不再走后端 API
// 这样可以避免 Mixed Content 问题，且利用 CDN 加速
const IMG_BASE_URL = ''; // 空字符串表示使用当前域名（即 CDN 域名）

// VUP 条目类型定义
export interface VupItem {
  id: string;
  uid: string;
  name: string;
  homepageUrl: string;
  livestreamUrl: string;
  playlistUrl: string;
  groups: string[];
  imageUrl: string;
  coverUrl?: string;
  avatarUrl: string;
  // 预提取的主题色，用于移动端背景流光效果
  themeColors?: string[];
  // 弹幕监控标识：与 VupConstants.cs 中的 Vups 列表对齐
  hasMonitor: boolean;
  // 扩展预留字段（未来接入 B站 API 时填充）
  followers?: number;        // 粉丝数 (follower)
  guardNum?: number;         // 舰长数 (guardNum)
  archiveView?: number;      // 总播放量 (archiveView)
  videoCount?: number;       // 视频数 (video)
  online?: number;           // 在线人气 (online)
  lastLiveTime?: number;     // 最近直播时间（Unix 时间戳 ms） (lastLive.time)
  isLiving?: boolean;        // 是否正在直播
}

// 拥有弹幕监控的用户 UID 集合（与 VupConstants.cs 同步）
export const MONITOR_UIDS = new Set([
  '1104048496',       // 桃几OvO
  '4718716',          // 鱼鸽鸽
  '3493271057730096', // 妮莉安Lily
  '17967817',         // 大哥L-
  '15641218',         // 帅比笙歌超可爱OvO
  '1376650682',       // 葡冷尔子gagako
  '7591465',          // 里奈Rina
  '390647282',        // 浅野天琪_TANCHJIM
  '188679',           // Niya阿布
  '128667389',        // -蔻蔻CC-
  '703018634',        // 莱妮娅_Rynia
  '90873',            // 内德维德
  '1112031857',       // 薇Steria
  '121309',           // CODE-V
  '796556',           // -菫時-
]);

export const VUP_LIST: VupItem[] = [
    {
        id: '1',
        uid: '1104048496',
        name: '桃几OvO',
        homepageUrl: 'https://space.bilibili.com/1104048496',
        livestreamUrl: 'https://live.bilibili.com/22642754',
        playlistUrl: 'https://www.ovo.fan',
        groups: [GROUPS.OVO_FAMILY, GROUPS.TYBK_SISTERS],
        imageUrl: `${IMG_BASE_URL}/vup-avatar/1104048496.webp`,
        coverUrl: `${IMG_BASE_URL}/vup-cover/1104048496.png`,
        avatarUrl: `${IMG_BASE_URL}/vup-avatar/1104048496.webp`,
        themeColors: VUP_COLORS['1104048496'],
        hasMonitor: true
    },
    {
        id: '2',
        uid: '4718716',
        name: '鱼鸽鸽',
        homepageUrl: 'https://space.bilibili.com/4718716',
        livestreamUrl: 'https://live.bilibili.com/673',
        playlistUrl: 'https://bot.starlwr.com/songlist?uid=4718716',
        groups: [GROUPS.OVO_FAMILY],
        imageUrl: `${IMG_BASE_URL}/vup-avatar/4718716.webp`,
        coverUrl: `${IMG_BASE_URL}/vup-cover/4718716.png`,
        avatarUrl: `${IMG_BASE_URL}/vup-avatar/4718716.webp`,
        themeColors: VUP_COLORS['4718716'],
        hasMonitor: true
    },
    {
        id: '3',
        uid: '3493271057730096',
        name: '妮莉安Lily',
        homepageUrl: 'https://space.bilibili.com/3493271057730096',
        livestreamUrl: 'https://live.bilibili.com/27484357',
        playlistUrl: 'https://www.nilianlily.cn',
        groups: [GROUPS.OVO_FAMILY],
        imageUrl: `${IMG_BASE_URL}/vup-avatar/3493271057730096.webp`,
        coverUrl: `${IMG_BASE_URL}/vup-cover/3493271057730096.png`,
        avatarUrl: `${IMG_BASE_URL}/vup-avatar/3493271057730096.webp`,
        themeColors: VUP_COLORS['3493271057730096'],
        hasMonitor: true
    },
    {
        id: '4',
        uid: '17967817',
        name: '大哥L-',
        homepageUrl: 'https://space.bilibili.com/17967817',
        livestreamUrl: 'https://live.bilibili.com/443197',
        playlistUrl: 'https://dagel.live',
        groups: [GROUPS.OVO_FAMILY],
        imageUrl: `${IMG_BASE_URL}/vup-avatar/17967817.webp`,
        coverUrl: `${IMG_BASE_URL}/vup-cover/17967817.png`,
        avatarUrl: `${IMG_BASE_URL}/vup-avatar/17967817.webp`,
        themeColors: VUP_COLORS['17967817'],
        hasMonitor: false
    },
    {
        id: '5',
        uid: '15641218',
        name: '帅比笙歌超可爱OvO',
        homepageUrl: 'https://space.bilibili.com/15641218',
        livestreamUrl: 'https://live.bilibili.com/545',
        playlistUrl: 'https://tools.vupgo.com/LiveMusic?buid=15641218',
        groups: [GROUPS.OVO_FAMILY],
        imageUrl: `${IMG_BASE_URL}/vup-avatar/15641218.webp`,
        coverUrl: `${IMG_BASE_URL}/vup-cover/15641218.png`,
        avatarUrl: `${IMG_BASE_URL}/vup-avatar/15641218.webp`,
        themeColors: VUP_COLORS['15641218'],
        hasMonitor: false
    },
    {
        id: '6',
        uid: '1376650682',
        name: '葡冷尔子gagako',
        homepageUrl: 'https://space.bilibili.com/1376650682',
        livestreamUrl: 'https://live.bilibili.com/22857429',
        playlistUrl: 'http://gagako.minamini.cn',
        groups: [GROUPS.OVO_FAMILY],
        imageUrl: `${IMG_BASE_URL}/vup-avatar/1376650682.webp`,
        coverUrl: `${IMG_BASE_URL}/vup-cover/1376650682.png`,
        avatarUrl: `${IMG_BASE_URL}/vup-avatar/1376650682.webp`,
        themeColors: VUP_COLORS['1376650682'],
        hasMonitor: false
    },
    {
        id: '7',
        uid: '7591465',
        name: '里奈Rina',
        homepageUrl: 'https://space.bilibili.com/7591465',
        livestreamUrl: 'https://live.bilibili.com/873642',
        playlistUrl: 'http://rinana.vsinger.ink',
        groups: [GROUPS.OVO_FAMILY],
        imageUrl: `${IMG_BASE_URL}/vup-avatar/7591465.webp`,
        coverUrl: `${IMG_BASE_URL}/vup-cover/7591465.png`,
        avatarUrl: `${IMG_BASE_URL}/vup-avatar/7591465.webp`,
        themeColors: VUP_COLORS['7591465'],
        hasMonitor: false
    },
    {
        id: '8',
        uid: '390647282',
        name: '浅野天琪_TANCHJIM',
        homepageUrl: 'https://space.bilibili.com/390647282',
        livestreamUrl: 'https://live.bilibili.com/21465419',
        playlistUrl: 'http://yybb.vsinger.ink',
        groups: [GROUPS.TYBK_SISTERS],
        imageUrl: `${IMG_BASE_URL}/vup-avatar/390647282.webp`,
        coverUrl: `${IMG_BASE_URL}/vup-cover/390647282.png`,
        avatarUrl: `${IMG_BASE_URL}/vup-avatar/390647282.webp`,
        themeColors: VUP_COLORS['390647282'],
        hasMonitor: false
    },
    {
        id: '9',
        uid: '188679',
        name: 'Niya阿布',
        homepageUrl: 'https://space.bilibili.com/188679',
        livestreamUrl: 'https://live.bilibili.com/685026',
        playlistUrl: 'https://2some.ren/niyabu/songs',
        groups: [GROUPS.TYBK_SISTERS],
        imageUrl: `${IMG_BASE_URL}/vup-avatar/188679.webp`,
        coverUrl: `${IMG_BASE_URL}/vup-cover/188679.png`,
        avatarUrl: `${IMG_BASE_URL}/vup-avatar/188679.webp`,
        themeColors: VUP_COLORS['188679'],
        hasMonitor: false
    },
    {
        id: '10',
        uid: '128667389',
        name: '-蔻蔻CC-',
        homepageUrl: 'https://space.bilibili.com/128667389',
        livestreamUrl: 'https://live.bilibili.com/23587427',
        playlistUrl: 'http://kkcc.vsinger.ink',
        groups: [GROUPS.TYBK_SISTERS],
        imageUrl: `${IMG_BASE_URL}/vup-avatar/128667389.webp`,
        coverUrl: `${IMG_BASE_URL}/vup-cover/128667389.png`,
        avatarUrl: `${IMG_BASE_URL}/vup-avatar/128667389.webp`,
        themeColors: VUP_COLORS['128667389'],
        hasMonitor: false
    },
    {
        id: '11',
        uid: '703018634',
        name: '莱妮娅_Rynia',
        homepageUrl: 'https://space.bilibili.com/703018634',
        livestreamUrl: 'https://live.bilibili.com/54363',
        playlistUrl: 'http://songlist.rynia.live',
        groups: [GROUPS.FEIENDS],
        imageUrl: `${IMG_BASE_URL}/vup-avatar/703018634.webp`,
        coverUrl: `${IMG_BASE_URL}/vup-cover/703018634.png`,
        avatarUrl: `${IMG_BASE_URL}/vup-avatar/703018634.webp`,
        themeColors: VUP_COLORS['703018634'],
        hasMonitor: false
    },
    {
        id: '12',
        uid: '90873',
        name: '内德维德',
        homepageUrl: 'https://space.bilibili.com/90873',
        livestreamUrl: 'https://live.bilibili.com/5424',
        playlistUrl: '',
        groups: [GROUPS.FEIENDS],
        imageUrl: `${IMG_BASE_URL}/vup-avatar/90873.webp`,
        coverUrl: `${IMG_BASE_URL}/vup-cover/90873.png`,
        avatarUrl: `${IMG_BASE_URL}/vup-avatar/90873.webp`,
        themeColors: VUP_COLORS['90873'],
        hasMonitor: false
    },
    {
        id: '13',
        uid: '1112031857',
        name: '薇Steria',
        homepageUrl: 'https://space.bilibili.com/1112031857',
        livestreamUrl: 'https://live.bilibili.com/22924075',
        playlistUrl: 'http://weisteria.vsinger.ink',
        groups: [GROUPS.FEIENDS],
        imageUrl: `${IMG_BASE_URL}/vup-avatar/1112031857.webp`,
        coverUrl: `${IMG_BASE_URL}/vup-cover/1112031857.png`,
        avatarUrl: `${IMG_BASE_URL}/vup-avatar/1112031857.webp`,
        themeColors: VUP_COLORS['1112031857'],
        hasMonitor: false
    },
    {
        id: '14',
        uid: '121309',
        name: 'CODE-V',
        homepageUrl: 'https://space.bilibili.com/121309',
        livestreamUrl: 'https://live.bilibili.com/858080',
        playlistUrl: 'https://codev.starlwr.com',
        groups: [GROUPS.FEIENDS],
        imageUrl: `${IMG_BASE_URL}/vup-avatar/121309.webp`,
        coverUrl: `${IMG_BASE_URL}/vup-cover/121309.png`,
        avatarUrl: `${IMG_BASE_URL}/vup-avatar/121309.webp`,
        themeColors: VUP_COLORS['121309'],
        hasMonitor: false
    },
    {
        id: '15',
        uid: '796556',
        name: '-菫時-',
        homepageUrl: 'https://space.bilibili.com/796556',
        livestreamUrl: 'https://live.bilibili.com/3473884',
        playlistUrl: 'https://sumireji.com/',
        groups: [GROUPS.FEIENDS],
        imageUrl: `${IMG_BASE_URL}/vup-avatar/796556.webp`,
        coverUrl: `${IMG_BASE_URL}/vup-cover/796556.png`,
        avatarUrl: `${IMG_BASE_URL}/vup-avatar/796556.webp`,
        themeColors: VUP_COLORS['796556'],
        hasMonitor: false
    },
];
