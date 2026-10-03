// Машина игрока: физика (подвеска на лучах, шины с пятном контакта, привод, тормоза, урон) и модель.
import * as THREE from '../vendor/three.module.js';
import { SURFACES, SURF, PAINTS } from './data.js';
import { clamp, lerp } from './noise.js';

const G = 9.81;
const V3 = THREE.Vector3;

// Размеры и подвеска для каждого типа кузова
const TYPES = {
  sedan:  { w: 1.84, l: 4.5, h: 1.42, r: 0.34, rest: 0.30, comH: 0.55, wb: 2.70, track: 1.56, bodyY: 0.30, bodyH: 0.62, cabH: 0.52, cabL: 2.3, cabZ: -0.25, gears: 6 },
  suv:    { w: 2.00, l: 5.1, h: 1.85, r: 0.43, rest: 0.42, comH: 0.82, wb: 3.05, track: 1.70, bodyY: 0.48, bodyH: 0.85, cabH: 0.62, cabL: 2.8, cabZ: -0.35, gears: 6 },
  pickup: { w: 2.00, l: 5.3, h: 1.85, r: 0.43, rest: 0.42, comH: 0.80, wb: 3.30, track: 1.70, bodyY: 0.50, bodyH: 0.78, cabH: 0.68, cabL: 1.75, cabZ: 0.45, gears: 5, bed: true },
  rally:  { w: 1.80, l: 4.3, h: 1.45, r: 0.34, rest: 0.36, comH: 0.58, wb: 2.60, track: 1.55, bodyY: 0.34, bodyH: 0.6, cabH: 0.54, cabL: 2.2, cabZ: -0.3, gears: 6, spoiler: true },
  sport:  { w: 1.95, l: 4.5, h: 1.22, r: 0.35, rest: 0.22, comH: 0.45, wb: 2.65, track: 1.65, bodyY: 0.22, bodyH: 0.52, cabH: 0.42, cabL: 1.9, cabZ: -0.35, gears: 6, spoiler: true },
};

export function vehicleSpec(def, cs) {
  const T = TYPES[def.body];
  const m = def.mass;
  const health = def.health * (1 + 0.15 * (cs.armor || 0));
  const k = (m * G / 4) / (T.rest * 0.42);
  return Object.assign({}, T, {
    mass: m,
    engineF: def.torque * 2.5 * (1 + 0.08 * (cs.engine || 0)),
    top: def.top / 3.6 * (1 + 0.04 * (cs.engine || 0)),
    grip: def.grip * (1 + 0.06 * (cs.handling || 0)),
    brakeF: m * 10.5 * (1 + 0.06 * (cs.handling || 0)),
    offroad: def.offroad, drive: def.drive, health,
    k, c: 2 * 0.42 * Math.sqrt(k * m / 4),
    I: new V3((T.h * T.h + T.l * T.l), (T.w * T.w + T.l * T.l), (T.w * T.w + T.h * T.h)).multiplyScalar(m / 12 * 1.25),
  });
}

// ---------- Модель ----------
function prism(wb, wt, zb0, zb1, zt0, zt1, h) {
  const g = new THREE.BoxGeometry(1, 1, 1);
  const p = g.attributes.position;
  for (let i = 0; i < p.count; i++) {
    const x = p.getX(i), y = p.getY(i), z = p.getZ(i) + 0.5;
    if (y > 0) p.setXYZ(i, x * wt, h, lerp(zt0, zt1, z)); else p.setXYZ(i, x * wb, 0, lerp(zb0, zb1, z));
  }
  g.computeVertexNormals();
  return g;
}

