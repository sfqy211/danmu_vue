import WebSocket from 'ws';
import axios from 'axios';
import zlib from 'node:zlib';
import fs from 'node:fs';
import path from 'node:path';
import minimist from 'minimist';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// Constants
const OP_HEARTBEAT = 2;
const OP_HEARTBEAT_REPLY = 3;
const OP_MESSAGE = 5;
const OP_USER_AUTHENTICATION = 7;
const OP_CONNECT_SUCCESS = 8;

const HEADER_SIZE = 16;

// Args
const args = minimist(process.argv.slice(2));
const ROOM_ID = args.room;
const COOKIE = args.cookie || process.env.BILI_COOKIE || '';
const COOKIE_UID = (() => {
  if (!COOKIE) return 0;
  const match = COOKIE.match(/DedeUserID=([^;]+)/);
  if (!match) return 0;
  const uid = parseInt(match[1], 10);
  return Number.isFinite(uid) ? uid : 0;
})();

if (!ROOM_ID) {
  console.error('Usage: tsx recorder.ts --room <room_id> [--cookie <cookie>]');
  process.exit(1);
}

interface RoomInfo {
  room_id: number;
  title: string;
  uid: number;
  uname: string; // user_name
  live_status: number;
}

// State
let ws: WebSocket | null = null;
let heartbeatInterval: NodeJS.Timeout | null = null;
let checkLiveInterval: NodeJS.Timeout | null = null;
let authTimeout: NodeJS.Timeout | null = null;
let currentFile: string | null = null;
let currentTempFile: string | null = null;
let roomInfo: RoomInfo | null = null;
let isRecording = false;
let hostIndex = 0;
let lastConnectAt = 0;

// Logger
function log(msg: string) {
  console.log(`[${new Date().toLocaleTimeString()}][Room ${ROOM_ID}] ${msg}`);
}

// Startup Cleanup
function convertLegacyRawFiles(targetRoomId?: number) {
  const baseDir = process.env.DANMAKU_DIR || path.resolve(__dirname, '../data/danmaku');
  
  const dirsToScan = [baseDir];
  if (targetRoomId) {
    dirsToScan.push(path.join(baseDir, String(targetRoomId)));
  }

  for (const dir of dirsToScan) {
    if (!fs.existsSync(dir)) continue;

    const files = fs.readdirSync(dir);
    const rawFiles = files.filter(f => f.endsWith('.raw'));
    
    for (const file of rawFiles) {
      const filePath = path.join(dir, file);
      const xmlPath = filePath.replace(/\.raw$/, '.xml');
      
      // Skip if current recording
      if (currentTempFile && path.resolve(filePath) === path.resolve(currentTempFile)) continue;

      // Check if file belongs to this room
      if (targetRoomId) {
        try {
          const fd = fs.openSync(filePath, 'r');
          const buffer = Buffer.alloc(1024); // Read enough for header
          const bytesRead = fs.readSync(fd, buffer, 0, 1024, 0);
          fs.closeSync(fd);
          
          if (bytesRead > 0) {
            const content = buffer.toString('utf-8', 0, bytesRead);
            const match = content.match(/<room_id>(\d+)<\/room_id>/);
            if (match) {
              const fileRoomId = parseInt(match[1]);
              if (fileRoomId !== targetRoomId) {
                // Belongs to another room, skip
                continue;
              }
            } else {
              // No room_id found, maybe not a valid recording file or empty
              // Skip to be safe
              continue;
            }
          }
        } catch (e) {
          // Error reading file, skip
          continue;
        }
      }
      
      log(`Found legacy raw file: ${file}, converting...`);
      try {
        let content = fs.readFileSync(filePath, 'utf-8');
        if (!content.trim().endsWith('</i>')) {
          content = content.trim() + '\n</i>';
          fs.writeFileSync(filePath, content);
        }
        fs.renameSync(filePath, xmlPath);
        log(`Converted legacy file: ${file} -> ${path.basename(xmlPath)}`);
      } catch (e: any) {
        log(`Failed to convert legacy file ${file}: ${e.message}`);
      }
    }
  }
}

