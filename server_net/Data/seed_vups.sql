-- OVO Family
INSERT INTO rooms (room_id, uid, name, is_active, auto_record, group_name, playlist_url, sort_order, followers, guard_num, video_count, last_live_time, updated_at) 
VALUES 
(22642754, '1104048496', '桃几OvO', 1, 1, 'OVO家族', 'https://www.ovo.fan', 1, 0, 0, 0, 0, NULL)
ON CONFLICT(uid) DO UPDATE SET name=excluded.name, group_name=excluded.group_name, playlist_url=excluded.playlist_url, sort_order=excluded.sort_order, auto_record=excluded.auto_record;

INSERT INTO rooms (room_id, uid, name, is_active, auto_record, group_name, playlist_url, sort_order, followers, guard_num, video_count, last_live_time, updated_at) 
VALUES 
(2303412, '4718716', '鱼鸽鸽', 1, 1, 'OVO家族', 'https://bot.starlwr.com/songlist?uid=4718716', 2, 0, 0, 0, 0, NULL)
ON CONFLICT(uid) DO UPDATE SET name=excluded.name, group_name=excluded.group_name, playlist_url=excluded.playlist_url, sort_order=excluded.sort_order, auto_record=excluded.auto_record;

INSERT INTO rooms (room_id, uid, name, is_active, auto_record, group_name, playlist_url, sort_order, followers, guard_num, video_count, last_live_time, updated_at) 
VALUES 
(27484357, '3493271057730096', '妮莉安Lily', 1, 1, 'OVO家族', 'https://www.nilianlily.cn', 3, 0, 0, 0, 0, NULL)
ON CONFLICT(uid) DO UPDATE SET name=excluded.name, group_name=excluded.group_name, playlist_url=excluded.playlist_url, sort_order=excluded.sort_order, auto_record=excluded.auto_record;

INSERT INTO rooms (room_id, uid, name, is_active, auto_record, group_name, playlist_url, sort_order, followers, guard_num, video_count, last_live_time, updated_at) 
VALUES 
(443197, '17967817', '大哥L-', 1, 0, 'OVO家族', 'https://dagel.live', 4, 0, 0, 0, 0, NULL)
ON CONFLICT(uid) DO UPDATE SET name=excluded.name, group_name=excluded.group_name, playlist_url=excluded.playlist_url, sort_order=excluded.sort_order, auto_record=excluded.auto_record;

INSERT INTO rooms (room_id, uid, name, is_active, auto_record, group_name, playlist_url, sort_order, followers, guard_num, video_count, last_live_time, updated_at) 
VALUES 
(573893, '15641218', '帅比笙歌超可爱OvO', 1, 0, 'OVO家族', 'https://tools.vupgo.com/LiveMusic?buid=15641218', 5, 0, 0, 0, 0, NULL)
ON CONFLICT(uid) DO UPDATE SET name=excluded.name, group_name=excluded.group_name, playlist_url=excluded.playlist_url, sort_order=excluded.sort_order, auto_record=excluded.auto_record;

INSERT INTO rooms (room_id, uid, name, is_active, auto_record, group_name, playlist_url, sort_order, followers, guard_num, video_count, last_live_time, updated_at) 
VALUES 
(22857429, '1376650682', '葡冷尔子gagako', 1, 0, 'OVO家族', 'http://gagako.minamini.cn', 6, 0, 0, 0, 0, NULL)
ON CONFLICT(uid) DO UPDATE SET name=excluded.name, group_name=excluded.group_name, playlist_url=excluded.playlist_url, sort_order=excluded.sort_order, auto_record=excluded.auto_record;

INSERT INTO rooms (room_id, uid, name, is_active, auto_record, group_name, playlist_url, sort_order, followers, guard_num, video_count, last_live_time, updated_at) 
VALUES 
(873642, '7591465', '里奈Rina', 1, 0, 'OVO家族', 'http://rinana.vsinger.ink', 7, 0, 0, 0, 0, NULL)
ON CONFLICT(uid) DO UPDATE SET name=excluded.name, group_name=excluded.group_name, playlist_url=excluded.playlist_url, sort_order=excluded.sort_order, auto_record=excluded.auto_record;

