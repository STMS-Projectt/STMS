/*  Little Threads — staff areas behind the shop floor
    Zone 4  "Supply Hub"         z -42 … -57 : reception, open-plan desks, glass meeting room, live dashboards
    Zone 5  "Quality & Dispatch" z -57 … -71 : QA table, packing stations, pallet racks, loading door
    People are stylised (faceless, architectural-visualisation style) so they read as staff, not as real persons.  */
import * as THREE from 'three';
import { RoundedBoxGeometry } from 'three/addons/geometries/RoundedBoxGeometry.js';
import { twillMaterial, knitMaterial, foldedMaterial, heightToNormal } from './realism.js';
import { person as human } from './people.js';

const rnd = (a = 0, b = 1) => a + Math.random() * (b - a);
const pick = a => a[Math.floor(Math.random() * a.length)];
function mk(w, h) { const c = document.createElement('canvas'); c.width = w; c.height = h; return [c, c.getContext('2d')]; }
function tex(c, rep) { const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace; t.anisotropy = 8; if (rep) { t.wrapS = t.wrapT = THREE.RepeatWrapping; t.repeat.set(...rep); } return t; }
const _m = {}; const mat = (k, f) => _m[k] || (_m[k] = f());
const std = (color, o = {}) => mat('s' + color + JSON.stringify(o), () => new THREE.MeshStandardMaterial({ color, roughness: .7, ...o }));
function add(geo, m, x, y, z, parent, o = {}) { const ms = new THREE.Mesh(geo, m); ms.position.set(x, y, z); if (o.rx) ms.rotation.x = o.rx; if (o.ry) ms.rotation.y = o.ry; if (o.rz) ms.rotation.z = o.rz; if (o.s) ms.scale.set(...o.s); ms.castShadow = o.cast !== false; ms.receiveShadow = true; parent.add(ms); return ms; }
const RB = (w, h, d, r = .02) => new RoundedBoxGeometry(w, h, d, 2, r);
const BOX = (w, h, d) => new THREE.BoxGeometry(w, h, d);

/* ======================= PEOPLE ======================= */
const SKIN = ['#EBC7A9', '#D9A784', '#C68E68', '#9A6646', '#F1D3BC', '#7A4E35'];
const HAIR = ['#2B211C', '#4A3426', '#1C1A19', '#6B4A30', '#A7835A', '#2E2622'];
let pc = 0;
/* maps the simple staff options used below to the detailed figure in people.js, adding natural variety */
export function person(parent, x, y, z, ry, o = {}) {
  const i = pc++;
  const female = !!(o.long || o.bun || o.female);
  const hairStyle = o.bun ? 'bun' : o.long ? ['long', 'pony', 'long'][i % 3] : ['short', 'side', 'curly', 'short', 'bald'][i % 5];
  const opts = {
    pose: o.pose, female, hairStyle, seed: i,
    skin: SKIN[(i * 7) % SKIN.length], hair: hairStyle === 'bald' ? '#3A2E28' : HAIR[(i * 5) % HAIR.length],
    top: o.shortSleeve ? 'tee' : female ? ['blouse', 'sweater', 'blouse'][i % 3] : ['shirt', 'sweater', 'shirt', 'tee'][i % 4],
    shirt: o.shirt, pants: o.pants, blazer: o.jacket || null, skirt: female && i % 4 === 1 && o.pose !== 'pack' ? '#2E3E57' : null,
    glasses: i % 3 === 1, lanyard: o.pose !== 'sit' || i % 2 === 0, headset: o.pose === 'sit' && i % 7 === 3, watch: true, rolled: i % 2 === 0,
    shoes: o.pose === 'pack' || o.pose === 'carry' || i % 3 === 0 ? 'sneaker' : 'leather', shoeColor: i % 3 === 0 ? '#FFFFFF' : undefined,
    tablet: o.tablet, look: o.look || 0
  };
  if (o.pose === 'carry') opts.carry = new THREE.Mesh(RB(.42, .3, .32, .01), cartonMat());
  return human(parent, x, y, z, ry, opts);
}