// API Functions
async function getRoomInfo(shortId: number): Promise<RoomInfo> {
  const commonHeaders: any = {
    'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36',
    'Referer': `https://live.bilibili.com/${shortId}`,
    'Origin': 'https://live.bilibili.com'
  };
  if (COOKIE) commonHeaders.Cookie = COOKIE;

  const res = await axios.get(`https://api.live.bilibili.com/room/v1/Room/get_info?room_id=${shortId}`, {
    headers: commonHeaders
  });
  if (res.data.code !== 0) throw new Error(`Get Room Info Error: ${res.data.msg}`);
  
  const data = res.data.data;
  // Need to get user info for user_name
  const userRes = await axios.get(`https://api.live.bilibili.com/live_user/v1/UserInfo/get_anchor_in_room?roomid=${data.room_id}`, {
    headers: commonHeaders
  });
  const uname = userRes.data.data?.info?.uname || 'Unknown';

  return {
    room_id: data.room_id,
    title: data.title,
    uid: data.uid,
    uname: uname,
    live_status: data.live_status
  };
}

async function getDanmuConf(roomId: number) {
  const commonHeaders: any = {
    'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36',
    'Referer': `https://live.bilibili.com/${roomId}`,
    'Origin': 'https://live.bilibili.com'
  };
  if (COOKIE) commonHeaders.Cookie = COOKIE;

  try {
    // Try newer API first
    const res = await axios.get(`https://api.live.bilibili.com/xlive/web-room/v1/index/getDanmuInfo?id=${roomId}&type=0`, {
      headers: commonHeaders
    });
    if (res.data.code === 0) return res.data.data;
    if (res.data.code === -352) {
      throw new Error(`RISK_CONTROL:${res.data.code}`);
    }
  } catch (e) {
    // ignore
  }

  // Fallback to older API
  const res = await axios.get(`https://api.live.bilibili.com/room/v1/Danmu/getConf?room_id=${roomId}&platform=pc&player=web`, {
    headers: commonHeaders
  });
  
  if (res.data.code !== 0) {
    console.error('Danmu Conf Response:', JSON.stringify(res.data));
    if (res.data.code === -352) {
      throw new Error(`RISK_CONTROL:${res.data.code}`);
    }
    throw new Error(`Get Danmu Conf Error: ${res.data.message || res.data.msg}`);
  }
  
  // Adapt old API format to new format expected by main logic
  const data = res.data.data;
  return {
    token: data.token,
    host_list: data.host_server_list
  };
}

