/*  Little Threads — procedural realism helpers: fabric textures, garments,
    folded stacks, accessories and foliage (no external assets needed).  */
import * as THREE from 'three';

const rnd = (a = 0, b = 1) => a + Math.random() * (b - a);
let seed = 7; export const srand = () => (seed = (seed * 16807) % 2147483647) / 2147483647;

/* ---------- texture helpers ---------- */
function mk(w, h) { const c = document.createElement('canvas'); c.width = w; c.height = h; return [c, c.getContext('2d')]; }
function tex(c, srgb = true, rep) { const t = new THREE.CanvasTexture(c); if (srgb) t.colorSpace = THREE.SRGBColorSpace; t.anisotropy = 8; if (rep) { t.wrapS = t.wrapT = THREE.RepeatWrapping; t.repeat.set(...rep); } return t; }
// height canvas -> normal map texture
export function heightToNormal(hc, strength = 2, rep) {
  const w = hc.width, h = hc.height, src = hc.getContext('2d').getImageData(0, 0, w, h).data;
  const [c, g] = mk(w, h); const out = g.createImageData(w, h);
  const H = (x, y) => src[(((y + h) % h) * w + ((x + w) % w)) * 4] / 255;
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
    const dx = (H(x + 1, y) - H(x - 1, y)) * strength, dy = (H(x, y + 1) - H(x, y - 1)) * strength;
    const l = Math.hypot(dx, dy, 1), i = (y * w + x) * 4;
    out.data[i] = (-dx / l * .5 + .5) * 255; out.data[i + 1] = (dy / l * .5 + .5) * 255; out.data[i + 2] = (1 / l * .5 + .5) * 255; out.data[i + 3] = 255; }
  g.putImageData(out, 0, 0); return tex(c, false, rep);
}
// fine fabric weave height
const weaveCache = {};
export function weaveNormal(kind = 'cotton', rep = [6, 6]) {
  const key = kind + rep; if (weaveCache[key]) return weaveCache[key];
  const [c, g] = mk(256, 256); g.fillStyle = '#808080'; g.fillRect(0, 0, 256, 256);
  if (kind == 'knit') { for (let x = 0; x < 256; x += 8) for (let y = 0; y < 256; y += 8) { g.fillStyle = '#d0d0d0'; g.beginPath(); g.ellipse(x + 4, y + 4, 3, 4.5, 0, 0, 7); g.fill(); g.fillStyle = '#404040'; g.fillRect(x, y, 1, 8); } }
  else if (kind == 'denim' || kind == 'twill') { for (let i = -256; i < 512; i += 4) { g.strokeStyle = '#c4c4c4'; g.lineWidth = 2; g.beginPath(); g.moveTo(i, 0); g.lineTo(i + 256, 256); g.stroke(); } }
  else if (kind == 'straw') { for (let y = 0; y < 256; y += 10) for (let x = 0; x < 256; x += 10) { g.fillStyle = ((x + y) / 10) % 2 ? '#e0e0e0' : '#606060'; g.fillRect(x, y, 10, 10); } }
  else { for (let y = 0; y < 256; y += 3) { g.fillStyle = 'rgba(200,200,200,.6)'; g.fillRect(0, y, 256, 1); } for (let x = 0; x < 256; x += 3) { g.fillStyle = 'rgba(80,80,80,.45)'; g.fillRect(x, 0, 1, 256); } }
  for (let i = 0; i < 2500; i++) { g.fillStyle = `rgba(${Math.random() < .5 ? 255 : 0},${Math.random() < .5 ? 255 : 0},128,.05)`; g.fillRect(rnd(0, 256), rnd(0, 256), 2, 2); }
  return weaveCache[key] = heightToNormal(c, kind == 'knit' ? 3 : 1.6, rep);
}
function shade(hex, f) { const c = new THREE.Color(hex); const hsl = {}; c.getHSL(hsl); c.setHSL(hsl.h, hsl.s, Math.min(1, Math.max(0, hsl.l + f))); return '#' + c.getHexString(); }

