
import sqlite3 from 'sqlite3';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import 'dotenv/config';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// 1. 硬编码的房间列表 (从 ecosystem.config.cjs 复制而来)
const rooms = [
  { id: 22642754, name: "桃几OvO" },
  { id: 673, name: "鱼鸽鸽" },
  { id: 27484357, name: "妮莉安Lily" },
];

const dbPath = process.env.DB_PATH || path.resolve(__dirname, '../data/danmaku_data.db');
const db = new sqlite3.Database(dbPath);

const dbRun = (sql: string, params: any[] = []) => new Promise<any>((resolve, reject) => {
  db.run(sql, params, function (err) { err ? reject(err) : resolve(this); });
});

export async function migrate() {
  console.log(`Migrating rooms to database: ${dbPath}`);
  
  // 确保表存在
  await dbRun(`
    CREATE TABLE IF NOT EXISTS rooms (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      room_id INTEGER UNIQUE,
      uid TEXT,
      name TEXT,
      is_active INTEGER DEFAULT 1,
      auto_record INTEGER DEFAULT 1,
      created_at DATETIME DEFAULT CURRENT_TIMESTAMP
    )
  `);

  let count = 0;
  for (const room of rooms) {
    try {
      await dbRun(
        `INSERT OR IGNORE INTO rooms (room_id, name, is_active, auto_record) VALUES (?, ?, 1, 1)`,
        [room.id, room.name]
      );
      count++;
    } catch (e) {
      console.error(`Error inserting room ${room.name}:`, e);
    }
  }

  console.log(`Migration completed. Processed ${count} rooms.`);
  // Do not close db here if called from another module, but since this script creates its own connection...
  // Ideally, we should close it if this is a standalone run.
  // If imported, we might want to keep it open or let the caller handle it.
  // But this script creates `const db = ...` at module level.
  // Let's modify to close only if standalone.
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  migrate().then(() => db.close()).catch(console.error);
}
