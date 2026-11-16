/*  Little Threads — stylised-realistic staff figures (procedural, no external assets)
    person(parent, x, y, z, rotationY, options)
    options: pose 'sit'|'stand'|'talk'|'tablet'|'pack'|'carry'|'coffee', female, skin, hair, hairStyle
             ('short'|'side'|'long'|'pony'|'bun'|'curly'|'bald'), top ('shirt'|'tee'|'sweater'|'blouse'),
             shirt, pants, blazer, skirt, glasses, lanyard, headset, shoes ('sneaker'|'leather'), look (head turn)   */
import * as THREE from 'three';
import { RoundedBoxGeometry } from 'three/addons/geometries/RoundedBoxGeometry.js';
import { twillMaterial, knitMaterial, heightToNormal, sneakers } from './realism.js';

const V2 = (r, y) => new THREE.Vector2(r, y);
const _m = {}; const mat = (k, f) => _m[k] || (_m[k] = f());
const std = (c, o = {}) => mat('p' + c + JSON.stringify(o), () => new THREE.MeshStandardMaterial({ color: c, roughness: .7, ...o }));
function add(geo, m, x, y, z, parent, o = {}) { const ms = new THREE.Mesh(geo, m); ms.position.set(x, y, z); ms.rotation.set(o.rx || 0, o.ry || 0, o.rz || 0); if (o.s) ms.scale.set(...o.s); ms.castShadow = o.cast !== false; ms.receiveShadow = true; parent.add(ms); return ms; }
const lathe = (pts, seg = 22) => new THREE.LatheGeometry(pts.map(p => V2(p[0], p[1])), seg);
const rnd = (a, b) => a + Math.random() * (b - a);

let _strands;
function hairMat(color) {
  if (!_strands) { const c = document.createElement('canvas'); c.width = 128; c.height = 128; const g = c.getContext('2d'); g.fillStyle = '#808080'; g.fillRect(0, 0, 128, 128);
    for (let i = 0; i < 400; i++) { g.strokeStyle = `rgba(${Math.random() < .5 ? 255 : 0},${Math.random() < .5 ? 255 : 0},128,.35)`; g.lineWidth = 1; const x = Math.random() * 128; g.beginPath(); g.moveTo(x, 0); g.bezierCurveTo(x + 4, 40, x - 4, 90, x + 2, 128); g.stroke(); }
    _strands = heightToNormal(c, 2.5, [3, 2]); }
  return mat('hair' + color, () => new THREE.MeshStandardMaterial({ color, roughness: .5, normalMap: _strands, normalScale: new THREE.Vector2(.8, .8) }));
}
const skinMat = c => mat('skin' + c, () => new THREE.MeshStandardMaterial({ color: c, roughness: .55, emissive: c, emissiveIntensity: .06 }));
const shade = (hex, f) => { const c = new THREE.Color(hex); const h = {}; c.getHSL(h); c.setHSL(h.h, h.s, Math.max(0, Math.min(1, h.l + f))); return '#' + c.getHexString(); };

/* displaced hair shell (sphere cap) for volume and irregular silhouette */
function hairShell(r, thetaLen, bulge = .06, seed = 1, phiStart = 0, phiLen = Math.PI * 2) {
  const g = new THREE.SphereGeometry(r, 36, 22, phiStart, phiLen, 0, thetaLen); const p = g.attributes.position;
  for (let i = 0; i < p.count; i++) { const v = new THREE.Vector3().fromBufferAttribute(p, i); const n = Math.sin(v.x * 70 + seed) * Math.sin(v.z * 60 + seed * 2) * .5 + Math.sin(v.y * 45 + seed) * .5;
    v.multiplyScalar(1 + bulge * .5 + n * bulge * .35); p.setXYZ(i, v.x, v.y, v.z); }
  g.computeVertexNormals(); return g;
}