/* ---------- garment silhouettes (normalized: x -0.56..0.56, y 0..-1.05) ---------- */
function outline(kind) {
  const s = new THREE.Shape();
  if (kind == 'tee' || kind == 'jacket' || kind == 'cardigan') { const lng = kind != 'tee'; const sl = lng ? -.9 : -.36;
    s.moveTo(-.13, 0); s.quadraticCurveTo(0, -.13, .13, 0); s.lineTo(.38, -.06); s.quadraticCurveTo(.47, -.12, .52, sl + .04); s.lineTo(lng ? .42 : .36, sl - .01);
    s.lineTo(.34, -.42); s.quadraticCurveTo(.36, -.8, .35, -1); s.quadraticCurveTo(0, -1.02, -.35, -1); s.quadraticCurveTo(-.36, -.8, -.34, -.42); s.lineTo(lng ? -.42 : -.36, sl - .01); s.lineTo(-.52, sl + .04); s.quadraticCurveTo(-.47, -.12, -.38, -.06); s.closePath(); }
  else if (kind == 'dress') { s.moveTo(-.12, 0); s.quadraticCurveTo(0, -.11, .12, 0); s.lineTo(.22, -.02); s.quadraticCurveTo(.3, -.08, .34, -.16); s.lineTo(.24, -.2); s.lineTo(.24, -.36);
    s.bezierCurveTo(.42, -.55, .52, -.8, .56, -1); s.quadraticCurveTo(.28, -1.04, 0, -1.02); s.quadraticCurveTo(-.28, -1.04, -.56, -1); s.bezierCurveTo(-.52, -.8, -.42, -.55, -.24, -.36); s.lineTo(-.24, -.2); s.lineTo(-.34, -.16); s.quadraticCurveTo(-.3, -.08, -.22, -.02); s.closePath(); }
  else if (kind == 'pants' || kind == 'jeans') { s.moveTo(-.3, 0); s.lineTo(.3, 0); s.quadraticCurveTo(.34, -.3, .35, -1); s.lineTo(.05, -1); s.lineTo(0, -.34); s.lineTo(-.05, -1); s.lineTo(-.35, -1); s.quadraticCurveTo(-.34, -.3, -.3, 0); }
  else { // onesie
    s.moveTo(-.13, 0); s.quadraticCurveTo(0, -.12, .13, 0); s.lineTo(.38, -.07); s.lineTo(.5, -.3); s.lineTo(.33, -.35); s.lineTo(.33, -.7); s.quadraticCurveTo(.3, -.9, .14, -1); s.lineTo(-.14, -1); s.quadraticCurveTo(-.3, -.9, -.33, -.7); s.lineTo(-.33, -.35); s.lineTo(-.5, -.3); s.lineTo(-.38, -.07); s.closePath(); }
  return s;
}
const TW = (window.__LT_QUALITY && window.__LT_QUALITY.mobile) ? 256 : 512, TH = TW;
const U = x => (x + .56) / 1.12 * TW, V = y => (-y + .03) / 1.08 * TH;
function pathOn(g, kind) { const pts = outline(kind).getPoints(24); g.beginPath(); pts.forEach((p, i) => i ? g.lineTo(U(p.x), V(p.y)) : g.moveTo(U(p.x), V(p.y))); g.closePath(); }

const garTexCache = {}, foldOv = {};
function foldOverlay(kind) { if (foldOv[kind]) return foldOv[kind];
  const [c, g] = mk(TW, TH); const img = g.createImageData(TW, TH); const d = img.data;
  for (let x = 0; x < TW; x++) { const nx = x / TW * 1.12 - .56; const f = foldZ(nx, -1, kind);
    for (let y = 0; y < TH; y++) { const k = Math.min(1, y / (TH * .35)); const i = (y * TW + x) * 4;
      if (f < 0) { d[i] = d[i + 1] = d[i + 2] = 0; d[i + 3] = 255 * Math.min(.22, -f * 3.2) * k; } else { d[i] = d[i + 1] = d[i + 2] = 255; d[i + 3] = 255 * Math.min(.12, f * 1.6) * k; } } }
  g.putImageData(img, 0, 0); return foldOv[kind] = c; }