/* ======================= OFFICE FURNITURE ======================= */
function screenTex(kind) {
  const [c, g] = mk(512, 320); g.fillStyle = '#F7F5F1'; g.fillRect(0, 0, 512, 320);
  g.fillStyle = '#111e33'; g.fillRect(0, 0, 512, 34); g.fillStyle = '#8fdcc0'; g.beginPath(); g.arc(20, 17, 8, 0, 7); g.fill();
  g.fillStyle = '#FFFFFF'; g.font = '600 15px Inter, Arial'; g.fillText(kind === 'orders' ? 'Orders' : kind === 'suppliers' ? 'Suppliers' : kind === 'map' ? 'Shipments' : 'Catalogue', 38, 22);
  if (kind === 'map') { g.fillStyle = '#E6ECEF'; g.fillRect(0, 34, 512, 286); g.strokeStyle = '#C9D3D9'; g.lineWidth = 2; for (let i = 0; i < 9; i++) { g.beginPath(); g.moveTo(rnd(0, 512), 34); g.bezierCurveTo(rnd(0, 512), 120, rnd(0, 512), 220, rnd(0, 512), 320); g.stroke(); }
    g.strokeStyle = '#08766c'; g.lineWidth = 3; g.setLineDash([6, 5]); for (let i = 0; i < 5; i++) { g.beginPath(); g.moveTo(256, 180); g.quadraticCurveTo(rnd(100, 400), rnd(40, 120), rnd(40, 470), rnd(60, 300)); g.stroke(); } g.setLineDash([]);
    for (let i = 0; i < 7; i++) { g.fillStyle = i ? '#111e33' : '#08766c'; g.beginPath(); g.arc(i ? rnd(40, 470) : 256, i ? rnd(60, 300) : 180, i ? 6 : 10, 0, 7); g.fill(); } return tex(c); }
  // KPI tiles
  for (let k = 0; k < 3; k++) { g.fillStyle = '#FFFFFF'; g.fillRect(14 + k * 166, 46, 152, 58); g.fillStyle = '#6B7385'; g.font = '11px Inter, Arial'; g.fillText(['Today', 'Pending', 'Shipped'][k], 24 + k * 166, 64); g.fillStyle = '#1E2A44'; g.font = '600 22px Inter, Arial'; g.fillText(String(Math.floor(rnd(80, 1400))), 24 + k * 166, 92); }
  if (kind === 'orders') { g.fillStyle = '#FFFFFF'; g.fillRect(14, 116, 484, 190); for (let i = 0; i < 12; i++) { const h = rnd(30, 150); g.fillStyle = i % 4 === 3 ? '#08766c' : '#bcc8da'; g.fillRect(34 + i * 38, 290 - h, 22, h); } }
  else for (let i = 0; i < 7; i++) { g.fillStyle = i % 2 ? '#FFFFFF' : '#F1ECE4'; g.fillRect(14, 116 + i * 27, 484, 27); g.fillStyle = '#111e33'; g.fillRect(26, 126 + i * 27, rnd(80, 160), 7); g.fillStyle = ['#007858', '#08766c', '#bcc8da'][i % 3]; g.fillRect(400, 124 + i * 27, 70, 11); }
  return tex(c);
}
const screens = {};
function screenMat(kind) { return screens[kind] || (screens[kind] = (() => { const t = screenTex(kind); return new THREE.MeshStandardMaterial({ map: t, emissive: '#ffffff', emissiveMap: t, emissiveIntensity: .75, roughness: .4 }); })()); }
const darkMetal = std('#2B2F36', { roughness: .4, metalness: .6 });
const deskTop = std('#F4F1EC', { roughness: .45 });
const oak = std('#C9A27A', { roughness: .55 });

function chair(parent, x, z, ry, color = '#3A4250') {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = ry; parent.add(g);
  const fab = knitMaterial(color);
  add(RB(.48, .08, .46, .03), fab, 0, .48, 0, g); add(RB(.46, .52, .07, .03), fab, 0, .84, -.24, g, { rx: -.12 });
  add(new THREE.CylinderGeometry(.025, .025, .38, 10), darkMetal, 0, .27, 0, g);
  for (let k = 0; k < 5; k++) { const a = k / 5 * 6.28; add(BOX(.3, .03, .04), darkMetal, Math.cos(a) * .15, .06, Math.sin(a) * .15, g, { ry: -a }); add(new THREE.SphereGeometry(.03, 8, 6), darkMetal, Math.cos(a) * .29, .03, Math.sin(a) * .29, g); }
  return g;
}
function desk(parent, x, z, ry, o = {}) {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = ry; parent.add(g);
  add(RB(1.4, .035, .72, .01), deskTop, 0, .74, 0, g);
  for (const s of [-1, 1]) { add(BOX(.04, .72, .04), darkMetal, s * .66, .37, -.3, g); add(BOX(.04, .72, .04), darkMetal, s * .66, .37, .3, g); add(BOX(.04, .04, .64), darkMetal, s * .66, .04, 0, g); }
  // monitor
  add(BOX(.04, .3, .04), darkMetal, 0, .9, -.26, g); add(RB(.22, .02, .16, .005), darkMetal, 0, .765, -.26, g);
  add(RB(.62, .38, .03, .01), std('#1C1F25', { roughness: .3 }), 0, 1.12, -.23, g);
  add(new THREE.PlaneGeometry(.58, .34), screenMat(o.screen || 'orders'), 0, 1.12, -.214, g, { cast: false });
  add(RB(.42, .018, .13, .006), std('#E9E7E3', { roughness: .5 }), 0, .766, .08, g); add(RB(.06, .02, .1, .02), std('#E9E7E3'), .3, .766, .1, g);
  if (o.mug) { add(new THREE.CylinderGeometry(.04, .036, .1, 16), std(o.mug), -.5, .8, -.05, g); }
  if (o.papers) for (let k = 0; k < 3; k++) add(BOX(.21, .004, .297), std('#FFFFFF', { roughness: .9 }), .48 + k * .01, .76 + k * .005, -.02, g, { ry: rnd(-.2, .2), cast: false });
  if (o.swatch) for (let k = 0; k < 4; k++) add(BOX(.12, .006, .12), foldedMaterial(['#F2C6C2', '#9DB4C9', '#E8C9A8', '#8FA88A'][k]), -.52 + k * .06, .762 + k * .006, .15, g, { ry: k * .2 });
  if (o.plant) { add(new THREE.CylinderGeometry(.06, .05, .1, 16), std('#E8E1D6'), -.55, .8, -.25, g); add(new THREE.SphereGeometry(.09, 12, 10), std('#6F9A58', { roughness: 1 }), -.55, .9, -.25, g); }
  return g;
}