export function buildCarModel(def, paint) {
  if (def.body === 'suv') return buildSuvModel(def, paint);
  const T = TYPES[def.body];
  const root = new THREE.Group();
  const body = new THREE.Group();
  root.add(body);
  const color = paint || def.color;
  const paintMat = new THREE.MeshStandardMaterial({ color, metalness: 0.45, roughness: 0.38 });
  const darkMat = new THREE.MeshStandardMaterial({ color: 0x1b1c1f, roughness: 0.8 });
  const glassMat = new THREE.MeshStandardMaterial({ color: 0x1d2a36, metalness: 0.6, roughness: 0.15 });
  const headMat = new THREE.MeshStandardMaterial({ color: 0xfffbe8, emissive: 0xfff2c0, emissiveIntensity: 0.6 });
  const tailMat = new THREE.MeshStandardMaterial({ color: 0x550000, emissive: 0xff1010, emissiveIntensity: 0.4 });
  const add = (geo, mat, x = 0, y = 0, z = 0) => { const m = new THREE.Mesh(geo, mat); m.position.set(x, y, z); m.castShadow = true; body.add(m); return m; };
  const L = T.l, W = T.w;

  // нижняя часть кузова
  add(prism(W, W * 0.98, -L / 2, L / 2, -L / 2 + 0.12, L / 2 - 0.25, T.bodyH), paintMat, 0, T.bodyY);
  // бампера
  add(new THREE.BoxGeometry(W * 1.01, 0.22, 0.2), darkMat, 0, T.bodyY + 0.05, L / 2 - 0.05);
  add(new THREE.BoxGeometry(W * 1.01, 0.22, 0.2), darkMat, 0, T.bodyY + 0.05, -L / 2 + 0.05);
  // кабина (стекло) и крыша
  const cz0 = T.cabZ - T.cabL / 2, cz1 = T.cabZ + T.cabL / 2;
  const slopeF = def.body === 'sport' ? 0.75 : 0.5, slopeB = def.body === 'suv' ? 0.12 : def.body === 'pickup' ? 0.1 : 0.4;
  const cy = T.bodyY + T.bodyH;
  add(prism(W * 0.9, W * 0.78, cz0, cz1, cz0 + slopeB, cz1 - slopeF, T.cabH), glassMat, 0, cy - 0.01);
  add(prism(W * 0.8, W * 0.76, cz0 + slopeB + 0.02, cz1 - slopeF - 0.02, cz0 + slopeB + 0.1, cz1 - slopeF - 0.08, 0.07), paintMat, 0, cy + T.cabH - 0.02);
  if (T.bed) {
    const bz0 = -L / 2 + 0.15, bz1 = cz0 - 0.1;
    add(new THREE.BoxGeometry(W * 0.96, 0.36, 0.08), paintMat, 0, cy + 0.18, bz0 + 0.04);
    add(new THREE.BoxGeometry(0.08, 0.36, bz1 - bz0), paintMat, W * 0.46, cy + 0.18, (bz0 + bz1) / 2);
    add(new THREE.BoxGeometry(0.08, 0.36, bz1 - bz0), paintMat, -W * 0.46, cy + 0.18, (bz0 + bz1) / 2);
    add(new THREE.BoxGeometry(W * 0.86, 0.04, bz1 - bz0), darkMat, 0, cy + 0.02, (bz0 + bz1) / 2);
  }
  if (def.body === 'suv') {
    add(new THREE.BoxGeometry(0.06, 0.06, T.cabL * 0.8), darkMat, W * 0.33, cy + T.cabH + 0.1, T.cabZ);
    add(new THREE.BoxGeometry(0.06, 0.06, T.cabL * 0.8), darkMat, -W * 0.33, cy + T.cabH + 0.1, T.cabZ);
  }
  if (T.spoiler) {
    const sy = cy + (def.body === 'rally' ? 0.45 : 0.25);
    add(new THREE.BoxGeometry(W * 0.92, 0.05, 0.38), def.body === 'rally' ? darkMat : paintMat, 0, sy, -L / 2 + 0.25);
    add(new THREE.BoxGeometry(0.06, sy - cy, 0.2), darkMat, W * 0.32, cy + (sy - cy) / 2, -L / 2 + 0.25);
    add(new THREE.BoxGeometry(0.06, sy - cy, 0.2), darkMat, -W * 0.32, cy + (sy - cy) / 2, -L / 2 + 0.25);
  }
  if (def.body === 'rally') {
    add(new THREE.BoxGeometry(W * 0.99, 0.08, L * 0.5), new THREE.MeshStandardMaterial({ color: 0xffffff, roughness: 0.6 }), 0, T.bodyY + T.bodyH * 0.55, 0.2).scale.set(1.005, 1, 1);
  }
  // фары и фонари
  add(new THREE.BoxGeometry(0.42, 0.13, 0.05), headMat, W * 0.32, T.bodyY + T.bodyH * 0.72, L / 2 - 0.12);
  add(new THREE.BoxGeometry(0.42, 0.13, 0.05), headMat, -W * 0.32, T.bodyY + T.bodyH * 0.72, L / 2 - 0.12);
  add(new THREE.BoxGeometry(0.42, 0.13, 0.05), tailMat, W * 0.33, T.bodyY + T.bodyH * 0.75, -L / 2 + 0.03);
  add(new THREE.BoxGeometry(0.42, 0.13, 0.05), tailMat, -W * 0.33, T.bodyY + T.bodyH * 0.75, -L / 2 + 0.03);
  // днище
  add(new THREE.BoxGeometry(W * 0.9, 0.12, L * 0.9), darkMat, 0, T.bodyY - 0.02, 0);

  body.position.y = -T.comH;

  // колёса
  const tireGeo = new THREE.CylinderGeometry(T.r, T.r, 0.26, 18).rotateZ(Math.PI / 2);
  const rimGeo = new THREE.CylinderGeometry(T.r * 0.6, T.r * 0.6, 0.27, 10).rotateZ(Math.PI / 2);
  const spokeGeo = new THREE.BoxGeometry(0.28, T.r * 1.1, 0.08);
  const tireMat = new THREE.MeshStandardMaterial({ color: 0x151515, roughness: 0.95 });
  const rimMat = new THREE.MeshStandardMaterial({ color: def.body === 'rally' ? 0xeeeeee : 0x9ea3aa, metalness: 0.7, roughness: 0.35 });
  const wheels = [];
  for (let i = 0; i < 4; i++) {
    const pivot = new THREE.Group();
    const spin = new THREE.Group();
    const t = new THREE.Mesh(tireGeo, tireMat); t.castShadow = true;
    spin.add(t, new THREE.Mesh(rimGeo, rimMat), new THREE.Mesh(spokeGeo, rimMat));
    pivot.add(spin);
    root.add(pivot);
    wheels.push({ pivot, spin });
  }
  return { root, wheels, tailMat, paintMat };
}

// ---------- Флагманский кроссовер VinCoder: детальная модель ----------
// Боковой профиль задаётся точками (z, y) и выдавливается по ширине машины.
// taper(y) — множитель ширины по высоте (завал боковин к крыше).
function sideExtrude(pts, width, bevel, taper) {
  const shape = new THREE.Shape(pts.map(([z, y]) => new THREE.Vector2(z, y)));
  const depth = width - 2 * bevel;
  const g = new THREE.ExtrudeGeometry(shape, { depth, bevelEnabled: true, bevelThickness: bevel, bevelSize: bevel, bevelSegments: 3, curveSegments: 12 });
  g.rotateY(-Math.PI / 2);
  g.translate(depth / 2, 0, 0);
  if (taper) {
    const p = g.attributes.position;
    for (let i = 0; i < p.count; i++) p.setX(i, p.getX(i) * taper(p.getY(i)));
    g.computeVertexNormals();
  }
  return g;
}

// Точки колёсной арки по нижней кромке профиля (от зада к носу)
function archPts(cz, r, cy, yb, steps = 16) {
  const a0 = Math.asin((yb - cy) / r);
  const out = [];
  for (let i = 0; i <= steps; i++) {
    const t = Math.PI - a0 - (Math.PI - 2 * a0) * i / steps;
    out.push([cz + r * Math.cos(t), cy + r * Math.sin(t)]);
  }
  return out;
}

// Пластиковая накладка арки: дуга-полоса толщиной depth по оси x
function archBand(r0, r1, a0, depth) {
  const s = new THREE.Shape();
  s.absarc(0, 0, r1, a0, Math.PI - a0, false);
  s.absarc(0, 0, r0, Math.PI - a0, a0, true);
  const g = new THREE.ExtrudeGeometry(s, { depth, bevelEnabled: false, curveSegments: 24 });
  g.rotateY(-Math.PI / 2);
  g.translate(depth / 2, 0, 0);
  return g;
}

