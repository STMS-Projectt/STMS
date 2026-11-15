import * as THREE from 'three';
import { RoomEnvironment } from 'three/addons/environments/RoomEnvironment.js';
import { RoundedBoxGeometry } from 'three/addons/geometries/RoundedBoxGeometry.js';
import { FontLoader } from 'three/addons/loaders/FontLoader.js';
import { TextGeometry } from 'three/addons/geometries/TextGeometry.js';
import * as R from './realism.js';
import * as OFFICE from './office.js';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';

/*  Little Threads — 3D walk-through store
    Builds the street, the storefront and three departments, and exposes
    render(p) where p is the scroll progress along the walk (0 → 11).          */
const Q = window.__LT_QUALITY || { mobile: false, reducedMotion: false };
const canvas = document.getElementById('gl');
const renderer = new THREE.WebGLRenderer({ canvas, antialias: !Q.mobile, powerPreference: 'high-performance' });
const MAX_DPR = Math.min(devicePixelRatio, Q.mobile ? 1.25 : 1.5);
renderer.setPixelRatio(MAX_DPR);
const progress = (pct, text) => window.__LT_PROGRESS && window.__LT_PROGRESS(pct, text);
const tick = () => new Promise(r => setTimeout(r, 0));   // let the page breathe while building
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure = .82;
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFSoftShadowMap;
renderer.shadowMap.autoUpdate = false;   // the store is static: shadows are baked once
const scene = new THREE.Scene();
const pmrem = new THREE.PMREMGenerator(renderer);
scene.environment = pmrem.fromScene(new RoomEnvironment(), 0.04).texture;
const camera = new THREE.PerspectiveCamera(55, 16 / 9, 0.1, 400);

/* ---------- helpers ---------- */
const _mats = {};
const M = (color, o = {}) => { const k = color + JSON.stringify(o, (key, v) => (v && v.isTexture) ? v.uuid : v);
  return _mats[k] || (_mats[k] = new THREE.MeshStandardMaterial({ color, roughness: .85, metalness: 0, ...o })); };
function box(w, h, d, mat, x, y, z, parent = scene, cast = true) {
  const m = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), mat); m.position.set(x, y, z);
  m.castShadow = cast; m.receiveShadow = true; parent.add(m); return m;
}
function rbox(w, h, d, r, mat, x, y, z, parent = scene) {
  const m = new THREE.Mesh(new RoundedBoxGeometry(w, h, d, 3, r), mat); m.position.set(x, y, z);
  m.castShadow = true; m.receiveShadow = true; parent.add(m); return m;
}
function canvasTex(w, h, draw, rep) {
  const c = document.createElement('canvas'); c.width = w; c.height = h; draw(c.getContext('2d'), w, h);
  const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace; t.anisotropy = 8;
  if (rep) { t.wrapS = t.wrapT = THREE.RepeatWrapping; t.repeat.set(...rep); } return t;
}
function roundedRectShape(w, h, r) { // centered bottom at y=0
  const s = new THREE.Shape(), x = -w / 2;
  s.moveTo(x, 0); s.lineTo(x, h - r); s.quadraticCurveTo(x, h, x + r, h); s.lineTo(x + w - r, h);
  s.quadraticCurveTo(x + w, h, x + w, h - r); s.lineTo(x + w, 0); s.lineTo(x, 0); return s;
}
function archPath(w, h) { // pill-top opening
  const p = new THREE.Path(), r = w / 2;
  p.moveTo(-r, 0); p.lineTo(-r, h - r); p.absarc(0, h - r, r, Math.PI, 0, true); p.lineTo(r, 0); p.lineTo(-r, 0); return p;
}

/* ---------- materials ---------- */
const oak = canvasTex(1024, 1024, (g, w, h) => {
  g.fillStyle = '#B88F62'; g.fillRect(0, 0, w, h);
  const rows = 8, rh = h / rows;
  for (let r = 0; r < rows; r++) { const y = r * rh; let x = Math.random() * w; const end = x + w;
    while (x < end) { const L = Math.min(380 + Math.random() * 300, end - x); const l = 58 + Math.random() * 12;
      for (const o of [0, -w]) { const xx = x + o;
        g.fillStyle = `hsl(32,${36 + Math.random() * 4}%,${l - 6}%)`; g.fillRect(xx, y, L, rh);
        g.fillStyle = 'rgba(80,55,30,.3)'; g.fillRect(xx, y, 2, rh); }
      for (let k = 0; k < 14; k++) { const yy = y + Math.random() * rh; const a = .04 + Math.random() * .05, lw = 1 + Math.random() * 2;
        for (const o of [0, -w]) { g.strokeStyle = `rgba(120,80,40,${a})`; g.lineWidth = lw; g.beginPath(); g.moveTo(x + o, yy); g.bezierCurveTo(x + o + L * .3, yy + 3, x + o + L * .6, yy - 3, x + o + L, yy + 1); g.stroke(); } }
      x += L; }
    g.fillStyle = 'rgba(80,55,30,.3)'; g.fillRect(0, y, w, 2); }
}, [3, 18]);
const concrete = canvasTex(512, 512, (g, w, h) => { g.fillStyle = '#AFABA4'; g.fillRect(0, 0, w, h);
  for (let i = 0; i < 9000; i++) { g.fillStyle = `rgba(${Math.random() < .5 ? '255,255,255' : '60,60,60'},${Math.random() * .06})`; g.fillRect(Math.random() * w, Math.random() * h, 2, 2); }
  g.strokeStyle = 'rgba(90,90,90,.35)'; g.lineWidth = 2; g.strokeRect(0, 0, w, h); }, [20, 20]);
