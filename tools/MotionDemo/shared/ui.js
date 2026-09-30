// Composants partages des clips. Tout est en coordonnees de l'application (fenetre 850 x 622),
// mesurees sur docs/screenshots/window-en.png : tuile 108 x 90, pas de 118 x 100, grille en (76, 71).
import { el, style, clamp, lerp, prog, E, env, press } from './engine.js';

export const icon = name => `/icons/${name}.png`;

// Bandes de type : QuickAccessWindow.xaml.cs, identiques dans les deux themes.
export const BAND = { run: '#A8CCEA', folder: '#F5CC80', url: '#92C690', term: '#C4ADE0', proc: '#F4A4A4' };
export const TYPE = {
  run: ['Command', '#3B82C4'], folder: ['Folder', '#C98A12'], url: ['URL', '#3E9A3B'],
  term: ['Terminal', '#8B5CC4'], proc: ['Process', '#D0544F'],
};

export const GRID = { x: 76, y: 71, px: 118, py: 100, w: 108, h: 90 };
export const tileXY = (r, c) => ({ x: GRID.x + c * GRID.px, y: GRID.y + r * GRID.py });
export const tileCenter = (r, c) => { const p = tileXY(r, c); return { x: p.x + 54, y: p.y + 45 }; };

const T = (r, c, name, ic, type) => ({ r, c, name, icon: ic, type });

/** Grille de demonstration : trois pages, les cinq types, quelques cases vides. Noms neutres. */
export const PAGES = [
  [
    T(0, 0, 'Claude Code', 'claude-code', 'term'), T(0, 1, 'VS Code', 'vscode', 'run'),
    T(0, 2, 'Terminal', 'terminal', 'term'), T(0, 3, 'PowerShell', 'powershell', 'term'),
    T(0, 4, 'Fusion 360', 'fusion360', 'run'), T(0, 5, 'Bambu Studio', 'bambu-studio', 'run'),
    T(1, 0, 'C:\\dev', 'folder', 'folder'), T(1, 1, 'Projects', 'folder', 'folder'),
    T(1, 2, '3D Prints', 'folder', 'folder'), T(1, 3, 'Documents', 'folder', 'folder'),
    T(1, 5, 'Explorer', 'explorer', 'folder'),
    T(2, 0, 'Azure DevOps', 'azure-devops', 'url'), T(2, 1, 'Pull requests', 'pull-requests', 'url'),
    T(2, 2, 'Board', 'task-board', 'url'), T(2, 3, 'Gmail', 'gmail', 'url'),
    T(2, 4, 'Figma', 'chrome', 'url'), T(2, 5, 'Calendar', 'chrome', 'url'),
    T(3, 0, 'Task Manager', 'taskmgr', 'proc'), T(3, 1, 'Notepad', 'notepad', 'run'),
  ],
  [
    T(0, 0, 'Linear', 'linear', 'url'), T(0, 1, 'Visual Studio', 'visual-studio', 'proc'),
    T(0, 2, 'YouTube Music', 'youtube-music', 'url'), T(0, 3, 'Edge', 'edge', 'run'),
    T(0, 4, 'Chrome', 'chrome', 'run'),
    T(1, 0, 'DockPad', 'dockpad-folder', 'folder'), T(1, 1, 'Database', 'diy-database-folder', 'folder'),
    T(1, 2, 'Backups', 'backup', 'folder'),
    T(2, 0, 'Scheduled', 'scheduled', 'run'), T(2, 1, 'DevOps jobs', 'devops-jobs', 'url'),
    T(2, 3, 'Code review', 'pull-requests', 'url'),
  ],
  [
    T(0, 0, 'Notes', 'notepad', 'run'), T(0, 1, 'Cloud Shell', 'powershell', 'term'),
    T(1, 0, 'Downloads', 'folder', 'folder'),
  ],
];

export function tileHTML(t) {
  return `<div class="tile" data-k="${t.r}:${t.c}">
    <div class="hover" style="background:${BAND[t.type]}"></div>
    <img class="ic" src="${icon(t.icon)}">
    <div class="nm">${t.name}</div>
    <div class="band" style="background:${BAND[t.type]}"></div></div>`;
}
const emptyHTML = (r, c) => `<div class="tile empty" data-k="${r}:${c}"><div class="plus">+</div></div>`;