function garmentTexture(kind, color, print) {
  const key = kind + color + print; if (garTexCache[key]) return garTexCache[key];
  const [c, g] = mk(TW, TH);
  g.save(); pathOn(g, kind); g.clip();
  const denim = kind == 'jeans';
  g.fillStyle = color; g.fillRect(0, 0, TW, TH);
  // prints
  if (print == 'stripe') { g.fillStyle = 'rgba(255,255,255,.75)'; for (let y = 0; y < TH; y += 22) g.fillRect(0, y, TW, 8); }
  if (print == 'gingham') { g.fillStyle = 'rgba(255,255,255,.45)'; for (let y = 0; y < TH; y += 18) g.fillRect(0, y, TW, 9); for (let x = 0; x < TW; x += 18) g.fillRect(x, 0, 9, TH); }
  if (print == 'floral') { for (let i = 0; i < 260; i++) { const x = rnd(0, TW), y = rnd(0, TH); g.fillStyle = ['#E98FA0', '#F4C7CF', '#FFFFFF', '#9CB98F', '#F2C46D'][i % 5]; for (let k = 0; k < 5; k++) { g.beginPath(); g.arc(x + Math.cos(k * 1.256) * 3.2, y + Math.sin(k * 1.256) * 3.2, 2.4, 0, 7); g.fill(); } g.fillStyle = '#F6D36B'; g.beginPath(); g.arc(x, y, 1.5, 0, 7); g.fill(); } }
  if (print == 'dots') { g.fillStyle = 'rgba(255,255,255,.85)'; for (let y = 0; y < TH; y += 20) for (let x = (y / 20 % 2) * 10; x < TW; x += 20) { g.beginPath(); g.arc(x, y, 3, 0, 7); g.fill(); } }
  if (denim) { for (let i = 0; i < 9000; i++) { g.fillStyle = `rgba(255,255,255,${rnd(0, .07)})`; g.fillRect(rnd(0, TW), rnd(0, TH), rnd(1, 4), 1); }
    const gr = g.createRadialGradient(U(-.18), V(-.45), 5, U(-.18), V(-.45), 90); gr.addColorStop(0, 'rgba(255,255,255,.22)'); gr.addColorStop(1, 'rgba(255,255,255,0)'); g.fillStyle = gr; g.fillRect(0, 0, TW, TH);
    const gr2 = g.createRadialGradient(U(.18), V(-.45), 5, U(.18), V(-.45), 90); gr2.addColorStop(0, 'rgba(255,255,255,.22)'); gr2.addColorStop(1, 'rgba(255,255,255,0)'); g.fillStyle = gr2; g.fillRect(0, 0, TW, TH);
    for (let k = 0; k < 4; k++) { g.strokeStyle = 'rgba(255,255,255,.18)'; g.lineWidth = 2; g.beginPath(); g.moveTo(U(-.05), V(-.18 - k * .03)); g.lineTo(U(-.2 + k * .02), V(-.24 - k * .04)); g.stroke(); g.beginPath(); g.moveTo(U(.05), V(-.18 - k * .03)); g.lineTo(U(.2 - k * .02), V(-.24 - k * .04)); g.stroke(); } }
  // vertical drape shading (matches geometry folds)
  g.drawImage(foldOverlay(kind), 0, 0);
  // details
  const dk = shade(color, -.12), st = denim ? '#D9A04B' : shade(color, -.18);
  g.lineWidth = 2.2; g.setLineDash([5, 4]); g.strokeStyle = st;
  if (kind == 'pants' || kind == 'jeans') { g.setLineDash([]); g.fillStyle = dk; g.fillRect(0, 0, TW, V(-.07)); g.setLineDash([5, 4]); g.beginPath(); g.moveTo(0, V(-.075)); g.lineTo(TW, V(-.075)); g.stroke();
    g.beginPath(); g.moveTo(U(-.02), V(-.07)); g.quadraticCurveTo(U(-.02), V(-.3), U(0), V(-.34)); g.stroke();
    g.beginPath(); g.moveTo(U(-.29), V(-.1)); g.quadraticCurveTo(U(-.2), V(-.2), U(-.12), V(-.08)); g.stroke(); g.beginPath(); g.moveTo(U(.29), V(-.1)); g.quadraticCurveTo(U(.2), V(-.2), U(.12), V(-.08)); g.stroke();
    g.beginPath(); g.moveTo(0, V(-.95)); g.lineTo(TW, V(-.95)); g.stroke();
    g.setLineDash([]); g.fillStyle = denim ? '#B98A4A' : shade(color, -.25); g.beginPath(); g.arc(U(0), V(-.035), 5, 0, 7); g.fill();
    for (const lx of [-.24, -.08, .08, .24]) { g.fillStyle = dk; g.fillRect(U(lx) - 3, 0, 6, V(-.09)); } }
  else {
    g.setLineDash([]); g.strokeStyle = dk; g.lineWidth = 9; g.beginPath(); g.moveTo(U(-.13), V(0)); g.quadraticCurveTo(U(0), V(-.13), U(.13), V(0)); g.stroke();
    g.lineWidth = 2; g.setLineDash([5, 4]); g.strokeStyle = st; g.beginPath(); g.moveTo(0, V(-.975)); g.lineTo(TW, V(-.975)); g.stroke();
    if (kind == 'dress') { g.setLineDash([]); g.fillStyle = shade(color, -.08); g.fillRect(0, V(-.2), TW, V(-.36) - V(-.2)); // smocked bodice
      g.strokeStyle = 'rgba(255,255,255,.55)'; g.lineWidth = 1.5; for (let y = V(-.22); y < V(-.34); y += 6) { g.beginPath(); for (let x = 0; x < TW; x += 8) g.lineTo(x, y + (x / 8 % 2) * 3); g.stroke(); }
      g.fillStyle = '#FFFFFF'; g.beginPath(); g.ellipse(U(-.07), V(-.03), 30, 13, -.3, 0, 7); g.ellipse(U(.07), V(-.03), 30, 13, .3, 0, 7); g.fill(); }
    if (kind == 'cardigan' || kind == 'jacket') { g.setLineDash([]); g.strokeStyle = dk; g.lineWidth = 3; g.beginPath(); g.moveTo(U(0), V(-.1)); g.lineTo(U(0), V(-1)); g.stroke();
      for (let k = 0; k < 5; k++) { g.fillStyle = kind == 'jacket' ? '#3A3A3A' : '#F3EEE5'; g.beginPath(); g.arc(U(.025), V(-.2 - k * .16), 6, 0, 7); g.fill(); g.fillStyle = 'rgba(0,0,0,.25)'; g.beginPath(); g.arc(U(.025) + 1, V(-.2 - k * .16) + 1.5, 6, 0, 7); g.fill(); }
      if (kind == 'jacket') { g.strokeStyle = dk; g.lineWidth = 2; g.strokeRect(U(-.3), V(-.6), U(-.1) - U(-.3), V(-.75) - V(-.6)); g.strokeRect(U(.1), V(-.6), U(.3) - U(.1), V(-.75) - V(-.6)); } 
      g.lineWidth = 12; g.strokeStyle = dk; g.beginPath(); g.moveTo(U(-.48), V(-.86)); g.lineTo(U(-.39), V(-.88)); g.stroke(); g.beginPath(); g.moveTo(U(.48), V(-.86)); g.lineTo(U(.39), V(-.88)); g.stroke(); }
    if (kind == 'onesie') { g.setLineDash([]); for (let k = 0; k < 3; k++) { g.fillStyle = '#FFFFFF'; g.beginPath(); g.arc(U(0), V(-.12 - k * .08), 4, 0, 7); g.fill(); } }
    if (kind == 'tee' && print == 'plain') { g.setLineDash([]); g.fillStyle = 'rgba(255,255,255,.85)'; g.font = '600 34px Inter, Arial'; g.textAlign = 'center'; g.fillText(['LT', 'KIDS', '★', 'PLAY'][Math.floor(rnd(0, 4))], U(0), V(-.42)); }
  }
  // edge darkening for volume
  g.restore(); g.save(); pathOn(g, kind); g.clip(); g.lineWidth = 16; g.strokeStyle = 'rgba(0,0,0,.12)'; pathOn(g, kind); g.stroke(); g.restore();
  const t = tex(c); return garTexCache[key] = t;
}
// fold displacement (normalized coords) – deeper toward the hem
function foldZ(nx, ny, kind) {
  const d = Math.min(1, Math.max(0, -ny));
  const f = Math.sin(nx * 21 + 1.3) * .5 + Math.sin(nx * 9.7 + .4) * .7 + Math.sin(nx * 37) * .18;
  return f * .012 * (.25 + d * (kind == 'dress' ? 1.6 : 1));
}
const gGeoCache = {};
function garmentGeo(kind) {
  if (gGeoCache[kind]) return gGeoCache[kind];
  const geo = new THREE.PlaneGeometry(1.12, 1.08, 18, 24); const p = geo.attributes.position;
  for (let i = 0; i < p.count; i++) { const x = p.getX(i), y = p.getY(i) - .51; const nx = x, ny = y;
    let z = foldZ(nx, ny, kind) * 1.0 + Math.cos(Math.min(1, Math.abs(nx) / .4) * Math.PI / 2) * .035 * (ny > -.9 ? 1 : .6);
    p.setXYZ(i, x, y, z); }
  geo.computeVertexNormals(); return gGeoCache[kind] = geo;
}
const edgeCache = {}, edgeMatCache = {};
function edgeGeo(kind) { if (edgeCache[kind]) return edgeCache[kind];
  const g = new THREE.ExtrudeGeometry(outline(kind), { depth: .03, bevelEnabled: true, bevelThickness: .012, bevelSize: .006, bevelSegments: 1, curveSegments: 6 });
  g.scale(.985, .985, 1); g.translate(0, -.005, -.042); return edgeCache[kind] = g; }