/* ======================= DISPATCH ======================= */
let _carton;
function cartonMat() {
  if (_carton) return _carton;
  const [c, g] = mk(256, 256); g.fillStyle = '#C69C6D'; g.fillRect(0, 0, 256, 256);
  for (let i = 0; i < 2500; i++) { g.fillStyle = `rgba(${Math.random() < .5 ? '255,240,220' : '90,60,30'},.06)`; g.fillRect(rnd(0, 256), rnd(0, 256), 3, 1); }
  g.fillStyle = 'rgba(210,180,140,.9)'; g.fillRect(118, 0, 20, 256);
  g.fillStyle = '#FFFFFF'; g.fillRect(150, 160, 86, 70); g.fillStyle = '#111e33'; for (let i = 0; i < 22; i++) g.fillRect(156 + i * 3.4, 192, Math.random() < .5 ? 1.5 : 2.5, 30);
  g.font = '700 14px Inter, Arial'; g.fillText('LT', 158, 182); g.fillStyle = '#08766c'; g.fillRect(186, 172, 40, 6);
  return _carton = new THREE.MeshStandardMaterial({ map: tex(c), roughness: .85 });
}
function carton(parent, x, y, z, w, h, d, ry = 0) { return add(RB(w, h, d, .012), cartonMat(), x, y + h / 2, z, parent, { ry }); }
function pallet(parent, x, z, ry = 0, load = true) {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = ry; parent.add(g);
  const w = std('#C9A57B', { roughness: .9 });
  for (let k = 0; k < 7; k++) add(BOX(1.2, .022, .12), w, 0, .133, -.45 + k * .15, g);
  for (const s of [-1, 0, 1]) add(BOX(1.2, .1, .1), w, 0, .07, s * .42, g);
  if (load) { let y = .145; for (let l = 0; l < 3; l++) { for (let i = 0; i < 2; i++) for (let j = 0; j < 2; j++) carton(g, -.29 + i * .58, y, -.24 + j * .48, .56, .36, .46, rnd(-.03, .03)); y += .36; } }
  return g;
}
function rack(parent, x, z0, z1, side) {
  const navy = std('#111e33', { roughness: .5, metalness: .3 }), beam = std('#08766c', { roughness: .45, metalness: .35 });
  const depth = 1.1, levels = [.15, 1.45, 2.75];
  const bays = Math.floor((z0 - z1) / 2.7);
  for (let b = 0; b <= bays; b++) { const z = z0 - b * 2.7; for (const dx of [0, depth]) add(BOX(.08, 3.7, .08), navy, x + side * dx, 1.85, z, parent); }
  for (let b = 0; b < bays; b++) { const zc = z0 - b * 2.7 - 1.35;
    for (const ly of levels) { for (const dx of [0, depth]) add(BOX(.06, .12, 2.6), beam, x + side * dx, ly + .06, zc, parent); if (ly > .2) add(BOX(depth, .02, 2.6), std('#B9BEC4', { metalness: .6, roughness: .5 }), x + side * depth / 2, ly + .12, zc, parent, { cast: false });
      if (Math.random() < .85) { const p = pallet(parent, x + side * depth / 2, zc + rnd(-.6, -.55), Math.PI / 2, false); p.position.y = ly + .13;
        let y = .145; const rows = ly < 2 ? 3 : 2; for (let l = 0; l < rows; l++) for (let i = 0; i < 2; i++) for (let j = 0; j < 2; j++) carton(p, -.29 + i * .58, y + l * .36, -.24 + j * .48, .56, .35, .46, rnd(-.03, .03)); }
      if (Math.random() < .7) { const p = pallet(parent, x + side * depth / 2, zc + .65, Math.PI / 2, false); p.position.y = ly + .13; for (let l = 0; l < 2; l++) for (let i = 0; i < 2; i++) for (let j = 0; j < 2; j++) carton(p, -.29 + i * .58, .145 + l * .36, -.24 + j * .48, .56, .35, .46); } } }
}
function packTable(parent, x, z, ry) {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = ry; parent.add(g);
  add(RB(1.8, .05, .85, .01), oak, 0, .88, 0, g);
  for (const s of [-1, 1]) for (const t of [-1, 1]) add(BOX(.05, .86, .05), darkMetal, s * .85, .43, t * .38, g);
  add(BOX(1.7, .03, .75), std('#E9E4D8'), 0, .3, 0, g);
  // shelf above with paper rolls + tape
  for (const s of [-1, 1]) add(BOX(.04, .7, .04), darkMetal, s * .85, 1.25, -.38, g); add(BOX(1.74, .03, .3), std('#F4F1EC'), 0, 1.5, -.3, g);
  add(new THREE.CylinderGeometry(.06, .06, .9, 20), std('#F3EEE6', { roughness: .9 }), -.3, 1.58, -.3, g, { rz: Math.PI / 2 });
  for (let k = 0; k < 3; k++) add(new THREE.TorusGeometry(.05, .025, 8, 20), std('#C9A27A', { roughness: .6 }), .45 + k * .14, 1.57, -.3, g);
  // open box with folded clothes + tissue
  const ob = carton(g, -.35, .905, .05, .5, .22, .38);
  add(BOX(.46, .01, .34), std('#FBF6EE', { roughness: 1 }), -.35, 1.12, .05, g, { cast: false });
  for (let k = 0; k < 3; k++) add(RB(.34, .045, .26, .015), foldedMaterial(['#F2C6C2', '#9DB4C9', '#E8C9A8'][k]), -.35, 1.13 + k * .046, .05, g);
  for (const s of [-1, 1]) add(BOX(.5, .01, .2), std('#C69C6D', { roughness: .85 }), -.35 + s * .0, 1.125, .05 + s * .29, g, { rx: s * -.9, cast: false });
  carton(g, .35, .905, .05, .38, .28, .3, .1); carton(g, .65, .905, -.15, .3, .22, .26, -.2); carton(g, .5, 1.185, 0, .3, .2, .24, .3);
  add(RB(.14, .1, .07, .02), std('#B48A52'), .2, .95, .3, g); // tape dispenser
  add(RB(.08, .14, .04, .01), std('#1E2430'), .75, .97, .25, g, { rx: -.3 }); // scanner
  return g;
}

