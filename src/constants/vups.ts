export const GROUPS = {
  OVO_FAMILY: 'OVO家族',
  TYBK_SISTERS: '桃圆布蔻',
  FEIENDS: '桃几的好朋友们'
};

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || '/api';

export const VUP_LIST = [
    {
        id: '1',
        uid: '1104048496',
        name: '桃几OvO',
        homepageUrl: 'https://space.bilibili.com/1104048496',
        livestreamUrl: 'https://live.bilibili.com/22642754',
        playlistUrl: 'https://www.ovo.fan',
        groups: [GROUPS.OVO_FAMILY, GROUPS.TYBK_SISTERS],
        imageUrl: `${API_BASE_URL}/avatars/1104048496.png`,
        coverUrl: `${API_BASE_URL}/covers/1104048496.png`
    },
    {
        id: '2',
        uid: '4718716',
        name: '鱼鸽鸽',
        homepageUrl: 'https://space.bilibili.com/4718716',
        livestreamUrl: 'https://live.bilibili.com/673',
        playlistUrl: 'https://bot.starlwr.com/songlist?uid=4718716',
        groups: [GROUPS.OVO_FAMILY],
        imageUrl: `${API_BASE_URL}/avatars/4718716.png`,
        coverUrl: `${API_BASE_URL}/covers/4718716.png`
    },
    {
        id: '3',
        uid: '3493271057730096',
        name: '妮莉安Lily',
        homepageUrl: 'https://space.bilibili.com/3493271057730096',
        livestreamUrl: 'https://live.bilibili.com/27484357',
        playlistUrl: 'https://www.nilianlily.cn',
        groups: [GROUPS.OVO_FAMILY],
        imageUrl: `${API_BASE_URL}/avatars/3493271057730096.png`,
        coverUrl: `${API_BASE_URL}/covers/3493271057730096.png`
    },
    {
        id: '4',
        uid: '17967817',
        name: '大哥L-',
        homepageUrl: 'https://space.bilibili.com/17967817',
        livestreamUrl: 'https://live.bilibili.com/443197',
        playlistUrl: 'https://dagel.live',
        groups: [GROUPS.OVO_FAMILY],
        imageUrl: `${API_BASE_URL}/avatars/17967817.png`,
        coverUrl: `${API_BASE_URL}/covers/17967817.png`
    },
    {
        id: '5',
        uid: '15641218',
        name: '帅比笙歌超可爱OvO',
        homepageUrl: 'https://space.bilibili.com/15641218',
        livestreamUrl: 'https://live.bilibili.com/545',
        playlistUrl: 'https://tools.vupgo.com/LiveMusic?buid=15641218',
        groups: [GROUPS.OVO_FAMILY],
        imageUrl: `${API_BASE_URL}/avatars/15641218.png`,
        coverUrl: `${API_BASE_URL}/covers/15641218.png`
    },
    {
        id: '6',
        uid: '1376650682',
        name: '葡冷尔子gagako',
        homepageUrl: 'https://space.bilibili.com/1376650682',
        livestreamUrl: 'https://live.bilibili.com/22857429',
        playlistUrl: 'http://gagako.minamini.cn',
        groups: [GROUPS.OVO_FAMILY],
        imageUrl: `${API_BASE_URL}/avatars/1376650682.png`,
        coverUrl: `${API_BASE_URL}/covers/1376650682.png`
    },
    {
        id: '7',
        uid: '7591465',
        name: '里奈Rina',
        homepageUrl: 'https://space.bilibili.com/7591465',
        livestreamUrl: 'https://live.bilibili.com/873642',
        playlistUrl: 'http://rinana.vsinger.ink',
        groups: [GROUPS.OVO_FAMILY],
        imageUrl: `${API_BASE_URL}/avatars/7591465.png`,
        coverUrl: `${API_BASE_URL}/covers/7591465.png`
    },
    {
        id: '8',
        uid: '390647282',
        name: '浅野天琪_TANCHJIM',
        homepageUrl: 'https://space.bilibili.com/390647282',
        livestreamUrl: 'https://live.bilibili.com/21465419',
        playlistUrl: 'http://yybb.vsinger.ink',
        groups: [GROUPS.TYBK_SISTERS],
        imageUrl: `${API_BASE_URL}/avatars/390647282.png`
    },
    {
        id: '9',
        uid: '188679',
        name: 'Niya阿布',
        homepageUrl: 'https://space.bilibili.com/188679',
        livestreamUrl: 'https://live.bilibili.com/685026',
        playlistUrl: 'https://2some.ren/niyabu/songs',
        groups: [GROUPS.TYBK_SISTERS],
        imageUrl: `${API_BASE_URL}/avatars/188679.png`
    },
    {
        id: '10',
        uid: '128667389',
        name: '-蔻蔻CC-',
        homepageUrl: 'https://space.bilibili.com/128667389',
        livestreamUrl: 'https://live.bilibili.com/23587427',
        playlistUrl: 'http://kkcc.vsinger.ink',
        groups: [GROUPS.TYBK_SISTERS],
        imageUrl: `${API_BASE_URL}/avatars/128667389.png`
    },
    {
        id: '11',
        uid: '703018634',
        name: '莱妮娅_Rynia',
        homepageUrl: 'https://space.bilibili.com/703018634',
        livestreamUrl: 'https://live.bilibili.com/54363',
        playlistUrl: 'http://songlist.rynia.live',
        groups: [GROUPS.FEIENDS],
        imageUrl: `${API_BASE_URL}/avatars/703018634.png`
    },
    {
        id: '12',
        uid: '90873',
        name: '内德维德',
        homepageUrl: 'https://space.bilibili.com/90873',
        livestreamUrl: 'https://live.bilibili.com/5424',
        playlistUrl: '',
        groups: [GROUPS.FEIENDS],
        imageUrl: `${API_BASE_URL}/avatars/90873.png`
    },
    {
        id: '13',
        uid: '1112031857',
        name: '薇Steria',
        homepageUrl: 'https://space.bilibili.com/1112031857',
        livestreamUrl: 'https://live.bilibili.com/22924075',
        playlistUrl: 'http://weisteria.vsinger.ink',
        groups: [GROUPS.FEIENDS],
        imageUrl: `${API_BASE_URL}/avatars/1112031857.png`
    },
    {
        id: '14',
        uid: '121309',
        name: 'CODE-V',
        homepageUrl: 'https://space.bilibili.com/121309',
        livestreamUrl: 'https://live.bilibili.com/858080',
        playlistUrl: 'https://codev.starlwr.com',
        groups: [GROUPS.FEIENDS],
        imageUrl: `${API_BASE_URL}/avatars/121309.png`
    },
    {
        id: '15',
        uid: '796556',
        name: '-菫時-',
        homepageUrl: 'https://space.bilibili.com/796556',
        livestreamUrl: 'https://live.bilibili.com/3473884',
        playlistUrl: 'https://sumireji.com/',
        groups: [GROUPS.FEIENDS],
        imageUrl: `${API_BASE_URL}/avatars/796556.png`
    },
];