/** Place une page de tuiles dans un conteneur ; rend {key: element}. */
export function buildPage(container, tiles) {
  const map = {};
  for (let r = 0; r < 4; r++) for (let c = 0; c < 6; c++) {
    const t = tiles.find(x => x.r === r && x.c === c);
    const node = el(t ? tileHTML(t) : emptyHTML(r, c));
    const p = tileXY(r, c);
    style(node, { left: `${p.x}px`, top: `${p.y - 46}px` });
    container.appendChild(node);
    map[`${r}:${c}`] = node;
  }
  return map;
}

/**
 * Fenetre DockPad complete. opts : pages, usage ('single' | 'tabs' | null), hotkey.
 * Rend les references que les clips animent.
 */
export function buildDock(parent, opts = {}) {
  const pages = opts.pages || PAGES;
  const root = el(`<div class="dock">
    <div class="toolbar">
      <div class="tbtn sym" data-b="menu" style="left:12px;width:32px">☰</div>
      <div class="search"><span class="q"></span><span class="caret"></span><span class="ph" style="color:var(--text-faint)"></span></div>
      <div class="tbtn sym" data-b="mode" style="left:673px;width:40px">▦</div>
      <div class="tbtn sym" data-b="lock" style="left:721px;width:40px">🔒</div>
      <div class="tbtn" data-b="min" style="left:769px;width:26px">─</div>
      <div class="tbtn sym" data-b="hide" style="left:803px;width:27px">⬇</div>
    </div>
    <div class="grid-clip"><div class="strip"></div></div>
    <div class="footer">
      <div class="version mono">v1.25.2</div>
      <div class="hotkey mono">${opts.hotkey || 'Alt + Space'}</div>
    </div>
  </div>`);
  parent.appendChild(root);
  const strip = root.querySelector('.strip');
  const pageEls = [], tiles = [];
  pages.forEach((p, i) => {
    const pg = el(`<div class="page" style="left:${i * 850}px"></div>`);
    strip.appendChild(pg);
    pageEls.push(pg);
    tiles.push(buildPage(pg, p));
  });
  const footer = root.querySelector('.footer');
  const pbtns = [];
  const n = pages.length + 1;
  const x0 = 425 - (n * 36 - 6) / 2;
  for (let i = 0; i < n; i++) {
    const b = el(`<div class="pbtn">${i < pages.length ? i + 1 : '+'}</div>`);
    style(b, { left: `${x0 + i * 36}px` });
    footer.appendChild(b);
    pbtns.push(b);
  }
  const btn = name => root.querySelector(`[data-b="${name}"]`);
  const dock = {
    root, strip, pageEls, tiles, pbtns, pages,
    search: root.querySelector('.search'), q: root.querySelector('.search .q'),
    caret: root.querySelector('.search .caret'), btn,
    x: 0, y: 0, s: 1,
  };
  root.querySelector('.search .caret').style.opacity = 0;
  if (opts.usage) dock.usage = buildUsage(root, opts.usage);
  return dock;
}

/** Positionne la fenetre sur la scene et memorise la transformation pour toStage. */
export function placeDock(dock, x, y, s, extra = {}) {
  dock.x = x; dock.y = y; dock.s = s;
  const k = extra.scale ?? 1;
  const w = 850 * s, h = 622 * s;
  style(dock.root, {
    transform: `translate(${x + w * (1 - k) / 2}px, ${y + h * (1 - k) / 2}px) scale(${s * k})`,
    opacity: extra.opacity ?? 1,
  });
}
export const toStage = (dock, x, y) => ({ x: dock.x + x * dock.s, y: dock.y + y * dock.s });

/** Page affichee (valeur continue pour le glissement). */
export function showPage(dock, pos) {
  dock.strip.style.transform = `translateX(${-pos * 850}px)`;
  dock.pbtns.forEach((b, i) => b.classList.toggle('active', i === Math.round(pos)));
}