// Плоская деталь (стекло) на наклонной боковине: x вычисляется из высоты
function sidePane(pts, side, hw) {
  const g = new THREE.ShapeGeometry(new THREE.Shape(pts.map(([z, y]) => new THREE.Vector2(z, y))));
  const p = g.attributes.position;
  for (let i = 0; i < p.count; i++) { const z = p.getX(i), y = p.getY(i); p.setXYZ(i, side * hw(y), y, z); }
  g.computeVertexNormals();
  return g;
}

function quadGeo(a, b, c, d) {
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute([...a, ...b, ...c, ...a, ...c, ...d], 3));
  g.computeVertexNormals();
  return g;
}

const TEX = {};
function labelTex(key, text, w, h, bg, fg, font) {
  if (TEX[key]) return TEX[key];
  const c = document.createElement('canvas'); c.width = w; c.height = h;
  const g = c.getContext('2d');
  if (bg) { g.fillStyle = bg; g.fillRect(0, 0, w, h); g.strokeStyle = '#0b5363'; g.lineWidth = 6; g.strokeRect(3, 3, w - 6, h - 6); }
  g.fillStyle = fg; g.font = font; g.textAlign = 'center'; g.textBaseline = 'middle';
  g.fillText(text, w / 2, h / 2 + 2);
  const t = new THREE.CanvasTexture(c);
  t.colorSpace = THREE.SRGBColorSpace; t.anisotropy = 4;
  return (TEX[key] = t);
}