const asphalt = canvasTex(512, 512, (g, w, h) => { g.fillStyle = '#5E6166'; g.fillRect(0, 0, w, h);
  for (let i = 0; i < 12000; i++) { g.fillStyle = `rgba(${Math.random() < .5 ? '255,255,255' : '0,0,0'},${Math.random() * .08})`; g.fillRect(Math.random() * w, Math.random() * h, 2, 2); } }, [16, 8]);

const facadeMat = M('#F2EFEA', { roughness: .9 });
const darkMetal = M('#23262B', { roughness: .4, metalness: .6 });
const brass = M('#C7A26B', { roughness: .3, metalness: 1 });
const chrome = M('#DADDE2', { roughness: .18, metalness: 1 });
const white = M('#F7F5F2', { roughness: .7 });
const glass = new THREE.MeshPhysicalMaterial({ color: '#dfe9ee', roughness: .03, metalness: 0, transparent: true, opacity: .16, envMapIntensity: 1.6, side: THREE.DoubleSide, depthWrite: false });
const led = new THREE.MeshBasicMaterial({ color: '#FFF4E2' });

/* ---------- sky ---------- */
scene.background = canvasTex(16, 512, (g, w, h) => { const gr = g.createLinearGradient(0, 0, 0, h); gr.addColorStop(0, '#7FB2DC'); gr.addColorStop(.55, '#CFE3F1'); gr.addColorStop(1, '#F2EEE6'); g.fillStyle = gr; g.fillRect(0, 0, w, h); });
scene.fog = new THREE.Fog('#E4EBEF', 40, 120);

/* ---------- lights ---------- */
scene.add(new THREE.HemisphereLight('#EAF2FA', '#9C8C78', .35));
const sun = new THREE.DirectionalLight('#FFF2DD', 2.0); sun.position.set(-14, 22, 26); sun.castShadow = true;
sun.shadow.mapSize.set(Q.mobile ? 1024 : 2048, Q.mobile ? 1024 : 2048); Object.assign(sun.shadow.camera, { left: -30, right: 30, top: 78, bottom: -30, near: 1, far: 200 });
sun.shadow.bias = -.0004; sun.shadow.normalBias = .02; scene.add(sun); sun.target.position.set(0, 0, 4); scene.add(sun.target);

/* ---------- street ---------- */
const ground = new THREE.Mesh(new THREE.PlaneGeometry(120, 30), M('#ffffff', { map: concrete, roughness: .95 }));
ground.rotation.x = -Math.PI / 2; ground.position.set(0, 0, 13); ground.receiveShadow = true; scene.add(ground);
const road = new THREE.Mesh(new THREE.PlaneGeometry(160, 40), M('#ffffff', { map: asphalt, roughness: .95 }));
road.rotation.x = -Math.PI / 2; road.position.set(0, -.12, 48); road.receiveShadow = true; scene.add(road);
box(160, .16, .3, M('#BDB9B2'), 0, -.04, 28.1);
for (let i = -8; i <= 8; i++) box(3, .01, .15, M('#EDEBE6'), i * 8, -.11, 40, scene, false);

/* neighbouring buildings */
[[-13.6, 11, 9, '#E2DDD4'], [-25.4, 13, 8, '#C9CDD2'], [13.6, 10, 9, '#D9D2C7'], [25.4, 14, 9, '#BFC4CA']].forEach(([x, h, d, c]) => {
  const b = box(11.6, h, d, M(c, { roughness: .9 }), x, h / 2, -d / 2 + .2);
  for (let r = 0; r < Math.floor(h / 3.2); r++) for (let k = -2; k <= 2; k++) {
    if (r == 0) continue; box(1.4, 1.8, .05, M('#6E7F8E', { roughness: .1, metalness: .3 }), x + k * 2.1, 1.6 + r * 3.2, .25, scene, false); }
  box(9, 3, .05, M('#55636F', { roughness: .1, metalness: .3 }), x, 1.8, .23, scene, false);
});

/* planters with topiary */
function planter(x, z) {
  rbox(1.1, .7, 1.1, .05, M('#3B3E43', { roughness: .6 }), x, .35, z);
  box(1.0, .04, 1.0, M('#3E2F24', { roughness: 1 }), x, .69, z);
  R.boxwoodBall(scene, x, 1.25, z, .58); R.boxwoodBall(scene, x, 2.02, z, .38);
}
planter(-5.6, 2.4); planter(5.6, 2.4); planter(-3.2, 2.4); planter(3.2, 2.4);
/* street trees */
function tree(x, z) {
  const t = new THREE.Mesh(new THREE.CylinderGeometry(.12, .16, 4, 12), M('#6B5847')); t.position.set(x, 2, z); t.castShadow = true; scene.add(t);
  const crown = new THREE.Group(); [[0, 4.6, 0, 1.7], [.9, 4.2, .3, 1.2], [-.9, 4.3, -.2, 1.3], [0, 5.5, .2, 1.1]].forEach(([a, b, c, r]) => {
    const s = new THREE.Mesh(new THREE.IcosahedronGeometry(r, 2), M('#557A4E', { roughness: 1 })); s.position.set(a, b, c); s.castShadow = true; crown.add(s); });
  crown.position.set(x, 0, z); scene.add(crown);
}
R.realTree(scene, -8.4, 5.2, 6.4); R.realTree(scene, 8.6, 5.6, 6.0);
[[-8.4, 5.2], [8.6, 5.6]].forEach(([x, z]) => box(1.6, .04, 1.6, M('#3B3E43', { roughness: .7 }), x, .02, z));
box(.12, 4, .12, darkMetal, -15, 2, 9); box(.7, .1, .7, darkMetal, -15, 4, 9);

