/* =========================================================
   Little Threads — Home page logic
   - Scroll-driven 3D walk through the store (three.js)
   - Department cards, hotspots and progress bar
   - Navbar, How It Works reveal, counters, contact form
   ========================================================= */

const $ = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => [...r.querySelectorAll(s)];
const t = window.stmsT;
const f = window.stmsFormat;
const clamp = (x, a = 0, b = 1) => Math.min(b, Math.max(a, x));
const seg = (p, a, b) => clamp((p - a) / (b - a));
const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)').matches;
const isMobile = matchMedia('(max-width: 900px), (pointer: coarse)').matches;

/* ---------------- content ---------------- */
const DEPARTMENTS = [
  { name: 'Girls & Baby', age: '0 – 10 years', side: 'left',
    text: 'Smocked dresses, knit cardigans, bodysuits and everyday basics from certified makers of soft natural fabrics.',
    tags: ['Dresses', 'Bodysuits', 'Knitwear', 'Sleepwear'], link: '#',
    hotspots: ['Dresses', 'Bodysuits & sets', 'Folded basics'] },
  { name: 'Boys', age: '2 – 14 years', side: 'right',
    text: 'Denim, cargo, chinos, joggers and everyday tees — full size runs and bulk pricing.',
    tags: ['Denim', 'Cargo', 'Joggers', 'Tees'], link: '#',
    hotspots: ['Denim & cargo', 'Folded tees', 'Jackets & tees'] },
  { name: 'Accessories', age: 'All ages', side: 'left',
    text: 'Caps, sun hats, knit beanies, sneakers and backpacks — the finishing touches.',
    tags: ['Caps', 'Sun hats', 'Beanies', 'Sneakers', 'Backpacks'], link: '#',
    hotspots: ['Backpacks', 'Kids sneakers', 'Sun hats'] },
  { name: 'Supply Hub', label: 'Our Team', age: 'Behind the scenes', side: 'left', eyebrow: 'Our team',
    text: 'Our sourcing, supplier-success and order teams work here — verifying suppliers, managing catalogues and keeping every order on track.',
    tags: ['Supplier onboarding', 'Order management', 'Live tracking'], link: '#about', cta: 'Meet the team',
    hotspots: ['Order team', 'Supplier success', 'Live shipment map'] },
  { name: 'Quality & Dispatch', label: 'Dispatch', age: 'Checked · Packed · Shipped', side: 'left', eyebrow: 'Operations',
    text: 'Every order is quality-checked, carefully packed and dispatched from our warehouse with full tracking.',
    tags: ['Quality check', 'Packing', 'Warehouse', 'Shipping'], link: '#how-it-works', cta: 'See how it works',
    hotspots: ['Packing station', 'Warehouse', 'Ready to ship'] }
];

const STEPS = [
  { title: 'Register', text: 'Create your supplier account with your company details.', color: '#d9f7ec', icon: 'M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8Zm-7 9a7 7 0 0 1 14 0' },
  { title: 'Verification', text: 'Our team reviews your documents and approves your profile.', color: '#e4eaf3', icon: 'M9 12l2 2 4-4M12 3l7 3v6c0 4.5-3 8-7 9-4-1-7-4.5-7-9V6l7-3Z' },
  { title: 'List Products', text: 'Upload your catalogue with sizes, MOQs and pricing.', color: '#d9f7ec', icon: 'M4 7l8-4 8 4-8 4-8-4Zm0 0v10l8 4 8-4V7M12 11v10' },
  { title: 'Receive Orders', text: 'Retailers place bulk orders directly with you.', color: '#e4eaf3', icon: 'M3 4h2l2.5 11h11L21 7H7M10 20a1 1 0 1 0 0-.1M18 20a1 1 0 1 0 0-.1' },
  { title: 'Fulfil & Ship', text: 'Pack, dispatch and share live tracking in one place.', color: '#d9f7ec', icon: 'M3 6h11v10H3zM14 9h4l3 3v4h-7M7 19a2 2 0 1 0 0-.1M17 19a2 2 0 1 0 0-.1' },
  { title: 'Get Paid', text: 'Secure payouts with clear sales reports.', color: '#e4eaf3', icon: 'M3 7h18v10H3zM3 11h18M7 15h3' }
];