function head(parent, o, skin, hairC) {
  const h = new THREE.Group(); parent.add(h);
  const sk = skinMat(skin), lip = skinMat(shade(skin, -.12)), brow = hairMat(hairC);
  // skull + jaw
  add(new THREE.SphereGeometry(.095, 32, 24), sk, 0, .012, 0, h, { s: [.9, 1.06, 1] });
  add(new THREE.SphereGeometry(.078, 28, 20), sk, 0, -.035, .014, h, { s: [o.female ? .88 : .95, .92, 1] });
  // nose, lips, eyes, brows, ears
  add(new THREE.SphereGeometry(.016, 14, 10), sk, 0, -.008, .094, h, { s: [.75, 1.35, 1.1], rx: -.25 });
  add(new THREE.SphereGeometry(.019, 14, 8), lip, 0, -.05, .084, h, { s: [1.25, .32, .55] });
  for (const s of [-1, 1]) {
    add(new THREE.SphereGeometry(.0105, 12, 8), std('#2A221E', { roughness: .25 }), s * .032, .016, .083, h);
    add(new RoundedBoxGeometry(.03, .006, .008, 1, .002), brow, s * .033, .035, .088, h, { rz: s * -.12 });
    add(new THREE.SphereGeometry(.022, 12, 10), sk, s * .087, .004, -.004, h, { s: [.42, 1, .72] });
  }
  // hair
  const hm = hairMat(hairC), st = o.hairStyle || (o.female ? 'long' : 'short');
  if (st !== 'bald') {
    if (st === 'curly') { add(hairShell(.099, Math.PI * .5, .02), hm, 0, .016, -.01, h, { s: [.95, 1.05, 1.03], rx: -.5 }); for (let i = 0; i < 46; i++) { const a = Math.random() * Math.PI * 2, b = Math.random() * Math.PI * .45; if (Math.sin(a) > .55 && b > .25) continue; add(new THREE.SphereGeometry(.022, 8, 6), hm, Math.sin(b) * Math.cos(a) * .1, .02 + Math.cos(b) * .105, Math.sin(b) * Math.sin(a) * .1 - .01, h); } }
    else add(hairShell(.1, Math.PI * .5, st === 'short' ? .05 : .08, o.seed || 1), hm, 0, .014, -.012, h, { s: [.96, 1.08, 1.05], rx: -.5 });
    // back of the head / nape
    add(hairShell(.099, Math.PI * .72, .04, (o.seed || 1) + 2, Math.PI * 1.08, Math.PI * .84), hm, 0, .01, -.006, h, { s: [.95, 1.05, 1.04] });
    if (st === 'side') add(new THREE.SphereGeometry(.06, 16, 12), hm, .03, .085, .055, h, { s: [1.3, .45, .8], rz: -.3 });
    if (st === 'long') { const back = hairShell(.105, Math.PI * .8, .06, 3, Math.PI * 1.02, Math.PI * .96); add(back, hm, 0, -.02, -.012, h, { s: [.99, 1.35, 1.02] }); add(new THREE.CapsuleGeometry(.07, .12, 6, 12), hm, 0, -.13, -.06, h, { s: [1.25, 1, .55] }); }
    if (st === 'pony') { add(new THREE.CapsuleGeometry(.03, .16, 6, 10), hm, 0, -.05, -.12, h, { rx: .35 }); add(new THREE.TorusGeometry(.022, .006, 6, 14), std('#1E2A44'), 0, .045, -.1, h, { rx: 1.2 }); }
    if (st === 'bun') add(new THREE.SphereGeometry(.045, 16, 12), hm, 0, .1, -.075, h, { s: [1, .85, 1] });
  }
  if (o.glasses) { const gm = std('#1E1E22', { roughness: .3, metalness: .4 }); for (const s of [-1, 1]) { add(new THREE.TorusGeometry(.019, .0025, 6, 20), gm, s * .033, .016, .1, h); add(new THREE.BoxGeometry(.003, .003, .1), gm, s * .056, .02, .052, h); } add(new THREE.BoxGeometry(.022, .003, .003), gm, 0, .02, .101, h); }
  if (o.headset) { const hs = std('#2B2F36', { roughness: .4 }); add(new THREE.TorusGeometry(.105, .008, 8, 24, Math.PI), hs, 0, .02, 0, h, { s: [1, 1.05, 1] }); for (const s of [-1, 1]) add(new THREE.CylinderGeometry(.03, .03, .025, 16), hs, s * .1, .0, 0, h, { rz: Math.PI / 2 }); add(new THREE.BoxGeometry(.004, .004, .08), hs, -.09, -.04, .05, h, { ry: .4 }); }
  return h;
}