function edgeMat(color) { return edgeMatCache[color] || (edgeMatCache[color] = new THREE.MeshStandardMaterial({ color: shade(color, -.06), roughness: .95, normalMap: weaveNormal('cotton', [6, 6]), normalScale: new THREE.Vector2(.4, .4) })); }
const fabricMatCache = {};
export function garmentMaterial(kind, color, print = 'plain') {
  const key = kind + color + print; if (fabricMatCache[key]) return fabricMatCache[key];
  const knit = kind == 'cardigan', denim = kind == 'jeans';
  return fabricMatCache[key] = new THREE.MeshStandardMaterial({ map: garmentTexture(kind, color, print), normalMap: weaveNormal(knit ? 'knit' : denim ? 'denim' : 'cotton', knit ? [10, 10] : [14, 14]), normalScale: new THREE.Vector2(.55, .55),
    alphaTest: .5, transparent: false, side: THREE.DoubleSide, roughness: .92 });
}
const hangerMat = new THREE.MeshStandardMaterial({ color: '#C49A6C', roughness: .45 });
const hookMat = new THREE.MeshStandardMaterial({ color: '#D8DBE0', roughness: .2, metalness: 1 });
export function garment(kind, color, print, parent, x, y, z, rotY, scale = .55) {
  const g = new THREE.Group();
  const sx = scale * .9, sy = scale * (kind == 'dress' ? 1.25 : kind == 'pants' || kind == 'jeans' ? 1.18 : 1);
  const m = new THREE.Mesh(garmentGeo(kind), garmentMaterial(kind, color, print)); m.scale.set(sx, sy, 1); m.position.y = -.035; m.castShadow = true; m.receiveShadow = true; g.add(m);
  const e = new THREE.Mesh(edgeGeo(kind), edgeMat(color)); e.scale.set(sx, sy, 1); e.position.set(0, -.035, -.004); e.castShadow = true; g.add(e);
  // wooden hanger (two angled arms) + chrome hook
  const pants = kind == 'pants' || kind == 'jeans';
  if (pants) { const bar = new THREE.Mesh(new THREE.BoxGeometry(sx * .75, .025, .02), hangerMat); bar.position.y = -.02; g.add(bar); }
  else for (const s of [-1, 1]) { const arm = new THREE.Mesh(new THREE.BoxGeometry(sx * .52, .03, .022), hangerMat); arm.position.set(s * sx * .22, -.045, .0); arm.rotation.z = -s * .2; g.add(arm); }
  const hook = new THREE.Mesh(new THREE.TorusGeometry(.03, .004, 6, 16, Math.PI * 1.25), hookMat); hook.position.y = .02; hook.rotation.z = -.3; g.add(hook);
  const neck = new THREE.Mesh(new THREE.CylinderGeometry(.004, .004, .04, 6), hookMat); neck.position.y = -.01; g.add(neck);
  g.position.set(x, y, z); g.rotation.y = rotY; parent.add(g); return g;
}