/* Walk timeline (scroll units). Must match the camera keyframes in store/scene.js */
const TOTAL = 17.5;                                                        // length of the walk
const STOPS = [[2.4, 3.3], [5.4, 6.3], [8.4, 9.3], [10.5, 11.4], [13.4, 14.3]];  // standing inside each area
const DEPT_SWITCH = [4.6, 7.6, 10.3, 13.0];                                 // passing through arches / doors
const SCREEN = [15.4, 16.6];                                                // standing in front of the video wall
const EXIT = [16.7, 17.5];                                                  // fade out → How It Works

/* ---------------- build UI ---------------- */
$('#departments').innerHTML = DEPARTMENTS.map((d, i) => `
  <article class="dept-card glass ${d.side}" id="dept${i}" aria-hidden="true">
    <div class="top"><div class="eyebrow">${d.eyebrow ? t(d.eyebrow) : f('Department {0} / 03', '0' + (i + 1))}</div><div class="num">0${i + 1}</div></div>
    <h2 class="serif">${t(d.name)}</h2>
    <div class="age">${t(d.age)}</div>
    <p>${t(d.text)}</p>
    <div>${d.tags.map(tag => `<span class="chip">${t(tag)}</span>`).join('')}</div>
    <a class="btn btn-dark" href="${d.link}">${d.cta ? t(d.cta) : f('Browse {0}', t(d.name))} →</a>
  </article>
  ${d.hotspots.map(h => `<div class="hotspot hs${i}"><i></i><span>${t(h)}</span></div>`).join('')}
`).join('');

$('#deptBar').innerHTML = DEPARTMENTS.map((d, i) =>
  `<div class="dept-step" id="ds${i}"><span class="n">0${i + 1}</span><span class="lbl">${t(d.label || d.name)}</span><span class="track"><b id="dsb${i}"></b></span></div>`).join('');

$('#steps').insertAdjacentHTML('beforeend', STEPS.map((s, k) => `
  <div class="step" style="transition-delay:${k * 120}ms">
    <div class="n">0${k + 1}</div>
    <div class="card">
      <div class="ico" style="background:${s.color}"><svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="#08766c" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><path d="${s.icon}"/></svg></div>
      <h3>${t(s.title)}</h3><p>${t(s.text)}</p>
    </div>
  </div>`).join(''));

$('#year').textContent = new Date().getFullYear();

/* ---------------- 3D store ---------------- */
let store = null, nearScreen = false;
const els = {
  hero: $('#hero'), heroFade: $('#heroFade'), veil: $('#veil'), vignette: $('#vignette'), gl: $('#gl'),
  bar: $('#deptBar'), video: $('#videoUI'), cards: DEPARTMENTS.map((_, i) => $('#dept' + i)),
  hot: DEPARTMENTS.map((_, i) => $$('.hs' + i)), steps: DEPARTMENTS.map((_, i) => $('#ds' + i)), stepBars: DEPARTMENTS.map((_, i) => $('#dsb' + i))
};