/* ======================= signage ======================= */
function sign(parent, text, sub, x, y, z, w = 4, dark = true, ry = 0) {
  const [c, g] = mk(1024, 256); g.fillStyle = dark ? '#111e33' : '#f5faf8'; g.fillRect(0, 0, 1024, 256);
  g.fillStyle = dark ? '#FFFFFF' : '#111e33'; g.font = '600 78px Inter, Arial'; g.textAlign = 'center'; g.textBaseline = 'middle'; g.fillText(text, 512, sub ? 108 : 128);
  if (sub) { g.fillStyle = dark ? '#8fdcc0' : '#08766c'; g.font = '600 34px Inter, Arial'; g.fillText(sub, 512, 190); }
  return add(new THREE.PlaneGeometry(w, w / 4), new THREE.MeshStandardMaterial({ map: tex(c), roughness: .6, emissive: '#ffffff', emissiveMap: tex(c), emissiveIntensity: .15 }), x, y, z, parent, { cast: false, ry });
}
function glassWall(parent, x0, x1, z, y0, h, frameMat, glass, doorFrom, doorTo) {
  const len = x1 - x0, n = Math.round(len / 1.4);
  for (let i = 0; i <= n; i++) { const x = x0 + i * len / n; if (doorFrom !== undefined && x > doorFrom + .01 && x < doorTo - .01) continue; add(BOX(.05, h, .07), frameMat, x, y0 + h / 2, z, parent); }
  add(BOX(len, .06, .08), frameMat, (x0 + x1) / 2, y0 + h, z, parent);
  const segs = doorFrom === undefined ? [[x0, x1]] : [[x0, doorFrom], [doorTo, x1]];
  for (const [a, b] of segs) { if (b - a < .05) continue; add(new THREE.PlaneGeometry(b - a, h), glass, (a + b) / 2, y0 + h / 2, z, parent, { cast: false }); add(BOX(b - a, .05, .07), frameMat, (a + b) / 2, y0 + .025, z, parent); }
}
function glassWallZ(parent, x, z0, z1, h, frameMat, glass) {
  const len = z0 - z1, n = Math.round(len / 1.4);
  for (let i = 0; i <= n; i++) add(BOX(.07, h, .05), frameMat, x, h / 2, z0 - i * len / n, parent);
  add(BOX(.08, .06, len), frameMat, x, h, (z0 + z1) / 2, parent);
  add(new THREE.PlaneGeometry(len, h), glass, x, h / 2, (z0 + z1) / 2, parent, { cast: false, ry: Math.PI / 2 });
}