/* ---------- folded clothes ---------- */
const foldCache = {};
export function foldedMaterial(color, print = 'plain') {
  const key = color + print; if (foldCache[key]) return foldCache[key];
  const [c, g] = mk(256, 256); g.fillStyle = color; g.fillRect(0, 0, 256, 256);
  if (print == 'stripe') { g.fillStyle = 'rgba(255,255,255,.7)'; for (let y = 0; y < 256; y += 20) g.fillRect(0, y, 256, 7); }
  if (print == 'gingham') { g.fillStyle = 'rgba(255,255,255,.4)'; for (let y = 0; y < 256; y += 16) g.fillRect(0, y, 256, 8); for (let x = 0; x < 256; x += 16) g.fillRect(x, 0, 8, 256); }
  g.strokeStyle = 'rgba(0,0,0,.18)'; g.lineWidth = 3; g.beginPath(); g.moveTo(128, 0); g.lineTo(128, 256); g.stroke();
  g.strokeStyle = 'rgba(255,255,255,.25)'; g.lineWidth = 2; g.beginPath(); g.moveTo(132, 0); g.lineTo(132, 256); g.stroke();
  for (let i = 0; i < 800; i++) { g.fillStyle = `rgba(0,0,0,${rnd(0, .04)})`; g.fillRect(rnd(0, 256), rnd(0, 256), 3, 1); }
  return foldCache[key] = new THREE.MeshStandardMaterial({ map: tex(c), normalMap: weaveNormal('cotton', [4, 4]), normalScale: new THREE.Vector2(.5, .5), roughness: .95 });
}

/* ---------- accessories ---------- */
const _mc = {}; const memo = (k, f) => _mc[k] || (_mc[k] = f());
export function knitMaterial(color) { return memo('k' + color, () => new THREE.MeshStandardMaterial({ color, normalMap: weaveNormal('knit', [8, 4]), normalScale: new THREE.Vector2(1.2, 1.2), roughness: 1 })); }
export function strawMaterial(color = '#D9BE8C') { return memo('s' + color, () => new THREE.MeshStandardMaterial({ color, normalMap: weaveNormal('straw', [16, 4]), normalScale: new THREE.Vector2(1, 1), roughness: .9 })); }
export function twillMaterial(color) { return memo('t' + color, () => new THREE.MeshStandardMaterial({ color, normalMap: weaveNormal('twill', [8, 8]), normalScale: new THREE.Vector2(.6, .6), roughness: .9 })); }