/** Survol et enfoncement d'une tuile. hover, down : 0..1 */
export function tileState(node, hover = 0, down = 0, flash = 0) {
  node.querySelector('.hover') && (node.querySelector('.hover').style.opacity = hover * .24 + flash * .5);
  node.style.transform = `scale(${1 - down * .06 + flash * .04})`;
  node.style.borderColor = hover > .5 ? 'var(--border-hover)' : '';
}

/** Reinitialise toutes les tuiles (a appeler en tete de chaque render). */
export function resetTiles(dock) {
  dock.tiles.forEach(map => Object.values(map).forEach(n => { n.style.transform = ''; n.style.borderColor = '';
    const h = n.querySelector('.hover'); if (h) h.style.opacity = 0; }));
}

// ---------- Bandeau Usage IA ----------
export const PROVIDERS = {
  claude: { name: 'Claude', color: '#D97757', glyph: '✳', model: 'claude-opus-5',
    gauges: [{ pct: 62, label: 'session', reset: '↻ 19:32' }, { pct: 44, label: 'week', reset: '↻ Wed 16:52' }],
    metrics: [['Session', 12400, 'k'], ['Day', 86000, 'k'], ['Month', 1200000, 'M'], ['Requests', 47, ''], ['Est. cost', 4, '$'], ['Model', 'claude-opus-5']] },
  codex: { name: 'Codex', color: '#10A37F', glyph: 'C', model: 'gpt-5-codex',
    gauges: [{ pct: 27, label: 'week', reset: '↻ Mon 20:00' }],
    metrics: [['Session', 8100, 'k'], ['Day', 54000, 'k'], ['Month', 760000, 'k'], ['Requests', 31, ''], ['Est. cost', null, '$'], ['Model', 'gpt-5-codex']] },
};

export function fmtTokens(v, unit) {
  if (v == null) return '—';
  if (unit === '$') return `$${Math.round(v)}`;
  const one = x => x.toFixed(1).replace(/\.0$/, '');
  if (v >= 1e6) return `${one(v / 1e6)}M`;
  if (v >= 1e3 && unit) return `${one(v / 1e3)}k`;
  return `${Math.round(v)}`;
}

/** Couleur de jauge (UsageFormat) : rouge sous le seuil restant, orange sous 50 % restant, sinon vert. */
export function gaugeColor(pct, threshold = 15) {
  const remaining = 100 - pct;
  return remaining < threshold ? '#E5484D' : remaining < 50 ? '#F5A623' : '#34A853';
}

export function buildUsage(root, mode) {
  const tabs = mode === 'tabs';
  const u = el(`<div class="usage">
    <div class="u-row u-main">
      <span class="u-head" style="display:${tabs ? 'none' : 'flex'};align-items:center;gap:7px">
        <span class="pastille sym" style="background:#D97757">✳</span><span class="u-name">Claude</span><span class="badge-demo">demo</span></span>
      <span style="display:${tabs ? 'flex' : 'none'};gap:6px">
        <div class="utab" data-p="claude"><span class="pastille sym" style="background:#D97757">✳</span>Claude <span class="badge-demo">demo</span></div>
        <div class="utab" data-p="codex"><span class="pastille" style="background:#10A37F;font-weight:700">C</span>Codex <span class="badge-demo">demo</span></div>
      </span>
      <div class="gauge g0"><span class="pct"></span><span class="lbl"></span><div class="bar"><div class="fill"></div></div><span class="reset"></span></div>
      <div class="gauge g1"><span class="pct"></span><span class="lbl"></span><div class="bar"><div class="fill"></div></div><span class="reset"></span></div>
      <span class="u-gap" style="flex:1;display:none"></span>
      <span class="link" style="display:flex;width:20px;justify-content:center"><span class="pastille sym pl" style="width:20px;height:20px;background:#D97757">✳</span><span class="spinner" style="display:none"></span></span>
    </div>
    <div class="u-sep"></div>
    <div class="u-metrics"></div>
  </div>`);
  root.appendChild(u);
  const metrics = u.querySelector('.u-metrics');
  const mEls = [];
  for (let i = 0; i < 6; i++) {
    const m = el('<div class="metric"><div class="k"></div><div class="v"></div></div>');
    metrics.appendChild(m); mEls.push(m);
  }
  return { el: u, g: [u.querySelector('.g0'), u.querySelector('.g1')], mEls, gap: u.querySelector('.u-gap'),
    tabs: [...u.querySelectorAll('.utab')], link: u.querySelector('.pl'), spinner: u.querySelector('.spinner') };
}

