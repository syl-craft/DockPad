// Moteur de timeline deterministe : chaque clip expose render(t), qui pose l'etat EXACT de l'instant
// t sans rien retenir de l'image precedente. C'est ce qui permet au rendu de capturer image par image,
// dans n'importe quel ordre, et d'obtenir la meme video d'une machine a l'autre.

export const clamp = (v, a = 0, b = 1) => Math.max(a, Math.min(b, v));
export const lerp = (a, b, p) => a + (b - a) * p;

export const E = {
  linear: p => p,
  in: p => p * p * p,
  out: p => 1 - Math.pow(1 - p, 3),
  inOut: p => (p < .5 ? 4 * p * p * p : 1 - Math.pow(-2 * p + 2, 3) / 2),
  outBack: p => { const c = 1.5; return 1 + (c + 1) * Math.pow(p - 1, 3) + c * Math.pow(p - 1, 2); },
};

/** Avancement 0..1 entre a et b, avec easing. */
export const prog = (t, a, b, e = E.inOut) => e(clamp((t - a) / (b - a)));

/** Visible entre a et b, avec fondu de f secondes a l'entree et a la sortie. */
export const env = (t, a, b, f = .25) => Math.min(prog(t, a, a + f, E.out), 1 - prog(t, b - f, b, E.in));

/** Interpolation par images cles [[t, v], ...] ; v nombre ou tableau de nombres. */
export function track(t, keys, e = E.inOut) {
  if (t <= keys[0][0]) return keys[0][1];
  for (let i = 1; i < keys.length; i++) {
    const [t1, v1] = keys[i];
    if (t <= t1) {
      const [t0, v0] = keys[i - 1];
      const p = t1 === t0 ? 1 : e((t - t0) / (t1 - t0));
      return Array.isArray(v0) ? v0.map((x, j) => lerp(x, v1[j], p)) : lerp(v0, v1, p);
    }
  }
  return keys[keys.length - 1][1];
}

/** Enfoncement d'un clic en tc : 0 hors du clic, 1 au plus bas. */
export const press = (t, tc, d = .09) => 1 - clamp(Math.abs(t - tc) / d);

/** Vrai des que t a depasse tc. */
export const after = (t, tc) => t >= tc;

/** Index du dernier evenement passe dans une liste de temps, -1 si aucun. */
export const stepIndex = (t, times) => { let k = -1; times.forEach((x, i) => { if (t >= x) k = i; }); return k; };

export const $ = (sel, root = document) => root.querySelector(sel);

/** Fabrique un element depuis du HTML. */
export function el(html) {
  const tpl = document.createElement('template');
  tpl.innerHTML = html.trim();
  return tpl.content.firstElementChild;
}

export function style(node, props) {
  for (const [k, v] of Object.entries(props)) node.style[k] = v;
}

/**
 * Point d'entree d'un clip.
 * - ?theme=light|dark pose le theme ;
 * - ?render=1 laisse render.mjs piloter window.seek ; sinon la page se lit en boucle pour la mise au point
 *   (espace = pause, fleches = pas d'une image, clic sur la barre = deplacement).
 */
export async function clip({ duration, build, render }) {
  const params = new URLSearchParams(location.search);
  document.documentElement.dataset.theme = params.get('theme') || 'light';

  const ctx = await build();
  await Promise.all([...document.images].map(img => img.complete ? Promise.resolve() : img.decode().catch(() => {})));
  await document.fonts.ready;

  window.DURATION = duration;
  window.seek = t => render(clamp(t, 0, duration), ctx);
  window.seek(0);
  window.READY = true;

  if (params.get('render')) return;

  // Lecture de mise au point, a l'echelle de la fenetre du navigateur.
  const stage = document.getElementById('stage');
  const fit = () => { const k = Math.min(innerWidth / 1920, (innerHeight - 24) / 1080); stage.style.transform = `scale(${k})`; stage.style.transformOrigin = '0 0'; };
  fit(); addEventListener('resize', fit);
  const bar = el('<div style="position:fixed;left:0;right:0;bottom:0;height:24px;background:#111;cursor:pointer;font:12px Consolas;color:#aaa;z-index:999"><div style="position:absolute;top:0;bottom:0;left:0;background:#0078D4"></div><span style="position:absolute;right:8px;top:4px"></span></div>');
  document.body.appendChild(bar);
  let t = 0, playing = true, last = performance.now();
  bar.onclick = e => { t = e.clientX / innerWidth * duration; };
  addEventListener('keydown', e => {
    if (e.key === ' ') playing = !playing;
    if (e.key === 'ArrowRight') { playing = false; t = Math.min(duration, t + 1 / 60); }
    if (e.key === 'ArrowLeft') { playing = false; t = Math.max(0, t - 1 / 60); }
  });
  const loop = now => {
    if (playing) t = (t + (now - last) / 1000) % duration;
    last = now;
    window.seek(t);
    bar.firstChild.style.width = `${t / duration * 100}%`;
    bar.lastChild.textContent = `${t.toFixed(2)} / ${duration}s`;
    requestAnimationFrame(loop);
  };
  requestAnimationFrame(loop);
}
