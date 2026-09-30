// Rendu des clips de demonstration : capture image par image dans Chromium headless, puis ffmpeg.
//
//   node render.mjs                              tous les clips, les deux themes, dans ../DockPad-media/videos
//   node render.mjs --clip 01-launcher --theme dark
//   node render.mjs --clip 01-launcher --stills 1.5,3.4   images cles en PNG, pour relecture
//   node render.mjs --serve                      http://localhost:4173/clips/01-launcher.html
//
// Les icones sont lues sur le disque, jamais copiees dans le depot : ce sont des logos de produits.
// Ordre de recherche : DOCKPAD_DEMO_ICONS (ou C:\dev\Dock-icons), puis les icones d'executables
// extraites par extract-icons.ps1 dans %TEMP%\dockpad-motion-icons, puis Assets\ du depot.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, '..', '..');
// Les rendus ne vivent pas dans ce depot : 64 Mo de GIF et de MP4 alourdiraient chaque clone. Ils vont
// dans le depot syl-craft/DockPad-media, clone a cote de celui-ci, d'ou le README les cite en URL raw.
const outDir = process.env.DOCKPAD_MEDIA_DIR || path.resolve(repo, '..', 'DockPad-media', 'videos');
const FPS = 60;

const ICON_DIRS = [
  process.env.DOCKPAD_DEMO_ICONS || 'C:\\dev\\Dock-icons',
  path.join(process.env.TEMP || '', 'dockpad-motion-icons'),
  path.join(repo, 'Assets'),
];

const args = process.argv.slice(2);
const opt = name => { const i = args.indexOf(`--${name}`); return i < 0 ? null : (args[i + 1] ?? true); };

const TYPES = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.png': 'image/png', '.svg': 'image/svg+xml' };

function serve(port) {
  const server = http.createServer((req, res) => {
    const url = decodeURIComponent(new URL(req.url, 'http://x').pathname);
    let file = null;
    if (url.startsWith('/icons/')) {
      const name = path.basename(url);
      file = ICON_DIRS.map(d => path.join(d, name)).find(f => fs.existsSync(f));
      if (!file) console.warn(`  icone absente : ${name}`);
    } else {
      const f = path.join(here, url);
      if (f.startsWith(here) && fs.existsSync(f) && fs.statSync(f).isFile()) file = f;
    }
    if (!file) { res.writeHead(404); res.end(); return; }
    res.writeHead(200, { 'Content-Type': TYPES[path.extname(file)] || 'application/octet-stream' });
    fs.createReadStream(file).pipe(res);
  });
  return new Promise(ok => server.listen(port, () => ok(server)));
}

function ffmpeg(argv) {
  const p = spawn('ffmpeg', ['-hide_banner', '-loglevel', 'error', '-y', ...argv], { stdio: ['pipe', 'inherit', 'inherit'] });
  const done = new Promise((ok, ko) => p.on('close', code => (code === 0 ? ok() : ko(new Error(`ffmpeg ${code}`)))));
  return { stdin: p.stdin, done };
}

const write = (stream, buf) => new Promise(ok => (stream.write(buf) ? ok() : stream.once('drain', ok)));

async function renderClip(browser, base, clip, theme, stills) {
  const page = await browser.newPage({ viewport: { width: 1920, height: 1080 }, deviceScaleFactor: 1 });
  await page.goto(`${base}/clips/${clip}.html?render=1&theme=${theme}`);
  await page.waitForFunction(() => window.READY === true, null, { timeout: 30000 });
  const duration = await page.evaluate(() => window.DURATION);
  const stage = page.locator('#stage');

  if (stills) {
    const dir = path.join(here, 'out', 'stills');
    fs.mkdirSync(dir, { recursive: true });
    for (const t of stills) {
      await page.evaluate(x => window.seek(x), t);
      const f = path.join(dir, `${clip}-${theme}-${t.toFixed(2)}.png`);
      await stage.screenshot({ path: f });
      console.log(`  ${f}`);
    }
    await page.close();
    return;
  }

  if (!fs.existsSync(path.dirname(outDir))) throw new Error(`depot media introuvable : ${path.dirname(outDir)} — git clone https://github.com/syl-craft/DockPad-media a cote de DockPad, ou DOCKPAD_MEDIA_DIR`);
  fs.mkdirSync(outDir, { recursive: true });
  const mp4 = path.join(outDir, `${clip}-${theme}.mp4`);
  const gif = path.join(outDir, `${clip}-${theme}.gif`);
  const frames = Math.round(duration * FPS);
  const enc = ffmpeg(['-f', 'image2pipe', '-framerate', String(FPS), '-c:v', 'png', '-i', '-',
    '-c:v', 'libx264', '-preset', 'slow', '-crf', '18', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', mp4]);
  const t0 = Date.now();
  for (let i = 0; i < frames; i++) {
    await page.evaluate(x => window.seek(x), i / FPS);
    await write(enc.stdin, await stage.screenshot({ type: 'png' }));
  }
  enc.stdin.end();
  await enc.done;
  await page.close();

  // GIF pour le README : 960 x 540, 20 i/s, palette calculee sur le clip lui-meme.
  const g = ffmpeg(['-i', mp4, '-filter_complex',
    'fps=20,scale=960:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=192:stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle',
    '-loop', '0', gif]);
  g.stdin.end();
  await g.done;
  const mb = f => (fs.statSync(f).size / 1048576).toFixed(2);
  console.log(`  ${clip}-${theme} : ${frames} images en ${((Date.now() - t0) / 1000).toFixed(0)} s — mp4 ${mb(mp4)} Mo, gif ${mb(gif)} Mo`);
}

const port = 4173;
const server = await serve(port);
const base = `http://localhost:${port}`;

if (opt('serve')) {
  console.log(`${base}/clips/  (Ctrl+C pour arreter)`);
} else {
  const all = fs.readdirSync(path.join(here, 'clips')).filter(f => f.endsWith('.html')).map(f => f.replace('.html', '')).sort();
  const clips = opt('clip') ? [opt('clip')] : all;
  const themes = opt('theme') ? [opt('theme')] : ['light', 'dark'];
  const stills = opt('stills') ? String(opt('stills')).split(',').map(Number) : null;
  const browser = await chromium.launch();
  try {
    for (const c of clips) for (const th of themes) await renderClip(browser, base, c, th, stills);
  } finally {
    await browser.close();
    server.close();
  }
}