/**
 * Pose l'etat du bandeau. fill : 0..1 remplissage des jauges, count : 0..1 defilement des compteurs.
 * gauges = false : quota encore inconnu, la jauge entiere disparait (jamais « 0 % »).
 * pct / values : valeurs qui remplacent celles du fournisseur (jauge qui franchit le seuil, compteurs qui montent).
 */
export function renderUsage(u, provider, { fill = 1, count = 1, loading = 0, t = 0, gauges = true, pct = {}, values = {}, threshold = 15 } = {}) {
  const p = PROVIDERS[provider];
  u.tabs.forEach(tb => tb.classList.toggle('sel', tb.dataset.p === provider));
  u.g.forEach((node, i) => {
    const g = p.gauges[i];
    if (!g || !gauges) { node.style.display = 'none'; return; }
    const value = pct[i] ?? g.pct;
    const shown = value * fill;
    const color = gaugeColor(shown, threshold);
    node.style.display = 'flex';
    node.querySelector('.pct').textContent = `${Math.round(shown)}%`;
    node.querySelector('.pct').style.color = color;
    node.querySelector('.lbl').textContent = g.label;
    node.querySelector('.fill').style.width = `${shown}%`;
    node.querySelector('.fill').style.background = color;
    node.querySelector('.reset').textContent = g.reset;
  });
  u.gap.style.display = gauges ? 'none' : 'block';
  p.metrics.forEach(([k, v, unit], i) => {
    const m = u.mEls[i];
    const val = values[i] ?? v;
    m.querySelector('.k').textContent = k;
    m.querySelector('.v').textContent = typeof val === 'string' ? (count > .3 ? val : '—')
      : count <= 0 ? '—' : fmtTokens(val == null ? null : val * E.out(count), unit);
  });
  u.link.style.background = p.color;
  u.link.textContent = p.glyph;
  u.link.style.fontWeight = provider === 'codex' ? 700 : 400;
  u.link.style.display = loading > .5 ? 'none' : 'inline-flex';
  u.spinner.style.display = loading > .5 ? 'inline-block' : 'none';
  u.spinner.style.transform = `rotate(${t * 540}deg)`;
}

// ---------- Scene : camera, curseur, legende, touches ----------
export function stage() {
  const st = document.getElementById('stage');
  const camera = el('<div id="camera"></div>');
  st.appendChild(camera);
  return { st, camera };
}

/** Zoom de z : le point (fx, fy) de la scene vient se placer en (tx, ty) a l'ecran (par defaut, il ne bouge pas). */
export function setCamera(camera, z, fx, fy, tx = fx, ty = fy) {
  camera.style.transform = `translate(${tx}px, ${ty}px) scale(${z}) translate(${-fx}px, ${-fy}px)`;
}

const CURSOR_SVG = `<svg viewBox="0 0 30 44" width="30" height="44"><path d="M3 3 L3 33 L10.5 26 L15.5 38 L20.5 36 L15.5 24.5 L25.5 24.5 Z" fill="#fff" stroke="#111" stroke-width="2" stroke-linejoin="round"/></svg>`;

export function cursor(layer) {
  const c = el(`<div class="cursor">${CURSOR_SVG}</div>`);
  const r = el('<div class="ripple"></div>');
  layer.appendChild(r); layer.appendChild(c);
  return { c, r };
}

/**
 * Curseur : path = [[t, x, y], ...] en coordonnees de scene, clicks = [t, ...].
 * visible = [a, b] : fondu d'entree et de sortie.
 */