function buildSuvModel(def, paint) {
  const T = TYPES.suv;
  const root = new THREE.Group();
  const body = new THREE.Group();
  root.add(body);
  const paintMat = new THREE.MeshPhysicalMaterial({ color: paint || def.color, metalness: 0.22, roughness: 0.34, clearcoat: 1, clearcoatRoughness: 0.08 });
  const pianoMat = new THREE.MeshStandardMaterial({ color: 0x0a0c0f, metalness: 0.4, roughness: 0.16 });
  const glassMat = new THREE.MeshStandardMaterial({ color: 0x2b4352, metalness: 0.35, roughness: 0.06, side: THREE.DoubleSide });
  const trimMat = new THREE.MeshStandardMaterial({ color: 0x17191c, roughness: 0.75 });
  const chromeMat = new THREE.MeshStandardMaterial({ color: 0xcfd8de, metalness: 1, roughness: 0.22 });
  const seamMat = new THREE.MeshStandardMaterial({ color: 0x0b0d10, roughness: 0.9 });
  const smokeMat = new THREE.MeshStandardMaterial({ color: 0x14080a, metalness: 0.3, roughness: 0.1 });
  const headMat = new THREE.MeshStandardMaterial({ color: 0xfffbe8, emissive: 0xfff2c0, emissiveIntensity: 0.8 });
  const drlMat = new THREE.MeshStandardMaterial({ color: 0xffffff, emissive: 0xe6f7ff, emissiveIntensity: 1.4 });
  const tailMat = new THREE.MeshStandardMaterial({ color: 0x550000, emissive: 0xff1010, emissiveIntensity: 0.4 });
  const wellMat = new THREE.MeshStandardMaterial({ color: 0x0c0d0f, roughness: 0.95, side: THREE.DoubleSide });
  const reflMat = new THREE.MeshStandardMaterial({ color: 0x6a0a0a, emissive: 0x400000, roughness: 0.3 });
  const add = (geo, mat, x = 0, y = 0, z = 0, parent = body) => {
    const m = new THREE.Mesh(geo, mat); m.position.set(x, y, z); m.castShadow = true; parent.add(m); return m;
  };
  const box = (w, h, d) => new THREE.BoxGeometry(w, h, d);
  const cl = (v) => clamp(v, 0, 1);
  const yb = 0.45, fz = T.wb / 2, cy = T.r;

  // --- нижняя часть кузова: капот, двери с арками, багажник ---
  const lower = [
    [-2.38, yb], ...archPts(-fz, 0.49, cy, yb), ...archPts(fz, 0.49, cy, yb), [2.36, yb],
    [2.50, 0.50], [2.57, 0.64], [2.61, 0.86], [2.58, 1.00], [2.47, 1.08], [2.0, 1.15], [1.45, 1.20],
    [0, 1.23], [-2.05, 1.25], [-2.44, 1.24], [-2.54, 1.17], [-2.57, 0.95], [-2.56, 0.62], [-2.50, 0.50],
  ];
  const taperL = (y) => 1 - 0.05 * cl((y - 1.0) / 0.3);
  const hwL = (y) => 1.0 * taperL(y);
  add(sideExtrude(lower, 2.0, 0.06, taperL), paintMat);

  // --- остекление и «парящая» чёрная крыша ---
  const gh = [[1.45, 1.19], [0.44, 1.76], [0.24, 1.80], [-1.95, 1.81], [-2.22, 1.77], [-2.34, 1.54], [-2.42, 1.24], [-2.30, 1.19]];
  const taperG = (y) => 1 - 0.13 * cl((y - 1.19) / 0.62);
  const hwG = (y) => 0.92 * taperG(y) + 0.004;
  add(sideExtrude(gh, 1.84, 0.05, taperG), pianoMat);
  // боковые стёкла (между ними видны чёрные стойки B и C)
  const panes = [
    [[1.20, 1.27], [0.48, 1.70], [-0.30, 1.70], [-0.30, 1.27]],
    [[-0.42, 1.27], [-0.42, 1.70], [-1.30, 1.70], [-1.30, 1.27]],
    [[-1.42, 1.27], [-1.42, 1.70], [-2.10, 1.70], [-2.24, 1.42], [-2.28, 1.27]],
  ];
  for (const side of [1, -1]) for (const p of panes) add(sidePane(p, side, hwG), glassMat);
  // лобовое стекло
  const wsZ = (y) => 1.45 - (y - 1.19) * 1.01 / 0.57;
  const wn = [0.491, 0.871], wo = 0.056;
  const wsP = (x, y) => [x, y + wn[1] * wo, wsZ(y) + wn[0] * wo];
  add(quadGeo(wsP(-0.84, 1.23), wsP(0.84, 1.23), wsP(0.74, 1.73), wsP(-0.74, 1.73)), glassMat);
  // заднее стекло
  add(quadGeo([0.74, 1.30, -2.47], [-0.74, 1.30, -2.47], [-0.70, 1.54, -2.405], [0.70, 1.54, -2.405]), glassMat);
  add(quadGeo([0.70, 1.54, -2.405], [-0.70, 1.54, -2.405], [-0.64, 1.71, -2.315], [0.64, 1.71, -2.315]), glassMat);
  // спойлер над задним стеклом, лидар, антенна
  add(box(1.56, 0.045, 0.24), pianoMat, 0, 1.845, -2.3).rotation.x = -0.08;
  add(box(0.34, 0.07, 0.18), pianoMat, 0, 1.875, 0.28);
  add(box(0.06, 0.07, 0.16), pianoMat, 0, 1.885, -1.85);

  // --- боковины: накладки, швы дверей, ручки, зеркала ---
  for (const s of [1, -1]) {
    // накладки колёсных арок
    for (const z of [fz, -fz]) {
      add(archBand(0.49, 0.545, 0.03, 0.08), trimMat, s * 0.975, cy, z);
      // подкрылок, чтобы сквозь диск не просвечивал кузов
      add(new THREE.CylinderGeometry(0.49, 0.49, 0.5, 20, 1, true, 0, Math.PI).rotateZ(Math.PI / 2), wellMat, s * 0.72, cy, z);
    }
    // пороги и хромированный молдинг
    add(box(0.04, 0.13, 1.94), trimMat, s * 0.99, 0.53, 0);
    add(box(0.012, 0.018, 1.8), chromeMat, s * 1.008, 0.6, 0);
    // швы дверей
    for (const [z, y0] of [[1.02, 0.95], [-0.36, 0.56], [-1.03, 0.95]]) {
      add(box(0.006, 1.24 - y0, 0.012), seamMat, s * (hwL((y0 + 1.24) / 2) + 0.002), (y0 + 1.24) / 2, z);
    }
    // утопленные ручки
    for (const z of [0.12, -0.92]) add(box(0.01, 0.03, 0.24), chromeMat, s * (hwL(1.1) + 0.004), 1.1, z);
    // зеркала
    add(box(0.12, 0.05, 0.12), pianoMat, s * 0.98, 1.27, 1.1);
    const mir = add(box(0.1, 0.13, 0.24), pianoMat, s * 1.08, 1.34, 1.08);
    mir.rotation.y = s * 0.12;
    add(box(0.012, 0.02, 0.16), drlMat, s * 1.13, 1.305, 1.09);
  }

  // --- корма: сквозная светодиодная полоса, номер, бампер ---
  add(box(1.96, 0.1, 0.04), smokeMat, 0, 1.165, -2.6);
  add(box(1.9, 0.035, 0.02), tailMat, 0, 1.165, -2.625);
  for (const s of [1, -1]) {
    add(box(0.03, 0.035, 0.34), tailMat, s * 0.985, 1.165, -2.42);
    add(box(0.035, 0.09, 0.36), smokeMat, s * 0.975, 1.165, -2.42);
    add(box(0.14, 0.035, 0.022), drlMat, s * 0.5, 1.165, -2.63).scale.y = 0.4; // фонари заднего хода
  }
  // контур двери багажника
  for (const s of [1, -1]) add(box(0.008, 0.5, 0.008), seamMat, s * 0.86, 0.95, -2.636);
  add(box(1.72, 0.008, 0.008), seamMat, 0, 0.70, -2.636);
  // эмблема
  const badge = add(new THREE.PlaneGeometry(0.62, 0.075), new THREE.MeshStandardMaterial({
    map: labelTex('badge', 'VINCODER', 512, 64, null, '#e8f0f4', '900 italic 54px Arial, sans-serif'), transparent: true, metalness: 0.6, roughness: 0.3,
  }), 0, 1.03, -2.637);
  badge.rotation.y = Math.PI; badge.castShadow = false; badge.userData.noMerge = true;
  // номерной знак
  add(box(0.62, 0.17, 0.01), trimMat, 0, 0.8, -2.634);
  const plate = add(new THREE.PlaneGeometry(0.52, 0.115), new THREE.MeshStandardMaterial({
    map: labelTex('plate', 'VINCODER', 512, 112, '#f4f6f6', '#0b5363', 'bold 72px Arial, sans-serif'), roughness: 0.5,
  }), 0, 0.8, -2.641);
  plate.rotation.y = Math.PI; plate.castShadow = false;
  // задний бампер, диффузор, катафоты
  add(box(1.94, 0.16, 0.12), trimMat, 0, 0.53, -2.56);
  add(box(1.1, 0.035, 0.05), chromeMat, 0, 0.48, -2.61);
  for (const s of [1, -1]) add(box(0.16, 0.03, 0.02), reflMat, s * 0.78, 0.6, -2.625);

  // --- передок: световая полоса через всю ширину, фары, воздухозаборник ---
  add(box(1.86, 0.06, 0.03), smokeMat, 0, 0.985, 2.635);
  add(box(1.8, 0.024, 0.02), drlMat, 0, 0.985, 2.65);
  for (const s of [1, -1]) {
    add(box(0.03, 0.024, 0.32), drlMat, s * 0.98, 0.985, 2.4);
    // фары: узкий тёмный блок с линзами по углам бампера
    add(box(0.36, 0.11, 0.03), smokeMat, s * 0.74, 0.78, 2.665);
    add(box(0.3, 0.022, 0.02), drlMat, s * 0.74, 0.815, 2.68);
    for (const dx of [-0.08, 0.02, 0.12]) add(new THREE.CylinderGeometry(0.026, 0.026, 0.02, 12).rotateX(Math.PI / 2), headMat, s * (0.74 + dx * s), 0.765, 2.68);
    add(box(0.09, 0.03, 0.02), headMat, s * 0.82, 0.58, 2.625);
  }
  add(box(1.36, 0.15, 0.06), trimMat, 0, 0.58, 2.6);
  for (let i = -5; i <= 5; i++) add(box(0.012, 0.11, 0.02), seamMat, i * 0.11, 0.58, 2.632);
  add(box(0.9, 0.035, 0.05), chromeMat, 0, 0.485, 2.6);
  add(box(0.16, 0.03, 0.01), chromeMat, 0, 0.9, 2.678);
  // днище
  add(box(1.8, 0.1, 4.4), trimMat, 0, 0.44, 0);

  body.position.y = -T.comH;

  // --- колёса: низкопрофильная резина, чёрные многоспицевые диски, бирюзовые суппорты ---
  const tread = new THREE.CylinderGeometry(T.r, T.r, 0.3, 36, 1, true).rotateZ(Math.PI / 2);
  const wall = new THREE.RingGeometry(0.315, T.r, 36);
  const barrel = new THREE.CylinderGeometry(0.315, 0.315, 0.3, 32, 1, true).rotateZ(Math.PI / 2);
  const lip = new THREE.TorusGeometry(0.315, 0.012, 6, 36).rotateY(Math.PI / 2);
  const spokeGeo = box(0.26, 0.25, 0.034).translate(0, 0.16, 0);
  const hubGeo = new THREE.CylinderGeometry(0.075, 0.075, 0.28, 18).rotateZ(Math.PI / 2);
  const discGeo = new THREE.CylinderGeometry(0.25, 0.25, 0.03, 28).rotateZ(Math.PI / 2);
  const tireMat = new THREE.MeshStandardMaterial({ color: 0x141414, roughness: 0.95, side: THREE.DoubleSide });
  const rimMat = new THREE.MeshStandardMaterial({ color: 0x2a2e33, metalness: 0.7, roughness: 0.25 });
  const innerMat = new THREE.MeshStandardMaterial({ color: 0x08090a, roughness: 0.8, side: THREE.DoubleSide });
  const discMat = new THREE.MeshStandardMaterial({ color: 0x7a7f86, metalness: 0.8, roughness: 0.45 });
  const caliperMat = new THREE.MeshStandardMaterial({ color: 0x19b3c2, metalness: 0.3, roughness: 0.4 });
  const capMat = new THREE.MeshStandardMaterial({ color: 0x0b5363, metalness: 0.6, roughness: 0.3 });
  const wheels = [];
  for (let i = 0; i < 4; i++) {
    const pivot = new THREE.Group();
    const spin = new THREE.Group();
    add(tread, tireMat, 0, 0, 0, spin);
    for (const s of [1, -1]) {
      const w = add(wall, tireMat, s * 0.15, 0, 0, spin); w.rotation.y = s * Math.PI / 2;
      add(lip, rimMat, s * 0.148, 0, 0, spin);
    }
    add(barrel, innerMat, 0, 0, 0, spin);
    add(discGeo, discMat, 0, 0, 0, spin);
    for (let k = 0; k < 10; k++) {
      const a = Math.floor(k / 2) * (Math.PI * 2 / 5) + (k % 2 ? 0.17 : -0.17);
      add(spokeGeo, rimMat, 0, 0, 0, spin).rotation.x = a;
    }
    add(hubGeo, capMat, 0, 0, 0, spin);
    add(box(0.07, 0.16, 0.1), caliperMat, 0, 0.17, -0.12, pivot).rotation.x = -0.6;
    pivot.add(spin);
    root.add(pivot);
    wheels.push({ pivot, spin });
  }
  return { root, wheels, tailMat, paintMat };
}