/* arm: shoulder group → upper arm → forearm → hand */
function arm(parent, side, top, sleeveLong, skin, o) {
  const sh = new THREE.Group(); parent.add(sh);
  const sk = skinMat(skin);
  add(lathe([[0, 0], [.04, -.005], [.05, -.04], [.046, -.16], [.04, -.29]]), top, 0, 0, 0, sh);
  const el = new THREE.Group(); el.position.y = -.29; sh.add(el);
  add(new THREE.SphereGeometry(.041, 14, 10), sleeveLong ? top : skinMat(skin), 0, 0, 0, el);
  add(lathe([[.04, 0], [.039, -.08], [.031, -.25], [0, -.255]]), sleeveLong ? top : sk, 0, 0, 0, el);
  if (!sleeveLong && o.rolled) add(new THREE.TorusGeometry(.042, .012, 8, 18), top, 0, -.03, 0, el, { rx: Math.PI / 2 });
  const hand = new THREE.Group(); hand.position.y = -.26; el.add(hand);
  add(new THREE.SphereGeometry(.04, 16, 12), sk, 0, -.04, 0, hand, { s: [.62, 1.15, .95] });
  add(new THREE.CapsuleGeometry(.011, .035, 4, 8), sk, side * -.022, -.03, .02, hand, { rz: side * .5, rx: .4 });
  if (o.watch && side < 0) add(new THREE.TorusGeometry(.033, .006, 6, 16), std('#2B2F36', { metalness: .6, roughness: .3 }), 0, -.23, 0, el, { rx: Math.PI / 2 });
  return { sh, el, hand };
}