export function renderCursor(cur, t, path, clicks = [], visible = [0, 999]) {
  const [x, y] = trackXY(t, path);
  let down = 0;
  for (const tc of clicks) down = Math.max(down, press(t, tc, .08));
  const op = env(t, visible[0], visible[1], .2);
  style(cur.c, { transform: `translate(${x - 3}px, ${y - 3}px) scale(${1 - down * .14})`, opacity: op });
  let rp = null;
  for (const tc of clicks) if (t >= tc && t < tc + .45) rp = (t - tc) / .45;
  if (rp == null) cur.r.style.opacity = 0;
  else style(cur.r, { left: `${x}px`, top: `${y}px`, opacity: (1 - rp) * .8 * op, transform: `scale(${.3 + rp * .9})` });
  return { x, y };
}

function trackXY(t, path) {
  const keys = path.map(([tt, x, y]) => [tt, [x, y]]);
  return track2(t, keys);
}
function track2(t, keys) {
  if (t <= keys[0][0]) return keys[0][1];
  for (let i = 1; i < keys.length; i++) {
    if (t <= keys[i][0]) {
      const [t0, v0] = keys[i - 1], [t1, v1] = keys[i];
      const p = E.inOut(clamp((t - t0) / (t1 - t0)));
      return [lerp(v0[0], v1[0], p), lerp(v0[1], v1[1], p)];
    }
  }
  return keys[keys.length - 1][1];
}

export function legend(st) {
  const box = el('<div class="legend"><span></span></div>');
  st.appendChild(box);
  const span = box.firstChild;
  return (t, items) => {
    const it = items.find(([a, b]) => t >= a && t < b);
    if (!it) { box.style.opacity = 0; return; }
    const [a, b, text] = it;
    const o = env(t, a, b, .3);
    span.textContent = text;
    style(box, { opacity: o, transform: `translateY(${(1 - o) * 16}px)` });
  };
}

/** Touches affichees : keys = [{label, a, b, down: [[t0, t1], ...], accent}] */
export function keycaps(st, cls = '') {
  const box = el(`<div class="keys ${cls}"></div>`);
  st.appendChild(box);
  return (t, keys) => {
    box.innerHTML = '';
    for (const k of keys) {
      if (t < k.a || t >= k.b) continue;
      const o = env(t, k.a, k.b, .15);
      const isDown = (k.down || []).some(([d0, d1]) => t >= d0 && t < d1);
      const n = el(`<div class="key ${isDown ? 'down' : ''} ${k.accent && isDown ? 'accent' : ''}">${k.label}</div>`);
      style(n, { opacity: o, transform: `translateY(${(isDown ? 4 : 0) + (1 - o) * 12}px)` });
      box.appendChild(n);
    }
  };
}

// ---------- Fenetres generiques ----------
export function appWindow(layer, { w, h, icon: ic, title, body }) {
  const win = el(`<div class="gwin" style="width:${w}px;height:${h}px">
    <div class="tb"><img src="${icon(ic)}"><span>${title}</span><span class="ctl"><span>─</span><span>☐</span><span>✕</span></span></div>
    <div class="body" style="position:absolute;left:0;right:0;top:38px;bottom:0">${body}</div></div>`);
  layer.appendChild(win);
  return win;
}

/**
 * Ouverture « zoom depuis la tuile » : de la boite source (scene) a la boite cible, puis fermeture.
 * open = [t0, t1] ouverture ; close = [t2, t3] fermeture.
 */
export function renderLaunch(win, t, from, to, open, close) {
  const pOpen = prog(t, open[0], open[1], E.out);
  const pClose = prog(t, close[0], close[1], E.in);
  if (t < open[0] || t >= close[1]) { win.style.opacity = 0; win.style.visibility = 'hidden'; return; }
  win.style.visibility = 'visible';
  const p = pOpen * (1 - pClose * .0);
  const sx = lerp(from.w / to.w, 1, p), sy = lerp(from.h / to.h, 1, p);
  const cx = lerp(from.x + from.w / 2, to.x + to.w / 2, p), cy = lerp(from.y + from.h / 2, to.y + to.h / 2, p);
  const k = 1 - pClose * .06;
  style(win, {
    left: `${to.x}px`, top: `${to.y}px`, transformOrigin: '50% 50%',
    transform: `translate(${cx - (to.x + to.w / 2)}px, ${cy - (to.y + to.h / 2)}px) scale(${sx * k}, ${sy * k})`,
    opacity: Math.min(1, pOpen * 2) * (1 - pClose),
  });
}

