// Overlay copy-safe areas on keyvisual.png -> keyvisual_layout_guide.png (layout reference only).
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const { chromium } = require(process.env.PLAYWRIGHT_MODULE ?? 'playwright');
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
const here = dirname(fileURLToPath(import.meta.url));
const img = readFileSync(join(here, 'keyvisual.png')).toString('base64');
const box = (x, y, w, h, label) =>
  `<div style="position:absolute;left:${x}px;top:${y}px;width:${w}px;height:${h}px;border:3px dashed rgba(255,255,255,.85);background:rgba(255,255,255,.06);color:#fff;font:600 28px sans-serif;display:flex;align-items:center;justify-content:center;letter-spacing:.08em">${label}</div>`;
const html = `<body style="margin:0"><div style="position:relative;width:1920px;height:1080px;background:url(data:image/png;base64,${img})">
${box(560, 40, 800, 200, 'TITLE')}${box(660, 260, 600, 70, 'CATCH COPY')}${box(1420, 960, 460, 90, 'CREDIT / LOGO')}
<div style="position:absolute;left:96px;top:54px;right:96px;bottom:54px;border:1px solid rgba(255,255,255,.35)"></div></div></body>`;
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1920, height: 1080 } });
await page.setContent(html);
await page.screenshot({ path: join(here, 'keyvisual_layout_guide.png') });
await browser.close();
console.log('wrote keyvisual_layout_guide.png');
