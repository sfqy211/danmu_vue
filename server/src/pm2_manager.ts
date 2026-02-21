import pm2 from 'pm2';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// PM2 connection state management
let isConnected = false;

const ensureConnected = () => new Promise<void>((resolve, reject) => {
  if (isConnected) return resolve();
  pm2.connect((err) => {
    if (err) return reject(err);
    isConnected = true;
    resolve();
  });
});

const pm2List = () => new Promise<pm2.ProcessDescription[]>((resolve, reject) => {
  pm2.list((err, list) => err ? reject(err) : resolve(list));
});

const pm2Start = (scriptPath: string, options: any) => new Promise<void>((resolve, reject) => {
  pm2.start(scriptPath, options, (err) => err ? reject(err) : resolve());
});

const pm2Stop = (processName: string) => new Promise<void>((resolve, reject) => {
  pm2.stop(processName, (err) => err ? reject(err) : resolve());
});

const pm2Delete = (processName: string) => new Promise<void>((resolve, reject) => {
  pm2.delete(processName, (err) => err ? reject(err) : resolve());
});

const pm2Restart = (processName: string) => new Promise<void>((resolve, reject) => {
  pm2.restart(processName, (err) => err ? reject(err) : resolve());
});

export async function getProcessStatus() {
  await ensureConnected();
  return await pm2List();
}

export async function startRecorder(roomId: number, name: string) {
  const scriptPath = path.resolve(__dirname, 'recorder.ts');
  const processName = `danmu-${name || roomId}`;
  
  await ensureConnected();
  
  // Check if already running
  const list = await pm2List();
  const exists = list.find(p => p.name === processName);
  
  if (exists) {
    if (exists.pm2_env?.status !== 'online') {
      await pm2Restart(processName);
    }
  } else {
    try {
        await pm2Start(scriptPath, {
          name: processName,
          interpreter: "node",
          node_args: "--import tsx",
          args: `--room ${roomId}`,
          watch: false,
          autorestart: true,
          exp_backoff_restart_delay: 5000,
          env: {
            NODE_ENV: "production",
            BILI_COOKIE: process.env.BILI_COOKIE,
            DANMAKU_DIR: process.env.DANMAKU_DIR || "server/data/danmaku"
          }
        });
    } catch (e: any) {
        // Ignore if process already exists (race condition)
        if (!e.message.includes('Script already launched')) {
            throw e;
        }
    }
  }
}

export async function stopRecorder(name: string) {
  const processName = `danmu-${name}`;
  await ensureConnected();
  try {
    await pm2Stop(processName);
    await pm2Delete(processName);
  } catch (e) {
    // Ignore if process not found
  }
}

export async function restartRecorder(roomId: number, name: string) {
  const processName = `danmu-${name || roomId}`;
  await ensureConnected();
  
  try {
    await pm2Restart(processName);
  } catch (e: any) {
    // If process not found, try to start it
    if (e.message.includes('process or namespace not found')) {
        await startRecorder(roomId, name);
        return;
    }
    throw e;
  }
}

// Cleanup on exit
process.on('SIGINT', () => {
  if (isConnected) {
    pm2.disconnect();
    isConnected = false;
  }
  process.exit();
});