/* ---------- foliage ---------- */
function leafCardTexture(style) {
  const [c, g] = mk(256, 256);
  const N = style == 'box' ? 40 : style == 'olive' ? 16 : 14;
  for (let i = 0; i < N; i++) {
    const x = rnd(40, 216), y = rnd(40, 216), a = rnd(0, Math.PI * 2);
    const L = style == 'box' ? rnd(14, 22) : style == 'olive' ? rnd(44, 60) : rnd(40, 58), W = style == 'olive' ? L * .18 : L * .55;
    g.save(); g.translate(x, y); g.rotate(a);
    const hue = style == 'olive' ? rnd(78, 95) : rnd(88, 115), lig = rnd(24, 42), sat = style == 'olive' ? rnd(14, 24) : rnd(30, 48);
    const gr = g.createLinearGradient(0, -W, 0, W); gr.addColorStop(0, `hsl(${hue},${sat}%,${lig + 10}%)`); gr.addColorStop(1, `hsl(${hue},${sat}%,${lig - 6}%)`);
    g.fillStyle = gr; g.beginPath(); g.moveTo(-L / 2, 0); g.bezierCurveTo(-L / 4, -W, L / 4, -W, L / 2, 0); g.bezierCurveTo(L / 4, W, -L / 4, W, -L / 2, 0); g.fill();
    g.strokeStyle = `hsla(${hue},${sat}%,${lig + 18}%,.6)`; g.lineWidth = 1; g.beginPath(); g.moveTo(-L / 2, 0); g.lineTo(L / 2, 0); g.stroke();
    if (style != 'box') { g.strokeStyle = `hsl(30,25%,28%)`; g.lineWidth = 1.5; g.beginPath(); g.moveTo(-L / 2, 0); g.lineTo(-L / 2 - 10, 2); g.stroke(); }
    g.restore(); }
  const t = tex(c); return t;
}
const leafTex = {}; const leafMats = {};
function leafMaterial(style) {
  if (leafMats[style]) return leafMats[style];
  leafTex[style] = leafCardTexture(style);
  const m = new THREE.MeshStandardMaterial({ map: leafTex[style], alphaTest: .5, side: THREE.DoubleSide, roughness: .75, metalness: 0 });
  const d = new THREE.MeshDepthMaterial({ depthPacking: THREE.RGBADepthPacking, map: leafTex[style], alphaTest: .5 });
  return leafMats[style] = [m, d];
}
// distribute leaf cards around cluster centers
export function foliage(parent, clusters, { style = 'tree', count = 1400, size = .55, tint = ['#7BA36B', '#5E8A50', '#90B07A'] } = {}) {
  const [mat, dmat] = leafMaterial(style);
  const geo = new THREE.PlaneGeometry(size, size);
  const im = new THREE.InstancedMesh(geo, mat, count); im.customDepthMaterial = dmat; im.castShadow = true; im.receiveShadow = true;
  const o = new THREE.Object3D(), col = new THREE.Color();
  for (let i = 0; i < count; i++) {
    const c = clusters[i % clusters.length]; const [cx, cy, cz, r] = c;
    const u = Math.random(), v = Math.random(); const th = u * Math.PI * 2, ph = Math.acos(2 * v - 1); const rr = r * Math.pow(Math.random(), .35);
    o.position.set(cx + rr * Math.sin(ph) * Math.cos(th), cy + rr * Math.cos(ph) * .85, cz + rr * Math.sin(ph) * Math.sin(th));
    o.lookAt(cx + (o.position.x - cx) * 3, cy + (o.position.y - cy) * 3 + rnd(-.3, .3), cz + (o.position.z - cz) * 3);
    o.rotateZ(rnd(0, Math.PI * 2)); o.rotateX(rnd(-.5, .5)); const s = rnd(.75, 1.25); o.scale.set(s, s, s); o.updateMatrix(); im.setMatrixAt(i, o.matrix);
    const outer = rr / r; col.set(tint[Math.floor(Math.random() * tint.length)]).multiplyScalar(.7 + outer * .45); im.setColorAt(i, col); }
  parent.add(im); return im;
}
// bark texture
let barkMat;
function bark() { if (barkMat) return barkMat; const [c, g] = mk(256, 512); g.fillStyle = '#6E5B4B'; g.fillRect(0, 0, 256, 512);
  for (let i = 0; i < 160; i++) { g.strokeStyle = `rgba(${rnd() < .5 ? '40,30,22' : '150,130,110'},${rnd(.15, .4)})`; g.lineWidth = rnd(1, 4); g.beginPath(); const x = rnd(0, 256); g.moveTo(x, 0); for (let y = 0; y < 512; y += 32) g.lineTo(x + rnd(-6, 6), y); g.stroke(); }
  const hc = document.createElement('canvas'); hc.width = 256; hc.height = 512; hc.getContext('2d').drawImage(c, 0, 0);
  return barkMat = new THREE.MeshStandardMaterial({ map: tex(c, true, [2, 2]), normalMap: heightToNormal(hc, 3, [2, 2]), roughness: .95 }); }
function limb(parent, a, b, r0, r1) { const dir = new THREE.Vector3().subVectors(b, a); const len = dir.length();
  const m = new THREE.Mesh(new THREE.CylinderGeometry(r1, r0, len, 10, 4), bark()); m.position.copy(a).addScaledVector(dir, .5);
  m.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), dir.normalize()); m.castShadow = true; parent.add(m); }