function update(p) {
  const moving = store ? store.render(p) : 0;

  // motion feel while walking
  els.vignette.style.opacity = (moving * .7).toFixed(3);
  els.veil.style.opacity = seg(p, EXIT[0], EXIT[1]).toFixed(3);

  // hero (outside)
  const h = 1 - seg(p, 0, .35);
  els.hero.style.opacity = h; els.heroFade.style.opacity = h;
  els.hero.style.transform = `translateY(${(1 - h) * -40}px)`;
  els.hero.style.pointerEvents = h > .5 ? 'auto' : 'none';

  // department cards + hotspots appear when you stop walking
  let active = 0; DEPT_SWITCH.forEach(v => { if (p >= v) active++; });
  STOPS.forEach(([s0, s1], i) => {
    const a = seg(p, s0 - .2, s0 + .05) * (1 - seg(p, s1 - .05, s1 + .2));
    const card = els.cards[i], dir = DEPARTMENTS[i].side === 'left' ? -40 : 40;
    card.style.opacity = a; card.style.transform = `translateX(${(1 - a) * dir}px)`;
    card.style.pointerEvents = a > .5 ? 'auto' : 'none'; card.setAttribute('aria-hidden', a > .5 ? 'false' : 'true');

    const anchors = a > 0 && store ? store.anchors(i) : null;
    els.hot[i].forEach((el, k) => {
      const b = clamp(a * 1.4 - k * .2);
      el.style.opacity = b; el.style.transform = `scale(${.8 + .2 * b})`;
      if (anchors) { el.style.left = `calc(${anchors[k][0]}% - 8px)`; el.style.top = `calc(${anchors[k][1]}% - 8px)`; }
    });

    els.steps[i].className = 'dept-step' + (i === active ? ' active' : i < active ? ' done' : '');
    els.stepBars[i].style.transform = `scaleX(${seg(p, s0 - 1.2, s1 + .8)})`;
  });
  els.bar.style.opacity = seg(p, 1.5, 1.9) * (1 - seg(p, SCREEN[0] - .8, SCREEN[0] - .4));

  // video wall: show the play button while standing in front of the screen
  const v = seg(p, SCREEN[0] - .25, SCREEN[0]) * (1 - seg(p, SCREEN[1], SCREEN[1] + .2));
  els.video.style.opacity = v; els.video.style.pointerEvents = v > .5 ? 'auto' : 'none';
  els.video.style.transform = `translate(-50%, ${(1 - v) * 20}px)`;
  nearScreen = v > .5;
  if (!nearScreen && store && store.screen.playing) { store.screen.pause(); syncVideoUI(); }
}

const track = $('#home');
function progress() {
  const max = track.offsetHeight - innerHeight;
  const y = clamp(-track.getBoundingClientRect().top / max);
  return y * TOTAL;
}
/* Smooth, frame-synced rendering:
   the scroll position only sets a target; every animation frame the camera eases
   toward it. Rendering stops completely when nothing moves.                     */
let target = 0, current = 0, dirty = true, running = false;
function onScroll() { target = progress(); updateNav(); kick(); }
function kick() { if (!running) { running = true; requestAnimationFrame(loop); } }
let slowFrames = 0, fastFrames = 0, dpr = 0;
function loop(t) {
  const diff = target - current;
  current = Math.abs(diff) < .0008 ? target : current + diff * (reducedMotion ? 1 : .14);
  const playing = store && store.screen.playing;              // keep drawing frames while the video plays
  const t0 = performance.now();
  if (dirty || diff !== 0 || playing) update(current);
  dirty = false;
  adaptQuality(performance.now() - t0);
  if (current !== target || playing) requestAnimationFrame(loop); else running = false;
}
/* adaptive resolution: if frames get slow, render at a lower pixel ratio */
function adaptQuality(ms) {
  if (!store) return;
  if (!dpr) dpr = store.maxPixelRatio;
  if (ms > 24) { slowFrames++; fastFrames = 0; } else if (ms < 9) { fastFrames++; slowFrames = 0; }
  if (slowFrames > 6 && dpr > .75) { dpr = Math.max(.75, dpr - .25); store.setPixelRatio(dpr); slowFrames = 0; }
  if (fastFrames > 90 && dpr < store.maxPixelRatio) { dpr = Math.min(store.maxPixelRatio, dpr + .25); store.setPixelRatio(dpr); fastFrames = 0; }
}

/* loader */
function setLoader(pct, text) { $('#loaderBar').style.width = pct + '%'; if (text) $('#loaderText').textContent = t(text); }
document.body.classList.add('loading');
setLoader(10, 'Opening the store…');

window.__LT_QUALITY = { mobile: isMobile, reducedMotion };
window.__LT_PROGRESS = setLoader;
import('./store/scene.js').then(async m => {
  store = m;
  store.screen.video.addEventListener('pause', syncVideoUI); store.screen.video.addEventListener('ended', syncVideoUI);
  await m.ready;
  const fit = () => { m.resize(innerWidth, innerHeight); dirty = true; kick(); };
  addEventListener('resize', fit); m.resize(innerWidth, innerHeight);
  setLoader(95, 'Almost there…');
  await m.warmUp();
  target = current = progress(); dirty = true; kick();
  setLoader(100, 'Welcome in');
  setTimeout(() => { $('#loader').classList.add('done'); document.body.classList.remove('loading'); }, 350);
}).catch(err => {
  console.error('3D store failed to load', err);
  $('#stage').style.background = 'linear-gradient(#CFE3F1,#FBF7F1)';
  $('#loader').classList.add('done'); document.body.classList.remove('loading');
});

