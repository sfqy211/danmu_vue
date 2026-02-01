require('dotenv').config();

// 直播间列表
const rooms = [
  { id: 22642754, name: "桃几OvO" },
  { id: 673, name: "鱼鸽鸽" },
  { id: 27484357, name: "妮莉安Lily" },
];

module.exports = {
  apps: [
    // 1. 后端 API 服务
    {
      name: "danmu-api",
      script: "server/src/api.ts",
      interpreter: "node",
      node_args: "--import tsx",
      watch: ["server/src"],
      ignore_watch: ["node_modules", "server/data", "logs", "dist", "public"],
      env: {
        PORT: 3001,
        NODE_ENV: "production",
        DANMAKU_DIR: "server/data/danmaku"
      }
    },
    // 2. 弹幕录制进程 (为每个直播间启动一个进程)
    ...rooms.map(room => ({
      name: `danmu-${room.name || room.id}`,
      script: "server/src/recorder.ts",
      interpreter: "node",
      node_args: "--import tsx",
      args: `--room ${room.id}`,
      // 注意: 这里不启用 watch，防止代码变动时打断录制
      watch: false,
      autorestart: true,
      exp_backoff_restart_delay: 5000,
      env: {
        NODE_ENV: "production",
        // BILI_COOKIE 会自动从 .env 读取
        BILI_COOKIE: process.env.BILI_COOKIE,
        DANMAKU_DIR: "server/data/danmaku"
      }
    }))
  ]
};