// File Operations
function initFile() {
  if (!roomInfo) return;
  
  const date = new Date();
  const dateStr = date.toISOString().replace(/T/, ' ').replace(/\..+/, '').replace(/:/g, '-');
  // Clean title for filename
  const safeTitle = roomInfo.title.replace(/[\\/:*?"<>|]/g, '_');
  const filenameBase = `${dateStr} ${safeTitle}`;
  const filename = `${filenameBase}.xml`;
  const tempFilename = `${filenameBase}.raw`;
  const baseDir = process.env.DANMAKU_DIR || path.resolve(__dirname, '../data/danmaku');
  const dir = path.join(baseDir, String(roomInfo.room_id));
  
  if (!fs.existsSync(dir)) {
    fs.mkdirSync(dir, { recursive: true });
  }
  
  currentFile = path.join(dir, filename);
  currentTempFile = path.join(dir, tempFilename);
  
  // XML Header with metadata (matching processor.ts expectations)
  const header = `<?xml version="1.0" encoding="UTF-8"?>
<i>
  <room_id>${roomInfo.room_id}</room_id>
  <room_title>${roomInfo.title}</room_title>
  <user_name>${roomInfo.uname}</user_name>
  <video_start_time>${Date.now()}</video_start_time>
`;
  
  fs.writeFileSync(currentTempFile, header);
  log(`Started recording to: ${tempFilename}`);
}

function appendToFile(line: string) {
  if (!currentTempFile) return;
  fs.appendFileSync(currentTempFile, line + '\n');
}

// Protocol Helpers
function makePacket(op: number, data: object | string) {
  const body = Buffer.from(typeof data === 'string' ? data : JSON.stringify(data));
  const header = Buffer.alloc(HEADER_SIZE);
  header.writeUInt32BE(HEADER_SIZE + body.length, 0); // Packet Length
  header.writeUInt16BE(HEADER_SIZE, 4); // Header Length
  header.writeUInt16BE(1, 6); // Proto Ver
  header.writeUInt32BE(op, 8); // Operation
  header.writeUInt32BE(1, 12); // Sequence
  return Buffer.concat([header, body]);
}

function decodePacket(buffer: Buffer) {
  const packets: any[] = [];
  let offset = 0;

  while (offset < buffer.length) {
    if (offset + HEADER_SIZE > buffer.length) break;

    const packetLen = buffer.readUInt32BE(offset);
    const headerLen = buffer.readUInt16BE(offset + 4);
    const protoVer = buffer.readUInt16BE(offset + 6);
    const op = buffer.readUInt32BE(offset + 8);
    const body = buffer.subarray(offset + headerLen, offset + packetLen);

    if (protoVer === 2) {
      // Zlib
      try {
        const decompressed = zlib.inflateSync(body);
        packets.push(...decodePacket(decompressed));
      } catch (e) { log('Zlib Decompress Error'); }
    } else if (protoVer === 3) {
      // Brotli
      try {
        const decompressed = zlib.brotliDecompressSync(body);
        packets.push(...decodePacket(decompressed));
      } catch (e) { log('Brotli Decompress Error'); }
    } else if (op === OP_MESSAGE) {
      try {
        const json = JSON.parse(body.toString('utf-8'));
        packets.push({ op, body: json });
      } catch (e) { 
        // Sometimes body is not JSON?
      }
    } else if (op === OP_HEARTBEAT_REPLY) {
      packets.push({ op, body: { count: body.readUInt32BE(0) } });
    } else if (op === OP_CONNECT_SUCCESS) {
      packets.push({ op, body: {} });
    } else {
      // For debug: other ops
      packets.push({ op, body: body.toString() });
    }

    offset += packetLen;
  }
  return packets;
}

// Main Logic
async function start() {
  try {
    roomInfo = await getRoomInfo(parseInt(ROOM_ID as string));

    // Check for legacy raw files on startup
    convertLegacyRawFiles(roomInfo.room_id);
    
    if (roomInfo.live_status !== 1) {
      if (isRecording) {
        log('Stream ended. Closing file...');
        stopRecording();
      }
      log(`Room is not live. Checking again in 60s...`);
      setTimeout(start, 60000);
      return;
    }

    log(`Room Info: ${roomInfo.title} (@${roomInfo.uname}) - LIVE`);
    
    if (!isRecording) {
      initFile();
      isRecording = true;
    }

    // Periodic live status check
    if (!checkLiveInterval) {
      checkLiveInterval = setInterval(async () => {
        try {
          const info = await getRoomInfo(parseInt(ROOM_ID as string));
          if (info.live_status !== 1) {
            log('Live check: Stream ended. Closing connection...');
            ws?.close(); // This will trigger cleanup and stopRecording via close event
          }
        } catch (e) {
          // ignore error
        }
      }, 300000); // Check every 5 minutes
    }
    
    const conf = await getDanmuConf(roomInfo.room_id);
    const token = conf.token;
    const hostList = conf.host_list;
    const host = hostList[hostIndex % hostList.length];
    hostIndex += 1;
    const hostName = host.host || host.hostname || host.ip;
    const wssPort = host.wss_port || host.wssPort || host.port || 443;
    if (!hostName) throw new Error('No valid danmu host');
    const url = `wss://${hostName}:${wssPort}/sub`;
    log(`Using danmu host: ${hostName}:${wssPort}`);
    
    const wsHeaders: any = {
      'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36',
      'Referer': `https://live.bilibili.com/${roomInfo.room_id}`,
      'Origin': 'https://live.bilibili.com'
    };
    if (COOKIE) wsHeaders.Cookie = COOKIE;
    ws = new WebSocket(url, { headers: wsHeaders });
    lastConnectAt = Date.now();
    
    ws.on('open', () => {
      log('Connected to WebSocket');
      const authPacket = makePacket(OP_USER_AUTHENTICATION, {
        uid: COOKIE_UID || 0, 
        roomid: roomInfo!.room_id,
        protover: 2,
        platform: 'web',
        type: 2,
        key: token
      });
      ws?.send(authPacket);
      authTimeout = setTimeout(() => {
        log('Authentication timeout. Reconnecting...');
        ws?.close();
      }, 8000);
    });

    ws.on('message', (data: Buffer) => {
      try {
        const packets = decodePacket(data);
        for (const p of packets) {
          if (p.op === OP_MESSAGE) {
            handleMessage(p.body);
          } else if (p.op === OP_CONNECT_SUCCESS) {
            log('Authentication Successful');
            if (authTimeout) {
              clearTimeout(authTimeout);
              authTimeout = null;
            }
            if (!heartbeatInterval) {
              heartbeatInterval = setInterval(() => {
                ws?.send(makePacket(OP_HEARTBEAT, ""));
              }, 30000);
            }
          }
        }
      } catch (err: any) {
        log(`Decode Error: ${err.message}`);
      }
    });

    ws.on('close', (code, reason) => {
      const reasonText = reason?.toString() || '';
      const duration = Date.now() - lastConnectAt;
      log(`Connection Closed (${code}) ${reasonText} after ${duration}ms. Reconnecting in 5s...`);
      cleanup();
      setTimeout(start, 5000);
    });

    ws.on('error', (err) => {
      log(`WS Error: ${err.message}`);
      ws?.close();
    });

  } catch (error: any) {
    log(`Start Error: ${error.message}`);
    const delay = String(error?.message || '').includes('RISK_CONTROL') ? 60000 : 10000;
    setTimeout(start, delay);
  }
}

function stopRecording() {
  if (currentTempFile && fs.existsSync(currentTempFile)) {
    const content = fs.readFileSync(currentTempFile, 'utf-8');
    if (!content.trim().endsWith('</i>')) {
      fs.appendFileSync(currentTempFile, '</i>');
    }
    
    // Rename .raw to .xml
    if (currentFile) {
        fs.renameSync(currentTempFile, currentFile);
        log(`Recording saved to: ${path.basename(currentFile)}`);
    }
  }
  isRecording = false;
  currentFile = null;
  currentTempFile = null;
}

function cleanup() {
  if (heartbeatInterval) clearInterval(heartbeatInterval);
  if (authTimeout) clearTimeout(authTimeout);
  if (checkLiveInterval) clearInterval(checkLiveInterval);
  heartbeatInterval = null;
  authTimeout = null;
  checkLiveInterval = null;
  ws = null;
}

let messageCount = 0;
function handleMessage(msg: any) {
  const cmd = msg.cmd || '';
  const now = Date.now();
  
  if (messageCount < 5) {
    // log(`Debug Message: ${cmd}`);
    messageCount++;
  }
  
  if (cmd.startsWith('DANMU_MSG')) {
      const info = msg.info;
      const text = info[1];
    const user = info[2][1];
    const uid = info[2][0];
    // p="timestamp,mode,size,color,timestamp_unix,pool,userid,rowId"
    // Bilibili uses milliseconds for timestamp usually
    const ts = info[0][4]; // timestamp
    
    // Construct XML line
    // <d p="0,1,25,16777215,1673856858,0,uhash,0" user="name" uid="123" timestamp="1673856858000">text</d>
    const pAttr = `0,1,25,16777215,${ts},0,${uid},0`;
    const line = `<d p="${pAttr}" user="${user}" uid="${uid}" timestamp="${ts}">${text}</d>`;
    appendToFile(line);
    // log(`[Danmu] ${user}: ${text}`);
  } else if (cmd === 'SEND_GIFT') {
    const data = msg.data;
    // <gift ts="?" giftname="..." giftcount="..." price="..." user="..." uid="..." timestamp="...">
    const line = `<gift ts="${now}" giftname="${data.giftName}" giftcount="${data.num}" price="${data.price}" user="${data.uname}" uid="${data.uid}" timestamp="${data.timestamp * 1000}" />`;
    appendToFile(line);
    // log(`[Gift] ${data.uname} sent ${data.giftName} x${data.num}`);
  } else if (cmd === 'SUPER_CHAT_MESSAGE') {
     const data = msg.data;
     // Handle SC as a special message or extend XML?
     // Processor.ts handles <sc> tag in getSessionDanmakuPaged but processDanmakuFile doesn't explicitly parse <sc> tag?
     // Wait, processor.ts processDanmakuFile (Line 141) only regexes <d> and <gift>.
     // But getSessionDanmakuPaged (Line 350) handles <sc> and <d>.
     // So I should write SC as <sc>.
     
     const line = `<sc price="${data.price}" user="${data.user_info.uname}" uid="${data.uid}" timestamp="${data.start_time * 1000}">${data.message}</sc>`;
     appendToFile(line);
     log(`[SC] ${data.user_info.uname}: ${data.message} (${data.price})`);
  }
}

start();

process.on('SIGINT', () => {
  log('Received SIGINT. Cleaning up...');
  stopRecording();
  process.exit();
});