addEventListener('scroll', onScroll, { passive: true });
update(0);

/* ---------------- video wall ---------------- */
const playBtn = $('#videoPlay'), muteBtn = $('#videoMute'), vNote = $('#videoNote');
function syncVideoUI() {
  const on = store && store.screen.playing;
  playBtn.innerHTML = `<span class="ico">${on ? "❚❚" : "▶"}</span> ${t(on ? "Pause" : "Play our story")}`;
  playBtn.setAttribute('aria-pressed', on ? 'true' : 'false');
  muteBtn.hidden = !on; muteBtn.textContent = t(store && store.screen.video.muted ? 'Unmute' : 'Mute');
}
async function toggleVideo() {
  if (!store) return;
  try { await store.screen.toggle(); vNote.textContent = ''; }
  catch (e) { vNote.textContent = t('Video coming soon'); setTimeout(() => (vNote.textContent = ''), 2500); }
  syncVideoUI(); dirty = true; kick();
}
playBtn.addEventListener('click', toggleVideo);
muteBtn.addEventListener('click', () => { store.screen.video.muted = !store.screen.video.muted; syncVideoUI(); });
// clicking the 3D screen itself also plays / pauses
$('#gl').addEventListener('click', e => { if (store && nearScreen && store.screen.hit(e.clientX, e.clientY)) toggleVideo(); });
$('#gl').addEventListener('pointermove', e => { if (!store) return; $('#gl').style.cursor = nearScreen && store.screen.hit(e.clientX, e.clientY) ? 'pointer' : ''; });

/* ---------------- navbar ---------------- */
const burger = $('#burger'), links = $('#navLinks');
burger.addEventListener('click', () => {
  const open = links.classList.toggle('open');
  burger.setAttribute('aria-expanded', open);
});
$$('#navLinks a').forEach(a => a.addEventListener('click', () => { links.classList.remove('open'); burger.setAttribute('aria-expanded', 'false'); }));

// "Home" jumps to the very start of the walk
$$('a[href="#home"]').forEach(a => a.addEventListener('click', e => { e.preventDefault(); scrollTo({ top: 0, behavior: reducedMotion ? 'auto' : 'smooth' }); }));

const sections = ['contact', 'about', 'how-it-works'].map(id => $('#' + id));
function updateNav() {
  let current = 'home';
  for (const s of sections) if (s.getBoundingClientRect().top < innerHeight * .45) { current = s.id; break; }
  $$('.nav-link').forEach(l => l.classList.toggle('active', l.dataset.section === current));
}

/* ---------------- reveal + counters ---------------- */
const io = new IntersectionObserver(entries => entries.forEach(e => {
  if (!e.isIntersecting) return;
  e.target.classList.add('in');
  $$('[data-count]', e.target).forEach(countUp);
  io.unobserve(e.target);
}), { threshold: .2 });
$$('.reveal, #steps').forEach(el => io.observe(el));

function countUp(el) {
  const to = +el.dataset.count, suf = el.dataset.suffix || '', t0 = performance.now(), dur = reducedMotion ? 0 : 1600;
  (function tick(t) {
    const k = dur ? clamp((t - t0) / dur) : 1, e = 1 - Math.pow(1 - k, 3);
    el.textContent = Math.round(to * e) + suf;
    if (k < 1) requestAnimationFrame(tick);
  })(t0);
}

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/* ---------------- newsletter ---------------- */
const news = $('#newsForm'), newsNote = $('#newsNote');
news?.addEventListener('submit', e => {
  e.preventDefault();
  const email = news.elements.email.value.trim();
  if (!EMAIL_RE.test(email)) { news.classList.add('invalid'); newsNote.textContent = t('Please enter a valid email.'); newsNote.className = 'ft-note err'; return; }
  news.classList.remove('invalid');
  // TODO: send `email` to your newsletter provider / backend
  news.reset(); newsNote.textContent = t('You’re subscribed — welcome aboard!'); newsNote.className = 'ft-note ok';
});

// initial state (e.g. when the page is reloaded halfway down)
onScroll();