/* ---------- BUILDING ---------- */
const W = 15, H = 7.2, D0 = 0;
// facade with openings
const fShape = new THREE.Shape(); fShape.moveTo(-W / 2, 0); fShape.lineTo(W / 2, 0); fShape.lineTo(W / 2, H); fShape.lineTo(-W / 2, H); fShape.lineTo(-W / 2, 0);
const shopGlass = new THREE.Path(); shopGlass.moveTo(-6.5, .5); shopGlass.lineTo(6.5, .5); shopGlass.lineTo(6.5, 4.2); shopGlass.lineTo(-6.5, 4.2); shopGlass.lineTo(-6.5, .5);
fShape.holes.push(shopGlass);
const doorHole = new THREE.Path(); doorHole.moveTo(-1.3, 0); doorHole.lineTo(1.3, 0); doorHole.lineTo(1.3, .5); doorHole.lineTo(-1.3, .5); doorHole.lineTo(-1.3, 0);
const facadeGeo = new THREE.ExtrudeGeometry(fShape, { depth: .5, bevelEnabled: false });
const facade = new THREE.Mesh(facadeGeo, facadeMat); facade.position.z = -.5; facade.castShadow = facade.receiveShadow = true; scene.add(facade);
// vertical wood slat band (upper facade)
const slat = M('#B58A5E', { roughness: .7 });
for (let i = 0; i < 46; i++) box(.12, 2.1, .14, slat, -6.6 + i * .293, 5.75, .07);
box(13.6, .1, .2, darkMetal, 0, 4.66, .05); box(13.6, .1, .2, darkMetal, 0, 6.85, .05);
// canopy
box(15.6, .18, 1.6, M('#2A2D32', { roughness: .5 }), 0, 4.45, .75);
box(15.6, .02, 1.5, M('#FFF0D8', { emissive: '#FFE3BC', emissiveIntensity: .25 }), 0, 4.355, .75, scene, false);
// window mullions
for (const x of [-6.5, -4.2, -1.3, 1.3, 4.2, 6.5]) box(.08, 3.7, .12, darkMetal, x, 2.35, .02);
box(13.1, .08, .12, darkMetal, 0, 4.2, .02); box(5.2, .08, .12, darkMetal, -3.9, .5, .02); box(5.2, .08, .12, darkMetal, 3.9, .5, .02);
[[-3.9, 5.2], [3.9, 5.2]].forEach(([x, w]) => { const g = new THREE.Mesh(new THREE.PlaneGeometry(w, 3.7), glass); g.position.set(x, 2.35, 0); scene.add(g); });
// open sliding doors (pushed aside)
[[-1.95], [1.95]].forEach(([x]) => { const g = new THREE.Mesh(new THREE.PlaneGeometry(1.3, 3.7), glass); g.position.set(x, 2.35, .08); scene.add(g); box(1.3, .06, .06, darkMetal, x, .52, .1); });
// window podiums & display (seen through glass)
function podium(x, z, h, r) { const m = new THREE.Mesh(new THREE.CylinderGeometry(r, r, h, 48), white); m.position.set(x, h / 2, z); m.castShadow = m.receiveShadow = true; scene.add(m); }
podium(-4.6, -1.2, .45, .7); podium(-2.6, -1.4, .25, .55); podium(4.6, -1.2, .45, .7); podium(2.6, -1.4, .25, .55);

/* sign */
const signGroup = new THREE.Group(); scene.add(signGroup);
let fontDone; export const ready = new Promise(res => fontDone = res);
new FontLoader().load('https://cdn.jsdelivr.net/npm/three@0.160.0/examples/fonts/helvetiker_bold.typeface.json', f => {
  const tg = new TextGeometry('LITTLE THREADS', { font: f, size: .52, height: .06, curveSegments: 6 }); tg.computeBoundingBox(); tg.translate(-(tg.boundingBox.max.x) / 2, 0, 0);
  const t = new THREE.Mesh(tg, M('#FFFFFF', { emissive: '#FFF6E8', emissiveIntensity: .35, roughness: .3 })); t.position.set(0, 5.55, .22); t.scale.x = 1.12; t.castShadow = true; signGroup.add(t);
  const t2g = new TextGeometry('K I D S W E A R   S U P P L Y', { font: f, size: .15, height: .02 }); t2g.computeBoundingBox(); t2g.translate(-(t2g.boundingBox.max.x) / 2, 0, 0);
  const t2 = new THREE.Mesh(t2g, M('#d9f7ec', { emissive: '#d9f7ec', emissiveIntensity: .25, roughness: .4 })); t2.position.set(0, 5.18, .22); signGroup.add(t2);
  box(7.0, 1.25, .08, M('#065b53', { roughness: .55 }), 0, 5.72, .16, signGroup);
  fontDone();
}, undefined, () => fontDone());