export function realTree(parent, x, z, h = 6.2) {
  const g = new THREE.Group(); g.position.set(x, 0, z); parent.add(g);
  const top = new THREE.Vector3(rnd(-.15, .15), h * .55, rnd(-.15, .15)); limb(g, new THREE.Vector3(0, 0, 0), top, .2, .13);
  const clusters = [];
  for (let i = 0; i < 7; i++) { const a = i / 7 * Math.PI * 2 + rnd(-.3, .3); const start = new THREE.Vector3(top.x * .9, h * rnd(.42, .55), top.z * .9);
    const end = new THREE.Vector3(Math.cos(a) * rnd(1, 1.7), h * rnd(.68, .92), Math.sin(a) * rnd(1, 1.7)); limb(g, start, end, .09, .035);
    clusters.push([end.x, end.y + .2, end.z, rnd(.9, 1.2)]); }
  limb(g, top, new THREE.Vector3(0, h * .95, 0), .1, .04); clusters.push([0, h * .95, 0, 1.3], [0, h * .78, 0, 1.6]);
  foliage(g, clusters, { style: 'tree', count: 2600, size: .6, tint: ['#9CC27F', '#7FAE66', '#B2CF8E', '#8BB873'] });
  return g;
}
export function boxwoodBall(parent, x, y, z, r) {
  const core = new THREE.Mesh(new THREE.SphereGeometry(r * .9, 24, 16), new THREE.MeshStandardMaterial({ color: '#46663A', roughness: 1 })); core.position.set(x, y, z); core.castShadow = true; parent.add(core);
  const N = Math.floor(600 * r * r * 4);
  const [mat, dmat] = leafMaterial('box'); const im = new THREE.InstancedMesh(new THREE.PlaneGeometry(.3, .3), mat, N); im.customDepthMaterial = dmat; im.castShadow = true;
  const o = new THREE.Object3D(), col = new THREE.Color();
  for (let i = 0; i < N; i++) { const v = new THREE.Vector3().randomDirection(); const rr = r * rnd(.86, 1.02); o.position.set(x + v.x * rr, y + v.y * rr, z + v.z * rr);
    o.lookAt(x + v.x * 9, y + v.y * 9, z + v.z * 9); o.rotateZ(rnd(0, 6.28)); o.rotateX(rnd(-.6, .6)); o.updateMatrix(); im.setMatrixAt(i, o.matrix);
    col.set(['#8DBA6E', '#7AAA5E', '#A2C47E'][i % 3]).multiplyScalar(.85 + Math.max(0, v.y) * .35); im.setColorAt(i, col); }
  parent.add(im);
}
export function oliveTree(parent, x, y, z, h = 1.6) {
  const g = new THREE.Group(); g.position.set(x, y, z); parent.add(g);
  limb(g, new THREE.Vector3(0, 0, 0), new THREE.Vector3(.05, h * .6, 0), .035, .025);
  const cl = []; for (let i = 0; i < 5; i++) { const a = i / 5 * 6.28; const e = new THREE.Vector3(Math.cos(a) * .3, h * rnd(.75, .95), Math.sin(a) * .3); limb(g, new THREE.Vector3(.05, h * .55, 0), e, .015, .008); cl.push([e.x, e.y + .08, e.z, .32]); }
  cl.push([0, h, 0, .38]);
  foliage(g, cl, { style: 'olive', count: 520, size: .3, tint: ['#A9B98A', '#93A677', '#BCC99D'] });
  return g;
}

/* ---------- detailed accessories (return groups, origin at base center) ---------- */
function add(g, geo, mat, x, y, z, rx = 0, ry = 0, rz = 0, sx = 1, sy = 1, sz = 1) { const m = new THREE.Mesh(geo, mat); m.position.set(x, y, z); m.rotation.set(rx, ry, rz); m.scale.set(sx, sy, sz); m.castShadow = true; m.receiveShadow = true; g.add(m); return m; }
const white = new THREE.MeshStandardMaterial({ color: '#F6F4F0', roughness: .7 });
const rubber = new THREE.MeshStandardMaterial({ color: '#EDEAE4', roughness: .85 });
import { RoundedBoxGeometry } from 'three/addons/geometries/RoundedBoxGeometry.js';
const RB = (w, h, d, r) => new RoundedBoxGeometry(w, h, d, 3, r);
export function cap(color) { const g = new THREE.Group(), m = twillMaterial(color);
  add(g, new THREE.SphereGeometry(.105, 20, 10, 0, Math.PI * 2, 0, Math.PI / 2), m, 0, 0, 0, 0, 0, 0, 1, .85, 1.08);
  const visor = add(g, new THREE.CylinderGeometry(.11, .11, .008, 32, 1, false, -Math.PI / 2, Math.PI), m, 0, .006, .07, -.18, 0, 0, 1, 1, .95);
  add(g, new THREE.SphereGeometry(.012, 12, 8), m, 0, .09, 0);
  for (let k = 0; k < 6; k++) add(g, new THREE.BoxGeometry(.002, .085, .002), memo('seam' + color, () => new THREE.MeshStandardMaterial({ color: shade(color, -.15) })), Math.sin(k / 6 * 6.28) * .07, .045, Math.cos(k / 6 * 6.28) * .07, 0, k / 6 * 6.28, 0);
  add(g, new THREE.CircleGeometry(.025, 24), memo('logo', () => new THREE.MeshStandardMaterial({ color: '#FFFFFF', roughness: .8 })), 0, .05, .088, -.35);
  return g; }
export function beanie(color) { const g = new THREE.Group(), m = knitMaterial(color);
  add(g, new THREE.SphereGeometry(.1, 20, 12, 0, Math.PI * 2, 0, Math.PI / 2), m, 0, .055, 0, 0, 0, 0, 1, 1.25, 1);
  add(g, new THREE.CylinderGeometry(.104, .106, .06, 32, 1, true), m, 0, .03, 0); add(g, new THREE.CylinderGeometry(.106, .106, .005, 32), m, 0, .002, 0);
  const pg = new THREE.IcosahedronGeometry(.045, 2); const p = pg.attributes.position; for (let i = 0; i < p.count; i++) { const v = new THREE.Vector3().fromBufferAttribute(p, i); v.multiplyScalar(1 + (Math.random() - .5) * .25); p.setXYZ(i, v.x, v.y, v.z); } pg.computeVertexNormals();
  add(g, pg, memo('pom' + color, () => new THREE.MeshStandardMaterial({ color: shade(color, .12), roughness: 1 })), 0, .2, 0);
  return g; }