export const TERMINAL_BODY = lines => `<div class="mono" style="position:absolute;inset:0;background:var(--term-bg);color:var(--term-text);font-size:17px;line-height:27px;padding:18px 22px">${lines.join('<br>')}</div>`;

export const EDITOR_BODY = () => {
  const code = [[70, '#569CD6', 120, '#9CDCFE'], [40, '#C586C0', 200, '#DCDCAA'], [90, '#4EC9B0', 60, '#CE9178'],
    [30, '#569CD6', 260, '#9CDCFE'], [120, '#DCDCAA', 90, '#CE9178'], [60, '#C586C0', 150, '#4EC9B0'],
    [80, '#569CD6', 210, '#9CDCFE'], [50, '#DCDCAA', 110, '#CE9178'], [100, '#4EC9B0', 170, '#9CDCFE']];
  const rows = code.map(([a, ca, b, cb], i) => `<div style="display:flex;gap:10px;height:26px;align-items:center;padding-left:${(i % 3) * 26}px">
    <div style="width:${a}px;height:10px;border-radius:5px;background:${ca};opacity:.85"></div>
    <div style="width:${b}px;height:10px;border-radius:5px;background:${cb};opacity:.7"></div></div>`).join('');
  return `<div style="position:absolute;inset:0;display:flex;background:#1E1E1E">
    <div style="width:200px;background:#252526;padding:16px 14px;display:flex;flex-direction:column;gap:12px">
      ${[120, 90, 140, 100, 80, 130].map(w => `<div style="width:${w}px;height:9px;border-radius:4px;background:#5A5A5A"></div>`).join('')}</div>
    <div style="flex:1;padding:20px 26px">${rows}</div></div>`;
};

export const BROWSER_BODY = (url, profile) => `<div style="position:absolute;inset:0;background:var(--surface-card)">
  <div style="height:46px;display:flex;align-items:center;gap:12px;padding:0 16px;border-bottom:1px solid var(--border)">
    <div style="flex:1;height:30px;border-radius:15px;background:var(--surface);display:flex;align-items:center;padding:0 16px;font-size:14px;color:var(--text-subtle)" class="mono">${url}</div>
    ${profile ? `<div style="height:28px;border-radius:14px;background:var(--accent-tint);color:var(--accent);font-size:13px;font-weight:600;display:flex;align-items:center;padding:0 12px">${profile}</div>` : ''}
  </div>
  <div style="padding:28px 34px;display:flex;flex-direction:column;gap:16px">
    <div style="width:55%;height:18px;border-radius:9px;background:var(--border)"></div>
    <div style="width:85%;height:11px;border-radius:6px;background:var(--surface-muted)"></div>
    <div style="width:78%;height:11px;border-radius:6px;background:var(--surface-muted)"></div>
    <div style="width:82%;height:11px;border-radius:6px;background:var(--surface-muted)"></div>
    <div style="display:flex;gap:16px;margin-top:10px">${[1, 2, 3].map(() => '<div style="flex:1;height:110px;border-radius:10px;background:var(--surface)"></div>').join('')}</div>
  </div></div>`;

/** Centre d'un element en coordonnees de scene (a mesurer au build, camera au repos). */
export function centerOf(node) {
  const r = node.getBoundingClientRect(), s = document.getElementById('stage').getBoundingClientRect();
  return { x: r.left - s.left + r.width / 2, y: r.top - s.top + r.height / 2, w: r.width, h: r.height, l: r.left - s.left, t: r.top - s.top };
}

/** Apparition d'une fenetre surgissante : echelle .96 -> 1 et fondu, puis fermeture. */
export function renderPop(node, t, open, close, from = .96) {
  const o = prog(t, open[0], open[1], E.out) * (1 - prog(t, close[0], close[1], E.in));
  style(node, { opacity: o, transform: `scale(${from + (1 - from) * o})`, visibility: o > 0.001 ? 'visible' : 'hidden' });
  return o;
}