await tick(); progress(35, 'Building the store…');
/* ---------- INTERIOR ---------- */
// soft "spotlight" glow baked into a texture, used on the lit wall bays
const POOL = canvasTex(256, 256, (g, w, h) => { g.fillStyle = '#b8b8b8'; g.fillRect(0, 0, w, h);
  const gr = g.createRadialGradient(w / 2, h * .12, 10, w / 2, h * .3, h * .85); gr.addColorStop(0, '#ffffff'); gr.addColorStop(.55, '#e2e2e2'); gr.addColorStop(1, '#b8b8b8');
  g.fillStyle = gr; g.fillRect(0, 0, w, h); });
const L = 72, IW = 14, IH = 4.4;
const floor = new THREE.Mesh(new THREE.PlaneGeometry(IW, L), M('#ffffff', { map: oak, roughness: .55 }));
floor.rotation.x = -Math.PI / 2; floor.position.set(0, .01, -L / 2); floor.receiveShadow = true; scene.add(floor);
const ceil = new THREE.Mesh(new THREE.PlaneGeometry(IW, L), M('#F4F1EC', { emissive: '#F4EEE6', emissiveIntensity: .35 })); ceil.rotation.x = Math.PI / 2; ceil.position.set(0, IH, -L / 2); scene.add(ceil);
// side walls & back
const wallMat = M('#F4F1EC', { roughness: .95 });
box(.3, IH, L, wallMat, -IW / 2 - .15, IH / 2, -L / 2, scene, false); box(.3, IH, L, wallMat, IW / 2 + .15, IH / 2, -L / 2, scene, false);
// building shell above/sides (so exterior reads as a solid volume)
box(.5, H, L, facadeMat, -W / 2 + .25, H / 2, -L / 2 - .5); box(.5, H, L, facadeMat, W / 2 - .25, H / 2, -L / 2 - .5);
box(W, .4, L, facadeMat, 0, H - .2, -L / 2 - .5);
// ceiling LED lines
for (const x of [-3.5, 0, 3.5]) box(.08, .03, L - 2, led, x, IH - .02, -L / 2, scene, false);
// interior lighting
// only a few real lights inside (one per department); the rest is baked into textures + environment light
for (const z of [-6, -20, -34, -48, -63]) { const p = new THREE.PointLight('#FFE9CF', 26, 16, 1.4); p.position.set(0, IH - .3, z); scene.add(p); }
const indoorFill = new THREE.HemisphereLight('#FFF6EC', '#C9A57B', .55); scene.add(indoorFill);

const zones = [
  { z0: 0, z1: -14, accent: '#EBD3CC', accent2: '#F6E7E2', name: 'GIRLS & BABY', cloth: ['#F2C6C2', '#F7E3D5', '#E9B9C6', '#FFFFFF', '#D9E4EE', '#F4D9A8'], kinds: ['dress', 'onesie', 'cardigan', 'dress', 'tee'] },
  { z0: -14, z1: -28, accent: '#cdeadf', accent2: '#e3f4ed', name: 'BOYS', cloth: ['#4D6E91', '#9DB4C9', '#7F9A79', '#E9E4D8', '#2E3E57', '#C9B28C'], kinds: ['tee', 'pants', 'jacket', 'pants', 'tee'] },
  { z0: -28, z1: -42, accent: '#dbe3ee', accent2: '#ebf0f6', name: 'ACCESSORIES', cloth: ['#E8C9A8', '#BFD0DE', '#F2D4CF', '#8FA88A', '#F5F0E6', '#D7B98A'], kinds: ['hat', 'cap', 'beanie', 'shoe'] }];

