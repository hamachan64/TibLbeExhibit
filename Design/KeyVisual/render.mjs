// Render keyvisual.svg to PNG with the pre-installed Chromium.
// Usage: node render.mjs [scale]   (scale 1 -> 1920x1080, 2 -> 3840x2160)
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const { chromium } = require(process.env.PLAYWRIGHT_MODULE ?? 'playwright');
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const scale = Number(process.argv[2] ?? 1);
const svg = readFileSync(join(here, 'keyvisual.svg'), 'utf8');
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1920, height: 1080 }, deviceScaleFactor: scale });
await page.setContent(`<html><body style="margin:0;background:#000">${svg}</body></html>`);
const out = join(here, scale === 1 ? 'keyvisual.png' : `keyvisual@${scale}x.png`);
await page.screenshot({ path: out, clip: { x: 0, y: 0, width: 1920, height: 1080 } });
await browser.close();
console.log('wrote', out);
