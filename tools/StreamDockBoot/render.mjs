// Rend boot.html en 854 x 480 dans Assets/StreamDockBoot.png, avec le Playwright de tools/MotionDemo (npm install là-bas).
import { createRequire } from 'module';
import { fileURLToPath, pathToFileURL } from 'url';
import path from 'path';

const here = path.dirname(fileURLToPath(import.meta.url));
const require = createRequire(path.join(here, '../MotionDemo/package.json'));
const { chromium } = require('playwright');

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 854, height: 480 } });
await page.goto(pathToFileURL(path.join(here, 'boot.html')).href);
await page.screenshot({ path: path.join(here, '../../Assets/StreamDockBoot.png') });
await browser.close();