// ---------- Физика ----------
const tmp = { a: new V3(), b: new V3(), c: new V3(), d: new V3(), n: new V3(), hf: new V3(), side: new V3(), vp: new V3(), F: new V3(), r: new V3(), q: new THREE.Quaternion() };

export class Vehicle {
  constructor(def, carSave, world) {
    this.def = def;
    this.world = world;
    this.spec = vehicleSpec(def, carSave);
    this.maxHealth = this.spec.health;
    this.health = this.spec.health * clamp(carSave.health ?? 1, 0, 1);
    const S = this.spec;
    this.pos = new V3(); this.vel = new V3(); this.q = new THREE.Quaternion(); this.angVel = new V3();
    this.fwd = new V3(0, 0, 1); this.up = new V3(0, 1, 0); this.right = new V3(-1, 0, 0);
    const anchorY = -(S.comH - S.r - 0.58 * S.rest);
    const fx = S.track / 2, fz = S.wb / 2;
    this.wheels = [[fx, fz, true], [-fx, fz, true], [fx, -fz, false], [-fx, -fz, false]].map(([x, z, front]) => ({
      local: new V3(x, anchorY, z), front,
      driven: S.drive === 'AWD' || (S.drive === 'FWD' ? front : !front),
      comp: 0, prevComp: 0, contact: false, surface: SURF.ASPHALT, spin: 0, skid: 0, load: 0, vL: 0, world: new V3(),
    }));
    this.drivenCount = this.wheels.filter(w => w.driven).length;
    this.steer = 0; this.steerAngle = 0;
    this.reverse = false; this.gear = 1; this.rpm = 900; this.shiftTimer = 0;
    this.throttleOut = 0; this.brakeOut = 0; this.handbrake = false;
    this.onImpact = null;
    this.impactCooldown = 0;
    this.speed = 0; this.airTime = 0; this.grounded = false;
    this.model = buildCarModel(def, PAINTS[carSave.paint || 0]);
    this.mesh = this.model.root;
  }

  setPaint(hex) { this.model.paintMat.color.setHex(hex || this.def.color); }

  place(x, z, yaw) {
    const S = this.spec;
    this.pos.set(x, this.world.heightAt(x, z) + S.comH + 0.25, z);
    this.q.setFromAxisAngle(new V3(0, 1, 0), yaw);
    this.vel.set(0, 0, 0); this.angVel.set(0, 0, 0);
    this.steer = 0; this.reverse = false; this.gear = 1;
    for (const w of this.wheels) { w.comp = w.prevComp = 0; w.contact = false; }
    this._axes();
    this.syncMesh(0);
  }

  _axes() {
    this.fwd.set(0, 0, 1).applyQuaternion(this.q);
    this.up.set(0, 1, 0).applyQuaternion(this.q);
    this.right.set(-1, 0, 0).applyQuaternion(this.q);
  }

  get healthRatio() { return this.health / this.maxHealth; }
  get yaw() { return Math.atan2(this.fwd.x, this.fwd.z); }