export function sunHat(color = '#D9BE8C', ribbon = '#F2C6C2') { const g = new THREE.Group(), m = strawMaterial(color);
  const pts = []; for (let i = 0; i <= 12; i++) { const r = .09 + i / 12 * .13; pts.push(new THREE.Vector2(r, .02 - Math.pow(i / 12, 2) * .03)); }
  const brim = new THREE.LatheGeometry(pts, 32); const bp = brim.attributes.position; for (let i = 0; i < bp.count; i++) { const x = bp.getX(i), z = bp.getZ(i); const a = Math.atan2(z, x), r = Math.hypot(x, z); bp.setY(i, bp.getY(i) + Math.sin(a * 5) * .008 * (r / .22)); } brim.computeVertexNormals();
  add(g, brim, memo('brim' + color, () => new THREE.MeshStandardMaterial({ color, normalMap: m.normalMap, roughness: .9, side: THREE.DoubleSide })), 0, 0, 0);
  add(g, new THREE.CylinderGeometry(.085, .095, .08, 40), m, 0, .06, 0); add(g, new THREE.SphereGeometry(.085, 32, 12, 0, Math.PI * 2, 0, Math.PI / 2), m, 0, .1, 0, 0, 0, 0, 1, .45, 1);
  const rib = memo('rib' + ribbon, () => new THREE.MeshStandardMaterial({ color: ribbon, roughness: .6 })); add(g, new THREE.CylinderGeometry(.097, .097, .025, 40, 1, true), rib, 0, .035, 0);
  add(g, RB(.06, .03, .012, .006), rib, -.03, .04, .095, 0, 0, .4); add(g, RB(.06, .03, .012, .006), rib, .03, .04, .095, 0, 0, -.4); add(g, new THREE.SphereGeometry(.012, 12, 8), rib, 0, .04, .1);
  return g; }
export function sneakers(color, accent = '#FFFFFF') { const g = new THREE.Group(); const up = memo('up' + color, () => new THREE.MeshStandardMaterial({ color, roughness: .75, normalMap: weaveNormal('twill', [4, 4]), normalScale: new THREE.Vector2(.5, .5) }));
  for (const [dx, dz, ry] of [[-.055, 0, .08], [.055, .02, -.05]]) { const s = new THREE.Group(); s.position.set(dx, 0, dz); s.rotation.y = ry; g.add(s);
    add(s, RB(.085, .03, .2, .014), rubber, 0, .015, 0);
    add(s, RB(.078, .07, .13, .03), up, 0, .06, -.03); add(s, new THREE.SphereGeometry(.039, 24, 16, 0, Math.PI * 2, 0, Math.PI / 2), up, 0, .03, .045, 0, 0, 0, 1, 1.1, 1.45);
    add(s, new THREE.TorusGeometry(.03, .008, 8, 24), memo('col' + color, () => new THREE.MeshStandardMaterial({ color: shade(color, -.2), roughness: .8 })), 0, .095, -.05, Math.PI / 2 - .2, 0, 0, 1.1, 1.3, 1);
    for (let k = 0; k < 4; k++) add(s, new THREE.BoxGeometry(.05, .006, .008), memo('acc' + accent, () => new THREE.MeshStandardMaterial({ color: accent, roughness: .7 })), 0, .093 - k * .006, .0 + k * .022, -.35);
    add(s, new THREE.BoxGeometry(.003, .03, .07), memo('acc' + accent, () => new THREE.MeshStandardMaterial({ color: accent, roughness: .7 })), .04, .055, -.02); add(s, new THREE.BoxGeometry(.003, .03, .07), memo('acc' + accent, () => new THREE.MeshStandardMaterial({ color: accent, roughness: .7 })), -.04, .055, -.02); }
  return g; }
export function backpack(color, accent) { const g = new THREE.Group(); const m = memo('bp' + color, () => new THREE.MeshStandardMaterial({ color, roughness: .8, normalMap: weaveNormal('twill', [6, 6]), normalScale: new THREE.Vector2(.5, .5) }));
  add(g, RB(.24, .3, .11, .05), m, 0, .15, 0); add(g, RB(.18, .13, .045, .025), memo('bpa' + (accent || color), () => new THREE.MeshStandardMaterial({ color: accent || shade(color, .1), roughness: .8 })), 0, .1, .065);
  add(g, new THREE.TorusGeometry(.035, .008, 8, 20, Math.PI), m, 0, .3, -.01);
  const zip = memo('zip', () => new THREE.MeshStandardMaterial({ color: '#2C2F35', roughness: .4, metalness: .5 })); add(g, new THREE.BoxGeometry(.16, .005, .004), zip, 0, .168, .089); add(g, RB(.012, .02, .006, .003), zip, .06, .16, .092);
  add(g, RB(.2, .04, .03, .012), m, 0, .27, .02, -.3);
  return g; }