export function build({ scene, IW, IH, L, M, box, rbox, canvasTex }) {
  const glass = new THREE.MeshStandardMaterial({ color: '#DCE7EC', roughness: .05, metalness: .1, transparent: true, opacity: .18, depthWrite: false, side: THREE.DoubleSide });
  const frost = new THREE.MeshStandardMaterial({ color: '#F4F6F7', roughness: .4, transparent: true, opacity: .75, side: THREE.DoubleSide });
  const frame = std('#2B2F36', { roughness: .35, metalness: .7 });
  const W2 = IW / 2;

  /* ---------- glass partition between shop and offices (z = -42) ---------- */
  glassWall(scene, -W2, W2, -42, 0, 3.2, frame, glass, -1.3, 1.3);
  add(BOX(IW, IH - 3.2, .12), M('#F4F1EC'), 0, 3.2 + (IH - 3.2) / 2, -42, scene, { cast: false });
  add(BOX(IW - .2, .25, .02), frost, 0, 1.35, -41.96, scene, { cast: false });            // frosted manifestation band
  sign(scene, 'SUPPLY HUB', 'OUR TEAM · SUPPLIERS · ORDERS', 0, 3.75, -41.93, 3.2);

  /* ---------- Zone 4: offices (-42 … -57) ---------- */
  // floor: warm grey carpet tiles
  const [cc, cg] = mk(512, 512); cg.fillStyle = '#B9B4AC'; cg.fillRect(0, 0, 512, 512); for (let i = 0; i < 20000; i++) { cg.fillStyle = `rgba(${Math.random() < .5 ? '255,255,255' : '40,40,40'},.07)`; cg.fillRect(rnd(0, 512), rnd(0, 512), 2, 2); }
  cg.strokeStyle = 'rgba(80,80,80,.18)'; cg.lineWidth = 2; for (let i = 0; i <= 512; i += 128) { cg.beginPath(); cg.moveTo(i, 0); cg.lineTo(i, 512); cg.stroke(); cg.beginPath(); cg.moveTo(0, i); cg.lineTo(512, i); cg.stroke(); }
  add(new THREE.PlaneGeometry(IW, 15), new THREE.MeshStandardMaterial({ map: tex(cc, [4, 4]), roughness: 1 }), 0, .02, -49.5, scene, { rx: -Math.PI / 2, cast: false });

  // reception
  const rec = new THREE.Shape(); rec.absarc(0, 0, 1.4, Math.PI * .1, Math.PI * .9, false); rec.absarc(0, 0, 1.0, Math.PI * .9, Math.PI * .1, true);
  const recGeo = new THREE.ExtrudeGeometry(rec, { depth: 1.05, bevelEnabled: false, curveSegments: 32 }); recGeo.rotateX(-Math.PI / 2); recGeo.rotateY(Math.PI);
  add(recGeo, std('#F4F1EC', { roughness: .5 }), -4.3, 0, -44.3, scene);
  const recTop = new THREE.Shape(); recTop.absarc(0, 0, 1.45, Math.PI * .08, Math.PI * .92, false); recTop.absarc(0, 0, .95, Math.PI * .92, Math.PI * .08, true);
  const rtg = new THREE.ExtrudeGeometry(recTop, { depth: .04, bevelEnabled: false, curveSegments: 32 }); rtg.rotateX(-Math.PI / 2); rtg.rotateY(Math.PI); add(rtg, oak, -4.3, 1.05, -44.3, scene);
  for (let k = 0; k < 24; k++) { const a = Math.PI * (.12 + k / 23 * .76); add(BOX(.05, 1.0, .03), oak, -4.3 - Math.cos(a) * 1.42, .5, -44.3 + Math.sin(a) * 1.42, scene, { ry: a - Math.PI / 2, cast: false }); }
  person(scene, -4.3, 0, -44.25, 0, { pose: 'stand', shirt: '#F5F0E6', pants: '#2E3E57', long: true, jacket: '#B48A52' });
  add(RB(.5, .3, .03, .01), std('#1C1F25'), -3.75, 1.28, -43.75, scene, { ry: .4 });
  // brand wall behind reception
  add(BOX(.04, 1.6, 4.2), oak, -W2 + .03, 2.6, -45.5, scene, { cast: false });
  sign(scene, 'LITTLE THREADS', 'KIDSWEAR SUPPLY · SINCE 2026', -W2 + .07, 2.6, -45.5, 3.4, false, Math.PI / 2);

  // open-plan desk clusters (right half)
  const shirts = ['#E9E4D8', '#9DB4C9', '#F2C6C2', '#FFFFFF', '#7F9A79', '#2E3E57', '#E8C9A8', '#C9B28C'];
  const scr = ['orders', 'suppliers', 'catalogue', 'map'];
  let n = 0;
  for (const cz of [-46.6, -51.0]) for (const cx of [1.4, 4.4]) {
    for (const s of [-1, 1]) for (const k of [-1, 1]) {
      const dx = cx + k * .72, dz = cz + s * .42, face = s < 0 ? Math.PI : 0;
      desk(scene, dx, dz, face, { screen: scr[n % 4], mug: n % 3 ? null : '#F2C6C2', papers: n % 2 === 0, plant: n % 5 === 1, swatch: n % 4 === 2 });
      chair(scene, dx, dz + (s < 0 ? -.62 : .62), face + Math.PI);
      if (n % 4 !== 3) person(scene, dx, 0, dz + (s < 0 ? -.5 : .5), face + Math.PI, { pose: 'sit', shirt: shirts[n % shirts.length], pants: pick(['#2E3E57', '#3B3F45', '#6E6A64', '#4C6A8F']), long: n % 2 === 1, bun: n % 5 === 0, shortSleeve: n % 3 === 0 });
      n++;
    }
    add(BOX(2.9, .4, .03), knitMaterial('#cdeadf'), cx, .95, cz, scene, { cast: false });   // acoustic divider
  }
  // standing colleagues chatting by the dashboard wall
  person(scene, 5.6, 0, -54.2, -2.4, { pose: 'talk', shirt: '#2E3E57', pants: '#C9B28C', tablet: true });
  person(scene, 4.8, 0, -54.9, .8, { pose: 'tablet', shirt: '#F2C6C2', pants: '#2E3E57', long: true });
  person(scene, 6.1, 0, -55.3, -1.4, { pose: 'coffee', shirt: '#E9E4D8', pants: '#4C6A8F', look: .3 });
  // big dashboard screen on the right wall
  add(RB(3.2, 1.85, .06, .02), std('#1C1F25', { roughness: .3 }), W2 - .05, 2.3, -54.0, scene, { ry: -Math.PI / 2 });
  add(new THREE.PlaneGeometry(3.08, 1.73), screenMat('map'), W2 - .09, 2.3, -54.0, scene, { ry: -Math.PI / 2, cast: false });
  // glass meeting room (left)
  glassWallZ(scene, -2.4, -47.4, -55.6, 3.0, frame, glass);
  glassWall(scene, -W2, -2.4, -47.4, 0, 3.0, frame, glass, -3.8, -2.6);
  glassWall(scene, -W2, -2.4, -55.6, 0, 3.0, frame, glass);
  add(BOX(4.6, IH - 3.0, .1), M('#F4F1EC'), -4.7, 3.0 + (IH - 3) / 2, -47.4, scene, { cast: false });
  add(BOX(.1, IH - 3.0, 8.2), M('#F4F1EC'), -2.4, 3.0 + (IH - 3) / 2, -51.5, scene, { cast: false });
  sign(scene, 'MEETING ROOM', '', -4.7, 2.75, -47.33, 1.3, true);
  add(new THREE.CylinderGeometry(.85, .85, .04, 48), oak, -4.6, .74, -51.4, scene); add(new THREE.CylinderGeometry(.08, .2, .72, 20), darkMetal, -4.6, .36, -51.4, scene);
  for (let k = 0; k < 5; k++) { const a = k / 5 * 6.28 + .3, face = Math.atan2(-Math.cos(a), -Math.sin(a)); chair(scene, -4.6 + Math.cos(a) * 1.15, -51.4 + Math.sin(a) * 1.15, face, '#C9B28C');
    if (k < 3) person(scene, -4.6 + Math.cos(a) * 1.05, 0, -51.4 + Math.sin(a) * 1.05, face, { pose: 'sit', shirt: shirts[(k * 3) % 8], pants: '#2E3E57', long: k === 1, jacket: k === 2 ? '#2E3E57' : null }); }
  add(RB(.34, .02, .24, .01), std('#1E2430'), -4.4, .77, -51.1, scene); add(BOX(.21, .004, .297), std('#FFFFFF'), -4.9, .765, -51.0, scene, { ry: .4 });
  add(RB(2.0, 1.15, .05, .02), std('#1C1F25'), -W2 + .05, 1.8, -51.4, scene, { ry: Math.PI / 2 });
  add(new THREE.PlaneGeometry(1.9, 1.05), screenMat('suppliers'), -W2 + .09, 1.8, -51.4, scene, { ry: Math.PI / 2, cast: false });
  // plants
  for (const [px, pz] of [[-6.3, -43.0], [6.3, -43.2], [-6.3, -56.3], [6.3, -56.3]]) { add(new THREE.CylinderGeometry(.26, .2, .55, 24), std('#E8E1D6'), px, .275, pz, scene); for (let k = 0; k < 6; k++) add(new THREE.SphereGeometry(rnd(.18, .26), 12, 10), std(pick(['#6F9A58', '#5C8848', '#82AA66']), { roughness: 1 }), px + rnd(-.18, .18), .75 + k * .14, pz + rnd(-.18, .18), scene); }
  // linear office pendants
  for (const cz of [-46.6, -51.0]) for (const cx of [1.4, 4.4]) { add(BOX(2.4, .05, .12), std('#2B2F36', { metalness: .5, roughness: .4 }), cx, 3.2, cz, scene, { cast: false }); add(BOX(2.3, .01, .08), new THREE.MeshBasicMaterial({ color: '#FFFFFF' }), cx, 3.17, cz, scene, { cast: false }); for (const s of [-1, 1]) add(BOX(.006, 1.2, .006), darkMetal, cx + s * 1.1, 3.8, cz, scene, { cast: false }); }

  /* ---------- partition to dispatch (z = -57) ---------- */
  add(BOX(4.8, IH, .2), M('#F4F1EC'), -W2 + 2.4, IH / 2, -57, scene); add(BOX(4.8, IH, .2), M('#F4F1EC'), W2 - 2.4, IH / 2, -57, scene);
  add(BOX(4.4, IH - 3.4, .2), M('#F4F1EC'), 0, 3.4 + (IH - 3.4) / 2, -57, scene);
  sign(scene, 'QUALITY & DISPATCH', 'CHECKED · PACKED · SHIPPED', 0, 3.9, -56.88, 3.0);

  /* ---------- Zone 5: quality & dispatch (-57 … -71) ---------- */
  const [ec, eg] = mk(512, 512); eg.fillStyle = '#B5B2AC'; eg.fillRect(0, 0, 512, 512); for (let i = 0; i < 16000; i++) { eg.fillStyle = `rgba(${Math.random() < .5 ? '255,255,255' : '30,30,30'},.06)`; eg.fillRect(rnd(0, 512), rnd(0, 512), 3, 3); }
  eg.fillStyle = '#E2B44C'; eg.fillRect(0, 0, 512, 10); eg.fillRect(0, 502, 512, 10);
  add(new THREE.PlaneGeometry(IW, 14.5), new THREE.MeshStandardMaterial({ map: tex(ec), roughness: .6 }), 0, .021, -64.2, scene, { rx: -Math.PI / 2, cast: false });
  rack(scene, -W2 + .1, -58.2, -70.6, 1); rack(scene, W2 - .1, -58.2, -70.6, -1);
  // QA table with samples
  const qa = new THREE.Group(); qa.position.set(-2.2, 0, -59.6); scene.add(qa);
  add(RB(2.0, .05, .9, .01), std('#F4F1EC'), 0, .9, 0, qa); for (const s of [-1, 1]) for (const t of [-1, 1]) add(BOX(.05, .88, .05), darkMetal, s * .95, .44, t * .4, qa);
  for (let k = 0; k < 6; k++) add(BOX(.22, .008, .22), foldedMaterial(['#F2C6C2', '#9DB4C9', '#E8C9A8', '#8FA88A', '#F7E3D5', '#2E3E57'][k]), -.75 + (k % 3) * .26, .93, -.18 + Math.floor(k / 3) * .26, qa, { ry: rnd(-.2, .2) });
  add(new THREE.TorusGeometry(.08, .012, 8, 20), std('#E2B44C'), .55, .935, .15, qa, { rx: Math.PI / 2 });
  add(RB(.5, .04, .36, .01), foldedMaterial('#F2C6C2'), .4, .94, -.15, qa);
  add(BOX(.04, .5, .04), darkMetal, .85, 1.17, -.3, qa); add(RB(.3, .05, .12, .02), std('#F4F1EC', { emissive: '#ffffff', emissiveIntensity: .3 }), .72, 1.42, -.3, qa);
  person(scene, -2.2, 0, -58.75, Math.PI, { pose: 'pack', shirt: '#FFFFFF', pants: '#3B3F45', bun: true, long: false });
  sign(scene, 'QUALITY CHECK', '', -2.2, 2.1, -60.2, 1.4, true);
  // packing stations
  packTable(scene, 2.3, -60.2, 0); person(scene, 2.3, 0, -59.45, Math.PI, { pose: 'pack', shirt: '#9DB4C9', pants: '#2E3E57', shortSleeve: true });
  packTable(scene, 2.3, -63.6, 0); person(scene, 2.3, 0, -62.85, Math.PI, { pose: 'pack', shirt: '#E8C9A8', pants: '#3B3F45', long: true });
  // outbound pallets + worker carrying a box
  pallet(scene, -3.7, -67.6, 0); pallet(scene, -2.3, -68.3, .08); pallet(scene, 3.6, -67.9, -.06);
  person(scene, -2.6, 0, -66.3, 2.6, { pose: 'carry', shirt: '#7F9A79', pants: '#2E3E57' });
  // pallet jack
  const pj = new THREE.Group(); pj.position.set(-2.8, 0, -64.2); pj.rotation.y = .5; scene.add(pj);
  for (const s of [-1, 1]) add(BOX(.16, .08, 1.15), std('#B48A52', { metalness: .4, roughness: .4 }), s * .28, .06, 0, pj);
  add(BOX(.7, .3, .2), std('#B48A52', { metalness: .4 }), 0, .2, .62, pj); add(new THREE.CylinderGeometry(.02, .02, 1.1, 8), darkMetal, 0, .75, .78, pj, { rx: -.3 });
  // back wall with the big video screen ("Our story") — the last stop of the walk
  add(BOX(IW, IH, .2), M('#F4F1EC'), 0, IH / 2, -71.6, scene, { cast: false });
  add(BOX(6.4, 3.75, .06), M('#065b53', { roughness: .8 }), 0, 2.08, -71.47, scene, { cast: false });           // navy feature panel
  add(RB(5.5, 3.2, .12, .03), std('#15171B', { roughness: .3, metalness: .3 }), 0, 2.0, -71.4, scene, { cast: false });
  const vScreen = new THREE.Mesh(new THREE.PlaneGeometry(5.3, 5.3 * 9 / 16), new THREE.MeshBasicMaterial({ color: '#ffffff', toneMapped: false }));
  vScreen.position.set(0, 2.0, -71.33); vScreen.name = 'videoScreen'; vScreen.userData.keep = true; scene.add(vScreen);
  const ledPts = [[-2.82, .36], [2.82, .36], [2.82, 3.64], [-2.82, 3.64], [-2.82, .36]].map(([x, y]) => new THREE.Vector3(x, y, -71.42));
  add(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(ledPts, false, 'catmullrom', 0), 80, .012, 6), new THREE.MeshBasicMaterial({ color: '#FFE6C2' }), 0, 0, 0, scene, { cast: false });
  sign(scene, 'OUR STORY', '', 0, 4.0, -71.43, 1.4, false);
  // small loading door on the left
  const [dc, dg] = mk(64, 256); for (let y = 0; y < 256; y++) { const v = Math.sin((y % 16) / 16 * Math.PI); dg.fillStyle = `rgb(${v * 255},${v * 255},${v * 255})`; dg.fillRect(0, y, 64, 1); }
  const ribs = heightToNormal(dc, 5, [1, 8]);
  add(new THREE.PlaneGeometry(2.0, 3.0), new THREE.MeshStandardMaterial({ color: '#D3D7DC', metalness: .5, roughness: .45, normalMap: ribs }), -4.6, 1.5, -71.48, scene, { cast: false });
  add(BOX(2.2, .1, .14), std('#E2B44C'), -4.6, 3.05, -71.42, scene); for (const s2 of [-1, 1]) add(BOX(.1, 3.1, .14), std('#E2B44C'), -4.6 + s2 * 1.05, 1.55, -71.42, scene);
  sign(scene, 'DOCK 1', '', -4.6, 3.45, -71.46, 1.0, true);
  // high-bay lights
  for (const z of [-60, -64.5, -69]) for (const x of [-3, 3]) { add(new THREE.CylinderGeometry(.25, .32, .18, 24, 1, true), darkMetal, x, 3.8, z, scene, { cast: false }); add(new THREE.CircleGeometry(.25, 24), new THREE.MeshBasicMaterial({ color: '#FFFFFF' }), x, 3.72, z, scene, { rx: Math.PI / 2, cast: false }); }
}