  // input: steer -1..1 (вправо +), throttle 0..1, brake 0..1, handbrake bool
  update(dt, input, damageOn = true) {
    const S = this.spec;
    this._axes();
    const v = this.vel.dot(this.fwd);
    this.speed = v;
    const absV = Math.abs(v);

    // руль: ограничение угла на скорости, плавный ход
    const target = clamp(input.steer, -1, 1);
    const rate = (Math.sign(target) !== Math.sign(this.steer) && target !== 0) ? 7 : 4;
    this.steer += clamp(target - this.steer, -rate * dt, rate * dt);
    const maxA = lerp(0.62, 0.11, clamp(absV / 42, 0, 1));
    this.steerAngle = this.steer * maxA;

    // газ/тормоз/задний ход
    let throttle = input.throttle, brake = input.brake;
    if (this.reverse) { if (throttle > 0.05 && v > -1.5) this.reverse = false; }
    else if (brake > 0.05 && v < 0.8 && throttle < 0.05) this.reverse = true;
    let drive, brakeAmt;
    if (!this.reverse) { drive = throttle; brakeAmt = brake; } else { drive = -brake; brakeAmt = throttle; }
    if (absV < 0.6 && drive === 0) brakeAmt = 1;
    this.handbrake = !!input.handbrake;
    this.brakeOut = brakeAmt; this.throttleOut = Math.abs(drive);

    // покрытие под колёсами (раз в кадр)
    let speedMul = 0, dn = 0;
    for (const w of this.wheels) {
      w.world.copy(w.local).applyQuaternion(this.q).add(this.pos);
      w.surface = this.world.surfaceAt(w.world.x, w.world.z);
      if (w.driven) { speedMul += SURFACES[w.surface].speed; dn++; }
    }
    speedMul = speedMul / dn;
    speedMul = speedMul + (1 - speedMul) * S.offroad * 0.5;

    // двигатель и коробка
    const hr = this.healthRatio;
    const dmg = hr <= 0 ? 0.4 : hr < 0.3 ? 0.75 : 1;
    const vmax = S.top * speedMul * (hr <= 0 ? 0.45 : hr < 0.3 ? 0.8 : 1);
    const vr = this.reverse ? Math.min(vmax * 0.3, 12) : vmax;
    let f = S.engineF * dmg * clamp(1 - Math.pow(absV / vr, 2), 0, 1);
    if (this.shiftTimer > 0) { this.shiftTimer -= dt; f *= 0.3; }
    this.driveForce = drive * f;
    this._gearbox(dt, absV, vmax, drive);

    // физика с подшагами
    const sub = Math.max(1, Math.ceil(dt / (1 / 120)));
    const h = dt / sub;
    for (let i = 0; i < sub; i++) this._step(h, brakeAmt, input);
    this._collide(damageOn);
    if (this.impactCooldown > 0) this.impactCooldown -= dt;
    this.airTime = this.grounded ? 0 : this.airTime + dt;
    this.syncMesh(dt);
  }

  _gearbox(dt, absV, vmax, drive) {
    const S = this.spec;
    if (this.reverse) { this.gear = -1; this.rpm = lerp(this.rpm, 900 + Math.abs(drive) * 2500 + absV * 120, 0.15); return; }
    if (this.gear < 1) this.gear = 1;
    const n = S.gears;
    const top = (g) => Math.pow(g / n, 0.78) * vmax * 1.04;
    let rpm = 900 + (absV / top(this.gear)) * 6100;
    if (rpm > 6500 && this.gear < n) { this.gear++; this.shiftTimer = 0.18; }
    else if (this.gear > 1 && 900 + (absV / top(this.gear - 1)) * 6100 < 5200) { this.gear--; }
    rpm = 900 + (absV / top(this.gear)) * 6100;
    const slipping = !this.grounded || this.wheels.some(w => w.driven && w.skid > 0.6);
    if (slipping) rpm = Math.max(rpm, 1200 + drive * 5600);
    if (drive === 0 && absV < 1) rpm = 900;
    this.rpm = lerp(this.rpm, clamp(rpm, 800, 7200), 1 - Math.exp(-dt * 12));
  }