const PR = { dress: ['floral', 'plain', 'gingham', 'dots'], onesie: ['plain', 'stripe', 'dots'], cardigan: ['plain'], tee: ['plain', 'stripe', 'plain'], jeans: ['plain'], pants: ['plain'], jacket: ['plain'] };
let gi = 0;
function garment(kind, color, parent, x, y, z, rotY, scale = .5) {
  if (kind == 'pants' && /^#[2-5]/.test(color)) kind = 'jeans';
  const pr = PR[kind] || ['plain']; const print = pr[(gi++) % pr.length];
  const col = kind == 'jeans' ? ['#4C6A8F', '#3B557A', '#6E8DB0'][gi % 3] : color;
  return R.garment(kind, col, print, parent, x, y, z, rotY, scale * 1.08);
}
function stack(x, y, z, colors, parent = scene, w = .42, d = .32) {
  colors.forEach((c, i) => { const m = rbox(w, .065, d, .022, R.foldedMaterial(c, ['plain', 'stripe', 'plain', 'gingham'][(i + Math.round(x * 3)) & 3]), x + (Math.random() - .5) * .02, y + .035 + i * .07, z, parent); m.rotation.y = (Math.random() - .5) * .06; });
}

function acc(kind, color, x, y, z, parent = scene, ry = 0, sc = 1.25) {
  const c2 = ['#F2C6C2', '#BFD0DE', '#E8C9A8', '#9DB08F', '#F5F0E6'][Math.floor(Math.abs(x * 7 + z * 3)) % 5];
  const g = kind == 'cap' ? R.cap(color) : kind == 'beanie' ? R.beanie(color) : kind == 'sunhat' ? R.sunHat(['#D9BE8C', '#E6D2A8', '#CBB088'][Math.floor(Math.abs(x * 5)) % 3], color) : kind == 'sneakers' ? R.sneakers(color, '#FFFFFF') : R.backpack(color, c2);
  g.position.set(x, y, z); g.rotation.y = ry + (Math.random() - .5) * .25; g.scale.setScalar(sc); parent.add(g); return g;
}
for (let zi = 0; zi < zones.length; zi++) { const Z = zones[zi];
  progress(40 + zi * 15, ['Hanging the dresses…', 'Folding the denim…', 'Arranging the accessories…'][zi]); await tick();
  const len = Z.z0 - Z.z1;
  // accent wall bays both sides
  for (const side of [-1, 1]) {
    const wx = side * (IW / 2 - .02);
    const nb = 3;
    for (let b = 0; b < nb; b++) {
      const zc = Z.z0 - 2.6 - b * 3.9; if (zc < Z.z1 + 1.4) continue;
      const g = new THREE.Group(); g.position.set(wx, 0, zc); g.rotation.y = -side * Math.PI / 2; scene.add(g);
      // niche back panel (accent) + frame
      box(3.3, 3.1, .05, M(Z.accent, { roughness: .9, map: POOL, emissive: Z.accent, emissiveMap: POOL, emissiveIntensity: .35 }), 0, 1.85, .03, g, false);
      box(3.5, .08, .35, white, 0, 3.44, .17, g); box(.08, 3.18, .35, white, -1.71, 1.85, .17, g); box(.08, 3.18, .35, white, 1.71, 1.85, .17, g);
      box(3.3, .03, .03, led, 0, 3.38, .32, g, false);
      // base cabinet
      rbox(3.3, .5, .45, .02, white, 0, .25, .25, g);
      if (zi < 2) {
        // upper face-out rail
        box(3.0, .025, .025, chrome, 0, 2.62, .32, g); box(.025, .025, .3, chrome, -1.45, 2.62, .17, g); box(.025, .025, .3, chrome, 1.45, 2.62, .17, g);
        for (let k = 0; k < 6; k++) { const kind = Z.kinds[(k + b + (side > 0 ? 2 : 0)) % Z.kinds.length]; garment(kind, Z.cloth[(k * 2 + b * 3 + side + 6) % 6], g, -1.25 + k * .5, 2.6, .3 - k * .01, 0, .52); }
        // lower side-hung rail, densely packed
        box(3.0, .025, .025, chrome, 0, 1.58, .36, g); box(.025, .025, .34, chrome, -1.45, 1.58, .19, g); box(.025, .025, .34, chrome, 1.45, 1.58, .19, g);
        for (let k = 0; k < 15; k++) { const kind = Z.kinds[(k * 3 + b) % Z.kinds.length]; garment(kind, Z.cloth[(k * 5 + b + (side > 0 ? 3 : 0)) % 6], g, -1.32 + k * .19, 1.56, .36, Math.PI / 2 + (Math.random() - .5) * .25, .46); }
        // top shelf + cabinet stacks
        box(3.0, .03, .3, white, 0, 3.08, .17, g);
        for (let k = 0; k < 4; k++) stack(-1.1 + k * .73, 3.095, .18, [Z.cloth[k], Z.cloth[k], Z.cloth[(k + 2) % 6], Z.cloth[k]], g, .34, .24);
        for (let k = 0; k < 4; k++) stack(-1.1 + k * .73, .5, .25, [Z.cloth[(k + 1) % 6], Z.cloth[(k + 1) % 6], Z.cloth[(k + 4) % 6], Z.cloth[(k + 1) % 6], Z.cloth[(k + 1) % 6]], g, .38, .3);
      } else { // accessories: dense lit shelves
        const types = ['sneakers', 'backpack', 'beanie', 'sunhat', 'cap'];
        for (let s = 0; s < 5; s++) { const y = .78 + s * .54; box(3.0, .03, .34, white, 0, y, .19, g); box(3.0, .012, .012, led, 0, y - .02, .35, g, false);
          const ty = types[(s + b * 2 + (side > 0 ? 1 : 0)) % 5]; const n = ty == 'sunhat' ? 5 : ty == 'backpack' ? 6 : 7;
          for (let k = 0; k < n; k++) acc(ty, Z.cloth[(k + s + b) % 6], -1.3 + k * (2.6 / (n - 1)), y + .016, .2, g, 0, ty == 'backpack' ? 1.1 : 1.3); }
      }
    }
  }
  // central tables
  const tz = Z.z0 - len / 2 + .5;
  for (const tx of [-1.6, 1.6]) {
    const t = rbox(2.2, .08, 1.0, .02, M('#E9E3DA', { roughness: .5 }), tx, .78, tz); 
    rbox(2.0, .74, .8, .02, M(Z.accent2, { roughness: .9 }), tx, .37, tz);
    if (zi < 2) { for (let row = 0; row < 2; row++) for (let k = 0; k < 4; k++) { const c = Z.cloth[(k + row * 3) % 6]; stack(tx - .78 + k * .52, .82, tz - .22 + row * .44, [c, c, Z.cloth[(k + 2) % 6], c].slice(0, 3 + ((k + row) % 2)), scene, .4, .3); } }
    else { const ts = ['sunhat', 'beanie', 'cap', 'sneakers']; for (let row = 0; row < 2; row++) for (let k = 0; k < 4; k++) acc(ts[(k + row * 2) % 4], Z.cloth[(k + row) % 6], tx - .75 + k * .5, .82, tz - .2 + row * .4, scene, 0, 1.35); }
  }
  // round low podium with plant
  const pod = new THREE.Mesh(new THREE.CylinderGeometry(.55, .55, .35, 48), white); pod.position.set(-5, .175, Z.z1 + 1.4); pod.castShadow = pod.receiveShadow = true; scene.add(pod);
  const pot = new THREE.Mesh(new THREE.CylinderGeometry(.24, .18, .42, 32), M('#D9D2C7', { roughness: .9 })); pot.position.set(-5, .55, Z.z1 + 1.4); scene.add(pot);
  R.oliveTree(scene, -5, .72, Z.z1 + 1.4, 1.5);
  // freestanding garment racks (side-hung) near the aisle
  if (zi < 2) for (const rx of [-4.1, 4.1]) { const rz = tz - 3.2;
    box(.04, 1.5, .04, chrome, rx, .75, rz + 1.1, scene); box(.04, 1.5, .04, chrome, rx, .75, rz - 1.1, scene); box(.03, .03, 2.3, chrome, rx, 1.5, rz, scene);
    box(.5, .03, .5, chrome, rx, .015, rz + 1.1, scene); box(.5, .03, .5, chrome, rx, .015, rz - 1.1, scene);
    for (let k = 0; k < 15; k++) garment(Z.kinds[(k * 2 + (rx > 0 ? 1 : 0)) % Z.kinds.length], Z.cloth[(k * 5 + (rx > 0 ? 1 : 0)) % 6], scene, rx, 1.48, rz - 1.05 + k * .15, Math.PI / 2 + (Math.random() - .5) * .25, .48); }
  else for (const rx of [-4.1, 4.1]) { // accessory towers
    const rz = tz - 3.2; rbox(.9, 1.6, .9, .03, white, rx, .8, rz);
    for (let s2 = 0; s2 < 2; s2++) for (let k = 0; k < 2; k++) acc(['backpack', 'beanie'][(s2 + k) % 2], Z.cloth[(s2 + k * 3) % 6], rx - .2 + k * .4, 1.6, rz - .2 + s2 * .4, scene, 0, 1.2); }
  // pendant lights over tables
  for (const tx of [-1.6, 1.6]) { const cord = box(.01, 1.6, .01, darkMetal, tx, IH - .8, tz, scene, false);
    const sh = new THREE.Mesh(new THREE.SphereGeometry(.28, 32, 16, 0, Math.PI * 2, 0, Math.PI / 2), M('#F5EFE6', { side: THREE.DoubleSide, roughness: .6 })); sh.position.set(tx, IH - 1.6, tz); scene.add(sh);
    const bulb = new THREE.Mesh(new THREE.SphereGeometry(.08, 16, 8), led); bulb.position.set(tx, IH - 1.62, tz); scene.add(bulb);
  }
  // wall label
  const lt = canvasTex(1024, 128, (g, w, h) => { g.fillStyle = '#111e33'; g.font = '600 64px Inter, Helvetica, Arial'; g.textAlign = 'center'; g.textBaseline = 'middle'; g.letterSpacing = '18px'; g.fillText(Z.name, w / 2, h / 2); });
  const label = new THREE.Mesh(new THREE.PlaneGeometry(4, .5), new THREE.MeshBasicMaterial({ map: lt, transparent: true })); 
  // portal wall at end of zone (not for last zone)
  if (zi < 2) {
    const ws = new THREE.Shape(); ws.moveTo(-IW / 2, 0); ws.lineTo(IW / 2, 0); ws.lineTo(IW / 2, IH); ws.lineTo(-IW / 2, IH); ws.lineTo(-IW / 2, 0);
    ws.holes.push(archPath(3.4, 3.6));
    const wg = new THREE.ExtrudeGeometry(ws, { depth: .4, bevelEnabled: false, curveSegments: 48 });
    const wall = new THREE.Mesh(wg, M(zones[zi + 1].accent, { roughness: .95 })); wall.position.z = Z.z1 - .2; wall.receiveShadow = true; scene.add(wall);
    // LED outline around opening
    const pts = []; for (let a = 0; a <= 64; a++) { const t = Math.PI - a / 64 * Math.PI; pts.push(new THREE.Vector3(Math.cos(t) * 1.75, 1.95 + Math.sin(t) * 1.75, 0)); }
    const curve = new THREE.CatmullRomCurve3([new THREE.Vector3(-1.75, 0, 0), ...pts, new THREE.Vector3(1.75, 0, 0)]);
    const tube = new THREE.Mesh(new THREE.TubeGeometry(curve, 120, .025, 8), led); tube.position.z = Z.z1 + .22; scene.add(tube);
    label.position.set(0, 3.95, Z.z1 + .22); scene.add(label);
  }
}
// back wall: bright window-light wall
OFFICE.build({ THREE, R, scene, IW, IH, L, M, box, rbox, canvasTex });

/* ---------- performance: merge static meshes ----------
   Thousands of small meshes (garments, hangers, shelves…) are merged into a few
   large ones per material and per 7 m slice of the store, so the GPU draws
   ~200 batches instead of ~5000 objects.                                       */
progress(88, 'Turning on the lights…'); await tick();
function batchStatic(root) {
  root.updateMatrixWorld(true);
  const groups = new Map(), victims = [];
  root.traverse(o => {
    if (!o.isMesh || o.isInstancedMesh || o.isSkinnedMesh || o.customDepthMaterial || Array.isArray(o.material)) return;
    if (o.material.transparent || o.userData.keep) return;
    o.getWorldPosition(_v);
    const key = o.material.uuid + '|' + o.castShadow + '|' + Math.floor(_v.z / 7);
    if (!groups.has(key)) groups.set(key, { mat: o.material, cast: o.castShadow, list: [] });
    groups.get(key).list.push(o); victims.push(o);
  });
  for (const { mat, cast, list } of groups.values()) {
    if (list.length < 2) { const i = victims.indexOf(list[0]); victims.splice(i, 1); continue; }
    const geos = list.map(o => {
      let g = o.geometry.index ? o.geometry.toNonIndexed() : o.geometry.clone();
      for (const k of Object.keys(g.attributes)) if (!['position', 'normal', 'uv'].includes(k)) g.deleteAttribute(k);
      if (!g.attributes.normal) g.computeVertexNormals();
      if (!g.attributes.uv) g.setAttribute('uv', new THREE.BufferAttribute(new Float32Array(g.attributes.position.count * 2), 2));
      g.applyMatrix4(o.matrixWorld); return g;
    });
    const merged = mergeGeometries(geos, false); geos.forEach(g => g.dispose());
    if (!merged) continue;
    const m = new THREE.Mesh(merged, mat); m.castShadow = cast; m.receiveShadow = true; m.matrixAutoUpdate = false; scene.add(m);
  }
  for (const o of victims) o.parent && o.parent.remove(o);
  root.traverse(o => { o.matrixAutoUpdate = false; o.updateMatrix(); });
}
const _v = new THREE.Vector3();
batchStatic(scene);
renderer.shadowMap.needsUpdate = true;

/* ---------- camera path ----------
   Keyframes [p, z, y, x]: p is the scroll position (0 → TOTAL). Equal z on two
   consecutive keyframes = standing still (a "stop").                          */
export const TOTAL = 17.5;
const K = [[0, 15.5, 1.7, 0], [1.6, 1.2, 1.65, 0],
  [2.4, -3.8, 1.62, 0], [3.3, -3.8, 1.62, 0],          // stop 1 · Girls & Baby
  [4.6, -15.5, 1.62, 0], [5.4, -17.8, 1.62, 0], [6.3, -17.8, 1.62, 0],   // stop 2 · Boys
  [7.6, -29.5, 1.62, 0], [8.4, -31.8, 1.62, 0], [9.3, -31.8, 1.62, 0],   // stop 3 · Accessories
  [10.5, -43.6, 1.64, 0], [11.4, -43.6, 1.64, 0],      // stop 4 · Supply Hub (offices)
  [12.6, -56.2, 1.64, -1.2], [13.4, -57.8, 1.64, 0], [14.3, -57.8, 1.64, 0],   // stop 5 · Quality & Dispatch
  [15.4, -65.2, 1.66, 0], [16.6, -65.2, 1.66, 0],      // stop 6 · video screen
  [17.5, -67.6, 1.7, 0]];
// stops: [from, to, head turn, side step, zoom (fov degrees), look up]
export const STOPS = [[2.4, 3.3, .9, 2.3, 5, 0], [5.4, 6.3, -.9, -2.3, 5, 0], [8.4, 9.3, .9, 2.3, 5, 0],
  [10.5, 11.4, .75, 0, 4, 0], [13.4, 14.3, .6, 0, 4, 0], [15.4, 16.6, 0, 0, 13, .5]];
const ease = t => t < .5 ? 2 * t * t : 1 - Math.pow(-2 * t + 2, 2) / 2;
const clamp = (x, a = 0, b = 1) => Math.min(b, Math.max(a, x));
export function cam(p) {
  let i = 0; while (i < K.length - 2 && p > K[i + 1][0]) i++;
  const a = K[i], b = K[i + 1]; const t = clamp((p - a[0]) / (b[0] - a[0])); const e = ease(t);
  const z = a[1] + (b[1] - a[1]) * e, x0 = a[3] + (b[3] - a[3]) * e;
  const moving = a[1] != b[1] ? Math.sin(Math.PI * t) : 0;
  const dist = Math.abs(z);
  const mm = Q.reducedMotion ? 0 : moving;
  const bob = Math.abs(Math.sin(dist * 1.35)) * .045 * mm, sway = Math.sin(dist * .675) * .03 * mm;
  const y = a[2] + (b[2] - a[2]) * e + bob;
  // on portrait screens step further back outside so the whole storefront fits
  const back = camera.aspect < 1 && z > 1.2 ? (z - 1.2) / 14.3 * 10 : 0;
  camera.position.set(x0 + sway, y, z + back);
  let lookY = z > 4 ? 2.75 - Math.min(1, (15.5 - z) / 11.5) * 1.15 : 1.5;
  // turn the head toward the products while standing in a department
  let yaw = 0, xo = 0, f = 0, zoom = 0, up = 0;
  for (const [s0, s1, v, step, zm, lu] of STOPS) { const tin = clamp((p - (s0 - .6)) / .6), tout = clamp((p - s1) / .55); const k = ease(tin) * (1 - ease(tout)); yaw += v * k; xo += step * k; f += k * (v ? 1 : 0);
    zoom += (camera.aspect < 1 && zm > 10 ? -20 : zm) * k; up += lu * k; }   // portrait phones: widen the view so the whole video wall fits
  camera.position.x += xo; camera.fov = (camera.aspect < 1 ? 70 : 55) - zoom; camera.updateProjectionMatrix();
  camera.lookAt(camera.position.x + Math.sin(yaw) * 10, lookY - f * .15 + up, z - Math.cos(yaw) * 10);
  camera.rotation.z += Math.sin(dist * .675) * .004 * mm;
  return moving;
}
const ANCH = [[[6.5, 2.2, -7.0], [6.4, 1.25, -6.0], [6.6, .78, -7.6]], [[-6.4, 1.25, -21.0], [-6.6, .78, -19.4], [-6.5, 2.2, -19.9]], [[6.6, 2.55, -34.0], [6.6, 1.92, -35.3], [6.6, .9, -35.6]],
  [[1.4, 1.35, -46.4], [4.4, 1.35, -50.8], [6.8, 2.6, -54.0]], [[2.3, 1.35, -60.2], [6.1, 2.3, -62.0], [2.3, 1.25, -63.6]]];
export function anchors(i) { return (ANCH[i] || []).map(([x, y, z]) => { const v = new THREE.Vector3(x, y, z).project(camera); return [(v.x * .5 + .5) * 100, (-v.y * .5 + .5) * 100, v.z < 1]; }); }

/* ---------- video screen on the back wall ----------
   The video is shown on the 3D screen itself (VideoTexture). Put your file at
   assets/video/our-story.mp4 (or call setVideo(url)).                           */
const screenMesh = scene.getObjectByName('videoScreen');
const poster = canvasTex(1280, 720, (g, w, h) => {
  const gr = g.createLinearGradient(0, 0, w, h); gr.addColorStop(0, '#111e33'); gr.addColorStop(1, '#065b53'); g.fillStyle = gr; g.fillRect(0, 0, w, h);
  for (let i = 0; i < 6; i++) { g.beginPath(); g.arc(w * .85, h * .2, 120 + i * 90, 0, 7); g.lineWidth = 2; g.strokeStyle = 'rgba(217,247,236,.14)'; g.stroke(); }
  g.textAlign = 'center'; g.fillStyle = '#8fdcc0'; g.font = '600 30px Inter, Arial'; g.fillText('L I T T L E   T H R E A D S', w / 2, h * .25);
  g.fillStyle = '#FFFFFF'; g.font = 'italic 500 86px "Cormorant Garamond", Georgia, serif'; g.fillText('Our story', w / 2, h * .38);
  g.beginPath(); g.arc(w / 2, h * .62, 78, 0, 7); g.fillStyle = 'rgba(255,255,255,.95)'; g.fill();
  g.beginPath(); g.moveTo(w / 2 - 22, h * .62 - 34); g.lineTo(w / 2 - 22, h * .62 + 34); g.lineTo(w / 2 + 38, h * .62); g.closePath(); g.fillStyle = '#08766c'; g.fill();
  g.fillStyle = 'rgba(255,255,255,.7)'; g.font = '500 26px Inter, Arial'; g.fillText('Click to play', w / 2, h * .84);
});
if (screenMesh) screenMesh.material.map = poster;
const video = document.createElement('video');
Object.assign(video, { playsInline: true, preload: 'metadata', loop: false, crossOrigin: 'anonymous' });
video.setAttribute('playsinline', ''); video.src = '/home/assets/video/our-story.mp4';
let videoTex = null;
export const screen = {
  video,
  setVideo(url) { video.src = url; },
  get playing() { return !video.paused && !video.ended; },
  async toggle() {
    if (!screenMesh) return false;
    if (this.playing) { video.pause(); return false; }
    if (!videoTex) { videoTex = new THREE.VideoTexture(video); videoTex.colorSpace = THREE.SRGBColorSpace; }
    await video.play();                       // throws if the file is missing / not allowed
    screenMesh.material.map = videoTex; screenMesh.material.needsUpdate = true;
    return true;
  },
  pause() { if (this.playing) video.pause(); },
  showPoster() { if (screenMesh) { screenMesh.material.map = poster; screenMesh.material.needsUpdate = true; } },
  hit(clientX, clientY) {                     // is the pointer over the screen?
    if (!screenMesh) return false;
    const r = canvas.getBoundingClientRect();
    _ndc.set((clientX - r.left) / r.width * 2 - 1, -(clientY - r.top) / r.height * 2 + 1);
    _ray.setFromCamera(_ndc, camera); return _ray.intersectObject(screenMesh).length > 0;
  }
};
const _ray = new THREE.Raycaster(), _ndc = new THREE.Vector2();
video.addEventListener('ended', () => screen.showPoster());

export async function warmUp() {            // compile every shader before the user starts scrolling
  cam(0); if (renderer.compileAsync) await renderer.compileAsync(scene, camera); else renderer.compile(scene, camera);
  renderer.render(scene, camera);
}
export function setPixelRatio(r) { renderer.setPixelRatio(Math.min(MAX_DPR, r)); renderer.setSize(canvas.clientWidth, canvas.clientHeight, false); }
export const maxPixelRatio = MAX_DPR;
export function view(pos, look, fov = 55) { camera.position.set(...pos); camera.fov = fov; camera.updateProjectionMatrix(); camera.lookAt(...look); renderer.render(scene, camera); }
export function render(p) { const mv = cam(p); renderer.render(scene, camera); return mv; }
export function resize(w, h) { renderer.setSize(w, h, false); camera.aspect = w / h; camera.updateProjectionMatrix(); }
