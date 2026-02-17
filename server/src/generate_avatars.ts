import fs from 'fs';
import path from 'path';
import sharp from 'sharp';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// Paths relative to this script
// Assumes this script is in server/src/ and public is in ../../public/ relative to this file
// Wait, the project structure is:
// root/
//   server/src/generate_avatars.ts
//   public/vup-bg/
// So from server/src/, we need to go up two levels to reach root, then down to public.
const ROOT_DIR = path.resolve(__dirname, '../../');
const PUBLIC_DIR = path.join(ROOT_DIR, 'public');
const BG_DIR = path.join(PUBLIC_DIR, 'vup-bg');
const AVATAR_DIR = path.join(PUBLIC_DIR, 'vup-avatar');

async function generateAvatars() {
  console.log(`Scanning for images in: ${BG_DIR}`);
  
  if (!fs.existsSync(BG_DIR)) {
    console.error(`Source directory not found: ${BG_DIR}`);
    return;
  }

  if (!fs.existsSync(AVATAR_DIR)) {
    console.log(`Creating avatar directory: ${AVATAR_DIR}`);
    fs.mkdirSync(AVATAR_DIR, { recursive: true });
  }

  const files = fs.readdirSync(BG_DIR).filter(file => /\.(png|jpg|jpeg|webp)$/i.test(file));
  
  console.log(`Found ${files.length} images. Processing...`);

  for (const file of files) {
    const inputPath = path.join(BG_DIR, file);
    const outputPath = path.join(AVATAR_DIR, file);
    
    try {
      // Check if output already exists (optional: skip if exists? No, let's overwrite to be safe)
      
      const image = sharp(inputPath);
      const metadata = await image.metadata();
      
      console.log(`Processing ${file} (${metadata.width}x${metadata.height})...`);
      
      await image
        .resize(200, 200, {
          fit: 'cover',
          position: 'center' // Crop from center
        })
        .toFile(outputPath);
        
      console.log(`  -> Saved to ${outputPath}`);
    } catch (err) {
      console.error(`  -> Error processing ${file}:`, err);
    }
  }
  
  console.log('Done!');
}

generateAvatars().catch(console.error);