  _step(dt, brakeAmt, input) {
    const S = this.spec, W = this.world;
    const m = S.mass;
    const F = tmp.F.set(0, -G * m, 0);
    const T = new V3();
    this._axes();
    const up = this.up, fwd = this.fwd, right = this.right;
    let contacts = 0;
    const maxLen = S.rest + S.r;
    const ca = Math.cos(this.steerAngle), sa = Math.sin(this.steerAngle);

    for (const w of this.wheels) {
      const A = tmp.a.copy(w.local).applyQuaternion(this.q).add(this.pos);
      const gh = W.heightAt(A.x, A.z);
      const dist = A.y - gh;
      if (dist > maxLen || up.y < 0.15) { w.contact = false; w.comp = 0; w.prevComp = 0; w.skid = 0; w.load = 0; continue; }
      contacts++;
      w.contact = true;
      const comp = maxLen - dist;
      const compRate = (comp - w.prevComp) / dt;
      w.prevComp = comp; w.comp = comp;
      const n = W.normalAt(A.x, A.z, tmp.n);
      let Fs = S.k * Math.min(comp, S.rest) + S.c * compRate;
      if (comp > S.rest) Fs += S.k * 10 * (comp - S.rest) + S.c * 2 * Math.max(0, compRate);
      const surf = SURFACES[w.surface];
      // тряска на неровных покрытиях
      if (surf.bump > 0) Fs *= 1 + (Math.random() - 0.5) * surf.bump * clamp(Math.abs(this.speed) / 15, 0, 1);
      Fs = Math.max(0, Fs);
      w.load = Fs;

      // направление колеса в плоскости земли
      const hf = tmp.hf.copy(fwd);
      if (w.front) hf.multiplyScalar(ca).addScaledVector(right, sa);
      hf.addScaledVector(n, -hf.dot(n)).normalize();
      const side = tmp.side.crossVectors(hf, n).normalize();

      const P = tmp.b.set(A.x, gh, A.z);
      const r = tmp.r.subVectors(P, this.pos);
      const vp = tmp.vp.crossVectors(this.angVel, r).add(this.vel);
      const vL = vp.dot(hf), vS = vp.dot(side);
      w.vL = vL;

      const loose = !(w.surface === SURF.ASPHALT || w.surface === SURF.CONCRETE || w.surface === SURF.WET);
      const gMul = loose ? surf.grip + (1 - surf.grip) * S.offroad * 0.6 : surf.grip;
      const Fmax = gMul * S.grip * 1.08 * Fs;

      // боковая сила
      const rear = !w.front;
      const latGrip = (this.handbrake && rear) ? 0.42 : 1;
      const slip = vS / Math.max(Math.abs(vL), 3.5);
      let Flat = -clamp(slip * 6, -1, 1) * Fmax * latGrip;

      // продольная сила
      let Flong = 0;
      if (w.driven) Flong += this.driveForce / this.drivenCount;
      const kb = (m / 4) / dt * 0.5;
      const Fb = brakeAmt * S.brakeF / 4 * surf.brake;
      if (Fb > 0) Flong += -clamp(vL * kb, -Fb, Fb);
      if (this.handbrake && rear) { const Fh = S.brakeF * 0.4; Flong += -clamp(vL * kb, -Fh, Fh); }
      Flong += -clamp(vL * 2, -1, 1) * (0.012 + surf.resist * (1 - S.offroad * 0.6)) * Fs;

      const tot = Math.hypot(Flat, Flong);
      let longSlip = 0;
      if (tot > Fmax && tot > 0) {
        const s = Fmax / tot;
        if (Math.abs(Flong) > Fmax * 0.9) longSlip = 1;
        Flat *= s; Flong *= s;
      }
      w.skid = clamp(Math.abs(vS) / 5 - 0.15 + longSlip * 0.8, 0, 1);

      const f = tmp.c.copy(n).multiplyScalar(Fs).addScaledVector(hf, Flong).addScaledVector(side, Flat);
      F.add(f);
      // силы шин прикладываем чуть выше пятна контакта — меньше склонность к опрокидыванию
      r.addScaledVector(up, (this.pos.y - P.y) * 0.45);
      T.add(tmp.d.crossVectors(r, f));
    }

    // стабилизаторы поперечной устойчивости
    for (let a = 0; a < 4; a += 2) {
      const l = this.wheels[a], rr = this.wheels[a + 1];
      if (!l.contact || !rr.contact) continue;
      const Far = (l.comp - rr.comp) * S.k * 0.7;
      const pl = tmp.a.copy(l.local).applyQuaternion(this.q);
      const pr = tmp.b.copy(rr.local).applyQuaternion(this.q);
      const fa = tmp.c.copy(up).multiplyScalar(Far);
      T.add(tmp.d.crossVectors(pl, fa));
      fa.multiplyScalar(-1);
      T.add(tmp.d.crossVectors(pr, fa));
    }

    // столкновение кузова с землёй (опрокидывание, жёсткие приземления)
    let bodyHit = 0;
    const hw = S.w / 2, hl = S.l / 2;
    const yb = -S.comH + S.r * 0.9, yt = yb + S.h * 0.85;
    for (let cxi = -1; cxi <= 1; cxi += 2) for (let czi = -1; czi <= 1; czi += 2) for (let yi = 0; yi < 2; yi++) {
      const lp = tmp.a.set(cxi * hw, yi ? yt : yb, czi * hl).applyQuaternion(this.q);
      const C = tmp.b.copy(lp).add(this.pos);
      const gh = W.heightAt(C.x, C.z);
      if (C.y >= gh) continue;
      const n = W.normalAt(C.x, C.z, tmp.n);
      const pen = gh - C.y;
      const vc = tmp.vp.crossVectors(this.angVel, lp).add(this.vel);
      const vn = vc.dot(n);
      if (-vn > bodyHit) bodyHit = -vn;
      const Fn = Math.max(0, pen * m * 80 - vn * m * 8);
      const vt = tmp.c.copy(vc).addScaledVector(n, -vn);
      const vtl = vt.length();
      const f = tmp.d.copy(n).multiplyScalar(Fn);
      if (vtl > 1e-3) f.addScaledVector(vt, -Math.min(0.55 * Fn, vtl * m / 8 / dt) / vtl);
      F.add(f);
      T.add(new V3().crossVectors(lp, f));
    }
    if (bodyHit > 7 && this.impactCooldown <= 0) this._impact(bodyHit * 0.6, 'ground');

    this.grounded = contacts > 0;
    // аэродинамика
    const sp = this.vel.length();
    F.addScaledVector(this.vel, -0.42 * sp);
    // управление в воздухе и выравнивание
    if (!contacts) {
      T.addScaledVector(right, (input.brake - input.throttle) * m * 0.9);
      T.addScaledVector(up, input.steer * -m * 0.8);
      T.add(tmp.a.crossVectors(up, tmp.b.set(0, 1, 0)).multiplyScalar(m * 1.5));
    }

    // вода: сопротивление и лёгкая плавучесть
    if (W.water && this.pos.y - S.comH * 0.6 < W.water.level) {
      const depth = clamp(W.water.level - (this.pos.y - S.comH * 0.6), 0, 1.2);
      F.addScaledVector(this.vel, -m * 0.9 * depth);
      F.y += m * G * 0.35 * depth;
    }
    // интегрирование
    this.vel.addScaledVector(F, dt / m);
    this.pos.addScaledVector(this.vel, dt);
    const qi = tmp.q.copy(this.q).invert();
    const wb = tmp.a.copy(this.angVel).applyQuaternion(qi);
    const tb = T.applyQuaternion(qi);
    wb.x += tb.x / S.I.x * dt; wb.y += tb.y / S.I.y * dt; wb.z += tb.z / S.I.z * dt;
    this.angVel.copy(wb.applyQuaternion(this.q)).multiplyScalar(1 - 0.4 * dt);
    const w = this.angVel;
    const dq = new THREE.Quaternion(w.x * dt * 0.5, w.y * dt * 0.5, w.z * dt * 0.5, 0).multiply(this.q);
    this.q.x += dq.x; this.q.y += dq.y; this.q.z += dq.z; this.q.w += dq.w;
    this.q.normalize();
  }