-- TYBK Sisters
INSERT INTO rooms (room_id, uid, name, is_active, auto_record, group_name, playlist_url, sort_order, followers, guard_num, video_count, last_live_time, updated_at) 
VALUES 
(21465419, '390647282', '浅野天琪_TANCHJIM', 1, 0, '桃圆布蔻', 'http://yybb.vsinger.ink', 8, 0, 0, 0, 0, NULL)
ON CONFLICT(uid) DO UPDATE SET name=excluded.name, group_name=excluded.group_name, playlist_url=excluded.playlist_url, sort_order=excluded.sort_order, auto_record=excluded.auto_record;

INSERT INTO rooms (room_id, uid, name, is_active, auto_record, group_name, playlist_url, sort_order, followers, guard_num, video_count, last_live_time, updated_at) 
VALUES 
(685026, '188679', 'Niya阿布', 1, 0, '桃圆布蔻', 'https://2some.ren/niyabu/songs', 9, 0, 0, 0, 0, NULL)
ON CONFLICT(uid) DO UPDATE SET name=excluded.name, group_name=excluded.group_name, playlist_url=excluded.playlist_url, sort_order=excluded.sort_order, auto_record=excluded.auto_record;

INSERT INTO rooms (room_id, uid, name, is_active, auto_record, group_name, playlist_url, sort_order, followers, guard_num, video_count, last_live_time, updated_at) 
VALUES 
(23587427, '128667389', '-蔻蔻CC-', 1, 0, '桃圆布蔻', 'http://kkcc.vsinger.ink', 10, 0, 0, 0, 0, NULL)
ON CONFLICT(uid) DO UPDATE SET name=excluded.name, group_name=excluded.group_name, playlist_url=excluded.playlist_url, sort_order=excluded.sort_order, auto_record=excluded.auto_record;

-- FRIENDS
INSERT INTO rooms (room_id, uid, name, is_active, auto_record, group_name, playlist_url, sort_order, followers, guard_num, video_count, last_live_time, updated_at) 
VALUES 
(22605289, '703018634', '莱妮娅_Rynia', 1, 0, '桃几的好朋友们', 'http://songlist.rynia.live', 11, 0, 0, 0, 0, NULL)
ON CONFLICT(uid) DO UPDATE SET name=excluded.name, group_name=excluded.group_name, playlist_url=excluded.playlist_url, sort_order=excluded.sort_order, auto_record=excluded.auto_record;

INSERT INTO rooms (room_id, uid, name, is_active, auto_record, group_name, playlist_url, sort_order, followers, guard_num, video_count, last_live_time, updated_at) 
VALUES 
(5424, '90873', '内德维德', 1, 0, '桃几的好朋友们', '', 12, 0, 0, 0, 0, NULL)
ON CONFLICT(uid) DO UPDATE SET name=excluded.name, group_name=excluded.group_name, playlist_url=excluded.playlist_url, sort_order=excluded.sort_order, auto_record=excluded.auto_record;

INSERT INTO rooms (room_id, uid, name, is_active, auto_record, group_name, playlist_url, sort_order, followers, guard_num, video_count, last_live_time, updated_at) 
VALUES 
(22924075, '1112031857', '薇Steria', 1, 0, '桃几的好朋友们', 'http://weisteria.vsinger.ink', 13, 0, 0, 0, 0, NULL)
ON CONFLICT(uid) DO UPDATE SET name=excluded.name, group_name=excluded.group_name, playlist_url=excluded.playlist_url, sort_order=excluded.sort_order, auto_record=excluded.auto_record;

INSERT INTO rooms (room_id, uid, name, is_active, auto_record, group_name, playlist_url, sort_order, followers, guard_num, video_count, last_live_time, updated_at) 
VALUES 
(858080, '121309', 'CODE-V', 1, 0, '桃几的好朋友们', 'https://codev.starlwr.com', 14, 0, 0, 0, 0, NULL)
ON CONFLICT(uid) DO UPDATE SET name=excluded.name, group_name=excluded.group_name, playlist_url=excluded.playlist_url, sort_order=excluded.sort_order, auto_record=excluded.auto_record;

INSERT INTO rooms (room_id, uid, name, is_active, auto_record, group_name, playlist_url, sort_order, followers, guard_num, video_count, last_live_time, updated_at) 
VALUES 
(3473884, '796556', '-菫時-', 1, 0, '桃几的好朋友们', 'https://sumireji.com/', 15, 0, 0, 0, 0, NULL)
ON CONFLICT(uid) DO UPDATE SET name=excluded.name, group_name=excluded.group_name, playlist_url=excluded.playlist_url, sort_order=excluded.sort_order, auto_record=excluded.auto_record;
