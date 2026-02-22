
import COS from 'cos-nodejs-sdk-v5';
import fs from 'node:fs';
import path from 'node:path';
import 'dotenv/config';

// 检查环境变量是否配置
const isCosConfigured = () => {
  return process.env.TENCENT_SECRET_ID && 
         process.env.TENCENT_SECRET_KEY && 
         process.env.COS_BUCKET && 
         process.env.COS_REGION;
};

let cos: any = null;

if (isCosConfigured()) {
  cos = new COS({
    SecretId: process.env.TENCENT_SECRET_ID,
    SecretKey: process.env.TENCENT_SECRET_KEY
  });
  console.log('COS SDK initialized successfully.');
} else {
  console.warn('COS configuration missing. Uploads will be skipped.');
}

/**
 * 上传文件到腾讯云 COS
 * @param localPath 本地文件绝对路径
 * @param remoteKey COS 中的存储路径 (例如: vup-cover/123.png)
 */
export const uploadToCos = (localPath: string, remoteKey: string): Promise<void> => {
  if (!cos || !isCosConfigured()) {
    console.warn(`[COS] Skipping upload for ${remoteKey} (Config missing)`);
    return Promise.resolve();
  }

  return new Promise((resolve, reject) => {
    // 确保路径分隔符为正斜杠
    const key = remoteKey.replace(/\\/g, '/');
    
    cos.sliceUploadFile({
      Bucket: process.env.COS_BUCKET,
      Region: process.env.COS_REGION,
      Key: key,
      FilePath: localPath
    }, (err: any, data: any) => {
      if (err) {
        console.error(`[COS] Upload failed for ${key}:`, err);
        reject(err);
      } else {
        console.log(`[COS] Upload success: ${key}`);
        resolve();
      }
    });
  });
};