export function person(parent, x, y, z, ry, o = {}) {
  const pose = o.pose || 'stand', sit = pose === 'sit', F = !!o.female;
  const skin = o.skin || '#D9A784', hairC = o.hair || '#2B211C';
  const topKind = o.top || 'shirt';
  const topM = topKind === 'sweater' ? knitMaterial(o.shirt || '#E9E4D8') : twillMaterial(o.shirt || '#E9E4D8');
  const pantsM = twillMaterial(o.pants || '#2E3E57');
  const g = new THREE.Group(); g.position.set(x, y, z); g.rotation.y = ry; parent.add(g);
  const S = F ? .955 : 1; g.scale.setScalar(S * (o.scale || 1));

  const hipY = sit ? .5 : .9;
  /* ---- legs ---- */
  const shinP = [[.043, 0], [.052, .1], [.056, .17], [.046, .36], [.05, .41]];
  const thighP = [[.05, 0], [.062, .08], [.075, .25], [.085, .4]];
  const pad = (P, d) => P.map(([r, yy]) => [r + d, yy]);
  for (const s of [-1, 1]) {
    const lx = s * (F ? .085 : .095);
    const shinM = o.skirt ? std(F ? shade(skin, -.05) : skin) : pantsM;
    const shinG = o.skirt ? lathe(shinP) : lathe(pad([[.06, 0], [.062, .2], [.064, .41]], 0));
    if (sit) {
      add(lathe(o.skirt ? thighP : pad(thighP, .012)), o.skirt ? std(skin) : pantsM, lx, hipY, .02, g, { rx: Math.PI / 2 }).scale.set(1, 1.05, .92);
      add(shinG, shinM, lx, .08, .44, g); if (!o.skirt) add(new THREE.SphereGeometry(.066, 14, 10), pantsM, lx, .49, .43, g);
      shoe(g, lx, .44, o);
    } else {
      const step = pose === 'carry' ? s * .14 : (pose === 'coffee' && s > 0 ? -.08 : 0);
      const leg = new THREE.Group(); leg.position.set(lx, hipY, 0); leg.rotation.x = step; g.add(leg);
      add(lathe(o.skirt ? thighP : pad(thighP, .012)), o.skirt ? std(skin) : pantsM, 0, -.41, 0, leg, { s: [1, 1.02, .92] });
      add(shinG, shinM, 0, -.82, 0, leg); if (!o.skirt) add(new THREE.SphereGeometry(.066, 14, 10), pantsM, 0, -.41, 0, leg);
      const sh = shoe(leg, 0, .02, o); sh.position.y = -.9;
    }
  }
  if (o.skirt) add(lathe([[.2, 0], [.19, .1], [.165, .3], [.15, .42]]), twillMaterial(o.skirt), 0, hipY - (sit ? .15 : .38), sit ? .05 : 0, g, { s: [1, 1, .8] });

  /* ---- pelvis + torso ---- */
  add(new THREE.SphereGeometry(.16, 24, 16), o.skirt ? twillMaterial(o.skirt) : pantsM, 0, hipY + .02, sit ? .02 : 0, g, { s: [F ? 1.05 : 1, .55, .7] });
  const torso = new THREE.Group(); torso.position.set(0, hipY + .03, sit ? .02 : 0); torso.rotation.x = sit ? .06 : 0; g.add(torso);
  const TP = F ? [[0, 0], [.158, 0], [.15, .08], [.125, .17], [.135, .28], [.158, .36], [.16, .42], [.15, .47], [.11, .52], [.055, .555], [0, .56]]
               : [[0, 0], [.15, 0], [.148, .08], [.14, .17], [.152, .28], [.168, .38], [.172, .44], [.16, .5], [.115, .545], [.06, .565], [0, .57]];
  add(lathe(TP, 30), topM, 0, 0, 0, torso, { s: [1, 1, .64] });
  if (!o.skirt) add(new THREE.TorusGeometry(.148, .012, 8, 32), std('#2A2421', { roughness: .4 }), 0, .03, 0, torso, { rx: Math.PI / 2, s: [1, .64, 1] });   // belt
  if (!o.skirt) add(new THREE.BoxGeometry(.035, .025, .01), std('#C7A26B', { metalness: .8, roughness: .3 }), 0, .03, .097, torso);
  // shirt details
  if (topKind === 'shirt' || topKind === 'blouse') {
    const cm = twillMaterial(shade(o.shirt || '#E9E4D8', topKind === 'blouse' ? .04 : -.02));
    for (const s of [-1, 1]) add(new THREE.BoxGeometry(.06, .012, .04), cm, s * .035, .53, .05, torso, { rz: s * -.6, ry: s * .3, rx: -.3 });
    if (topKind === 'shirt') { add(new THREE.BoxGeometry(.018, .5, .004), cm, 0, .28, .104, torso); for (let k = 0; k < 5; k++) add(new THREE.SphereGeometry(.005, 8, 6), std('#F2EFEA'), 0, .1 + k * .09, .108, torso); }
  } else if (topKind === 'tee' || topKind === 'sweater') add(new THREE.TorusGeometry(.055, .01, 8, 20), topKind === 'sweater' ? knitMaterial(shade(o.shirt, -.05)) : topM, 0, .545, .012, torso, { rx: Math.PI / 2 - .25, s: [1, .8, 1] });
  // blazer: open jacket shell + lapels
  let sleeveM = topM;
  if (o.blazer) { const bm = twillMaterial(o.blazer); sleeveM = bm;
    add(new THREE.LatheGeometry(TP.map(([r, yy]) => V2(r * 1.07 + (r > .05 ? .005 : 0), yy - .01)), 30, Math.PI * .62, Math.PI * 1.76), bm, 0, 0, 0, torso, { s: [1, 1, .66] });
    for (const s of [-1, 1]) add(new THREE.BoxGeometry(.05, .2, .008), bm, s * .055, .42, .104, torso, { rz: s * .32, rx: -.12 }); }
  if (o.lanyard) { add(new THREE.TorusGeometry(.075, .004, 6, 24, Math.PI * 1.1), std('#08766c'), 0, .44, .03, torso, { rx: -1.25, rz: -Math.PI * .05 }); add(new RoundedBoxGeometry(.055, .08, .004, 1, .004), std('#FFFFFF', { roughness: .4 }), 0, .33, .106, torso); add(new THREE.BoxGeometry(.045, .012, .005), std('#08766c'), 0, .355, .108, torso); }

  /* ---- arms ---- */
  const P = { sit: [[-.5, .12, -1.2], [-.55, .1, -1.15]], stand: [[.04, .06, -.12], [.02, .08, -.2]], talk: [[.05, .06, -.15], [-.45, .15, -1.45]], tablet: [[-.35, .2, -1.25], [-.4, .15, -1.2]],
    pack: [[-.65, .12, -.75], [-.6, .1, -.85]], carry: [[-.5, .25, -1.25], [-.5, .25, -1.25]], coffee: [[.04, .08, -.15], [-.25, .12, -1.7]] }[pose];
  for (const s of [-1, 1]) {
    const a = arm(torso, s, sleeveM, o.top !== 'tee', skin, o); a.sh.position.set(s * (F ? .16 : .175), .47, 0);
    const [rx, rz, ex] = s < 0 ? P[0] : P[1]; a.sh.rotation.set(rx, 0, s * rz); a.el.rotation.x = ex;
    add(new THREE.SphereGeometry(.052, 16, 12), sleeveM, s * (F ? .14 : .152), .465, 0, torso, { s: [1.15, .8, .85] });   // shoulder
    if (pose === 'coffee' && s > 0) { add(new THREE.CylinderGeometry(.035, .03, .1, 18), std('#F6F1EA', { roughness: .4 }), 0, -.07, .02, a.hand); add(new THREE.CylinderGeometry(.036, .036, .02, 18), std('#B48A52'), 0, -.03, .02, a.hand); }
    if (pose === 'talk' && s > 0 && o.tablet) add(new RoundedBoxGeometry(.24, .17, .012, 1, .01), std('#1E2430', { roughness: .3 }), 0, -.08, .05, a.hand, { rx: 1.2 });
  }
  if (pose === 'tablet') add(new RoundedBoxGeometry(.24, .015, .17, 1, .01), std('#1E2430', { roughness: .3 }), 0, .28, .28, torso, { rx: -.5 });
  if (pose === 'carry' && o.carry) { o.carry.position.set(0, .2, .33); torso.add(o.carry); }

  /* ---- neck + head ---- */
  add(new THREE.CylinderGeometry(.047, .055, .1, 16), skinMat(skin), 0, .59, .005, torso);
  const hd = head(torso, o, skin, hairC); hd.position.set(0, .7, .012);
  hd.rotation.set(sit ? .22 : (pose === 'tablet' ? .38 : pose === 'pack' ? .45 : 0), o.look || 0, 0);
  return g;
}

function shoe(parent, x, z, o) {
  const s = new THREE.Group(); s.position.set(x, 0, z); parent.add(s);
  if (o.shoes === 'sneaker') { const sn = sneakers(o.shoeColor || '#FFFFFF'); sn.scale.set(1.25, 1.2, 1.35); sn.children[0].position.set(0, 0, 0); s.add(sn.children[0]); return s; }
  const lm = std(o.shoeColor || '#3A2A20', { roughness: .35 });
  add(new RoundedBoxGeometry(.095, .07, .26, 2, .03), lm, 0, .045, .045, s); add(new RoundedBoxGeometry(.1, .02, .27, 1, .01), std('#1A1715'), 0, .01, .045, s);
  return s;
}