  _impact(speed, kind) {
    this.impactCooldown = 0.25;
    if (this.onImpact) this.onImpact(speed, kind);
  }

  applyDamage(speed) {
    if (speed <= 4) return 0;
    const d = Math.pow(speed - 4, 1.25) * 1.6;
    this.health = Math.max(0, this.health - d);
    return d;
  }

  // столкновения с деревьями, камнями, зданиями, фонарями и трафиком (в плоскости XZ)
  _collide() {
    const S = this.spec, W = this.world;
    const groundY = W.heightAt(this.pos.x, this.pos.z);
    if (this.pos.y - groundY > 4) return;
    const fl = Math.hypot(this.fwd.x, this.fwd.z) || 1;
    const fx = this.fwd.x / fl, fz = this.fwd.z / fl;
    const rad = S.w / 2;
    const offs = [S.l / 2 - rad, 0, -(S.l / 2 - rad)];
    let maxImp = 0, kind = '';
    for (const o of offs) {
      const cx = this.pos.x + fx * o, cz = this.pos.z + fz * o;
      const [circles, boxes] = W.obstaclesNear(cx, cz);
      if (circles) for (const c of circles) {
        if (c.h && this.pos.y - groundY > c.h + 0.5) continue;
        const dx = cx - c.x, dz = cz - c.z, d = Math.hypot(dx, dz);
        const pen = rad + c.r - d;
        if (pen > 0 && d > 1e-4) { const imp = this._resolve(cx, cz, dx / d, dz / d, pen, rad, 0, 0); if (imp > maxImp) { maxImp = imp; kind = c.kind; } }
      }
      if (boxes) for (const b of boxes) {
        const px = clamp(cx, b.x0, b.x1), pz = clamp(cz, b.z0, b.z1);
        let dx = cx - px, dz = cz - pz, d = Math.hypot(dx, dz), pen;
        if (d < 1e-4) {
          const ex = [cx - b.x0, b.x1 - cx, cz - b.z0, b.z1 - cz];
          const k = ex.indexOf(Math.min(...ex));
          dx = k === 0 ? -1 : k === 1 ? 1 : 0; dz = k === 2 ? -1 : k === 3 ? 1 : 0; d = 1; pen = ex[k] + rad;
        } else pen = rad - d;
        if (pen > 0) { const imp = this._resolve(cx, cz, dx / d, dz / d, pen, rad, 0, 0); if (imp > maxImp) { maxImp = imp; kind = 'building'; } }
      }
      if (this.traffic) for (const car of this.traffic.cars) {
        for (const co of car.circles) {
          const dx = cx - co.x, dz = cz - co.z, d = Math.hypot(dx, dz);
          const pen = rad + co.r - d;
          if (pen > 0 && d > 1e-4) {
            const imp = this._resolve(cx, cz, dx / d, dz / d, pen, rad, car.vx, car.vz);
            car.hit(imp);
            if (imp > maxImp) { maxImp = imp; kind = 'car'; }
          }
        }
      }
    }
    // граница карты
    const lim = W.half - 4;
    for (const axis of ['x', 'z']) {
      if (Math.abs(this.pos[axis]) > lim) {
        const s = Math.sign(this.pos[axis]);
        this.pos[axis] = s * lim;
        if (this.vel[axis] * s > 0) { const imp = Math.abs(this.vel[axis]); this.vel[axis] *= -0.3; if (imp > maxImp) { maxImp = imp; kind = 'wall'; } }
      }
    }
    if (maxImp > 2.5 && this.impactCooldown <= 0) this._impact(maxImp, kind);
  }

  _resolve(cx, cz, nx, nz, pen, rad, ovx, ovz) {
    const S = this.spec;
    this.pos.x += nx * pen; this.pos.z += nz * pen;
    const r = tmp.r.set(cx - nx * rad - this.pos.x, 0, cz - nz * rad - this.pos.z);
    const n = tmp.n.set(nx, 0, nz);
    const vr = tmp.vp.crossVectors(this.angVel, r).add(this.vel);
    vr.x -= ovx; vr.z -= ovz;
    const vn = vr.dot(n);
    if (vn >= 0) return 0;
    const qi = tmp.q.copy(this.q).invert();
    const invI = (v) => { const b = v.clone().applyQuaternion(qi); b.x /= S.I.x; b.y /= S.I.y; b.z /= S.I.z; return b.applyQuaternion(this.q); };
    const rn = new V3().crossVectors(r, n);
    const term = n.dot(new V3().crossVectors(invI(rn), r));
    const j = -(1 + 0.15) * vn / (1 / S.mass + term);
    this.vel.addScaledVector(n, j / S.mass);
    this.angVel.add(invI(rn.multiplyScalar(j)));
    // трение вдоль препятствия
    const vt = vr.addScaledVector(n, -vn);
    const vtl = vt.length();
    if (vtl > 0.01) {
      const jt = Math.min(vtl * S.mass * 0.3, j * 0.35);
      this.vel.addScaledVector(vt, -jt / vtl / S.mass);
    }
    return -vn;
  }

  syncMesh(dt) {
    const S = this.spec;
    this.mesh.position.copy(this.pos);
    this.mesh.quaternion.copy(this.q);
    this.wheels.forEach((w, i) => {
      const vis = this.model.wheels[i];
      const off = w.contact ? clamp(S.rest - w.comp, 0, S.rest) : S.rest;
      vis.pivot.position.set(w.local.x, w.local.y - off, w.local.z);
      vis.pivot.rotation.y = w.front ? -this.steerAngle : 0;
      let spinV = w.contact ? w.vL : (w.driven ? this.speed + this.driveForce * 0.002 : this.speed);
      if (w.contact && w.driven && w.skid > 0.7 && this.throttleOut > 0.5 && !this.reverse) spinV += 8;
      if (this.handbrake && !w.front) spinV = 0;
      w.spin += spinV / S.r * dt;
      vis.spin.rotation.x = w.spin;
    });
    const braking = this.brakeOut > 0.1 && Math.abs(this.speed) > 0.6;
    this.model.tailMat.emissiveIntensity = braking || this.reverse ? 2.2 : 0.4;
  }
}
