
import fs from 'fs';
import path from 'path';
import { Jimp } from 'jimp';
import { Vibrant } from 'node-vibrant/node';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// Constants
const PROJECT_ROOT = path.resolve(__dirname, '..');
const OUTPUT_FILE = path.resolve(PROJECT_ROOT, 'src/constants/vup-colors.json');
const PUBLIC_DIR = path.resolve(PROJECT_ROOT, 'public');
const COVER_DIR = path.resolve(PUBLIC_DIR, 'vup-cover');

// Helper: Calculate Euclidean distance between two RGB colors
function colorDistance(c1: {r: number, g: number, b: number}, c2: {r: number, g: number, b: number}) {
  return Math.sqrt(
    Math.pow(c1.r - c2.r, 2) +
    Math.pow(c1.g - c2.g, 2) +
    Math.pow(c1.b - c2.b, 2)
  );
}

// Extract colors for a single VUP
async function extractColorsForUid(uid: string): Promise<string[] | null> {
  // ONLY use cover image (vup-cover/*.png)
  const imgPath = path.join(PUBLIC_DIR, 'vup-cover', `${uid}.png`);
  
  if (!fs.existsSync(imgPath)) {
    console.warn(`[WARN] No cover image found for UID ${uid} (checked: ${imgPath})`);
    return null;
  }
  
  try {
    // 1. Get Dominant Color using Jimp (downsample for speed/generalization)
    const image = await Jimp.read(imgPath);
    image.resize({ w: 50, h: 50 });
    
    const colorCounts: Record<string, number> = {};
    const width = image.bitmap.width;
    const height = image.bitmap.height;
    
    for (let y = 0; y < height; y++) {
      for (let x = 0; x < width; x++) {
        const color = image.getPixelColor(x, y);
        // Ignore fully transparent pixels
        if ((color & 0xFF) === 0) continue;
        
        const r = (color >>> 24) & 0xFF;
        const g = (color >>> 16) & 0xFF;
        const b = (color >>> 8) & 0xFF;
        
        // Quantize to group similar colors
        const qr = Math.min(255, Math.round(r / 10) * 10);
        const qg = Math.min(255, Math.round(g / 10) * 10);
        const qb = Math.min(255, Math.round(b / 10) * 10);
        
        const key = `${qr},${qg},${qb}`;
        colorCounts[key] = (colorCounts[key] || 0) + 1;
      }
    }
    
    // Find dominant color from histogram
    let dominantColor = { r: 255, g: 255, b: 255 }; // Default white
    let maxCount = 0;
    for (const key in colorCounts) {
      if (colorCounts[key] > maxCount) {
        maxCount = colorCounts[key];
        const [r, g, b] = key.split(',').map(Number);
        dominantColor = { r, g, b };
      }
    }
    
    // 2. Get Vibrant Palette
    const v = new Vibrant(imgPath);
    const palette = await v.getPalette();
    
    // Collect candidate colors
    const candidates: { r: number, g: number, b: number, pop: number }[] = [];
    
    for (const name in palette) {
      // @ts-ignore
      const swatch = palette[name];
      if (swatch) {
        const [r, g, b] = swatch.rgb;
        candidates.push({
          r, g, b,
          pop: swatch.population
        });
      }
    }
    
    // Sort by population
    candidates.sort((a, b) => b.pop - a.pop);
    
    // Selection Strategy:
    // 1. Start with the dominant color (usually background)
    const selectedColors: { r: number, g: number, b: number }[] = [dominantColor];
    
    // 2. Add distinct colors from candidates
    for (const cand of candidates) {
      if (selectedColors.length >= 9) break;
      
      let isDistinct = true;
      for (const sel of selectedColors) {
        // Threshold: 60 for RGB distance
        if (colorDistance(cand, sel) < 60) {
          isDistinct = false;
          break;
        }
      }
      
      if (isDistinct) {
        selectedColors.push(cand);
      }
    }
    
    // 3. Fallback: Relax constraint if we don't have enough colors
    if (selectedColors.length < 9) {
      for (const cand of candidates) {
        if (selectedColors.length >= 9) break;
        let isDistinct = true;
        for (const sel of selectedColors) {
          if (colorDistance(cand, sel) < 30) {
            isDistinct = false;
            break;
          }
        }
        if (isDistinct) {
           selectedColors.push(cand);
        }
      }
    }
    
    // 4. Fill with duplicates if absolutely necessary
    // Use modulo to cycle through existing colors instead of just repeating the first one
    const originalLength = selectedColors.length;
    while (selectedColors.length < 9) {
      const index = selectedColors.length % originalLength;
      selectedColors.push(selectedColors[index]);
    }
    
    return selectedColors.map(c => `rgb(${Math.round(c.r)}, ${Math.round(c.g)}, ${Math.round(c.b)})`);
    
  } catch (err) {
    console.error(`[ERROR] Failed to process UID ${uid}:`, err);
    return null;
  }
}

async function main() {
  console.log('🎨 Starting VUP Theme Color Extraction...');

  if (!fs.existsSync(COVER_DIR)) {
    console.error(`[FATAL] cover directory not found at ${COVER_DIR}`);
    process.exit(1);
  }

  const uids = fs.readdirSync(COVER_DIR)
    .map(filename => path.parse(filename))
    .filter(file => file.ext.toLowerCase() === '.png' && /^\d+$/.test(file.name))
    .map(file => file.name)
    .sort((a, b) => a.localeCompare(b, 'zh-CN'));

  console.log(`Found ${uids.length} VUPs.`);
  
  const colorMap: Record<string, string[]> = {};
  
  for (const uid of uids) {
    process.stdout.write(`Processing ${uid}... `);
    const colors = await extractColorsForUid(uid);
    if (colors) {
      colorMap[uid] = colors;
      console.log('✅');
    } else {
      console.log('❌');
    }
  }
  
  // Write to JSON file
  fs.writeFileSync(OUTPUT_FILE, JSON.stringify(colorMap, null, 2));
  console.log(`✨ Color map written to ${OUTPUT_FILE}`);
}

main().catch(err => {
  console.error('Unhandled error:', err);
  process.exit(1);
});
