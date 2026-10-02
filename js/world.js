// Процедурный мир зоны: рельеф, дороги, покрытия, препятствия, декорации, маршруты.
import * as THREE from '../vendor/three.module.js';
import { SURF, SURFACES } from './data.js';
import { makeNoise, rng, clamp, lerp, smoothstep } from './noise.js';
import { ZONE_BUILDERS } from './zones.js';

const GRID = 16; // ячейка пространственных индексов, м

// ---------- Сплайны ----------
export function catmull(ctrl, closed, step = 3) {
  const out = [];
  const n = ctrl.length;
  const segs = closed ? n : n - 1;
  const P = (i) => closed ? ctrl[(i + n) % n] : ctrl[clamp(i, 0, n - 1)];
  for (let i = 0; i < segs; i++) {
    const p0 = P(i - 1), p1 = P(i), p2 = P(i + 1), p3 = P(i + 2);
    const len = Math.hypot(p2[0] - p1[0], p2[1] - p1[1]);
    const k = Math.max(1, Math.ceil(len / step));
    for (let j = 0; j < k; j++) {
      const t = j / k, t2 = t * t, t3 = t2 * t;
      const f = (a, b, c, d) => 0.5 * ((2 * b) + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t2 + (-a + 3 * b - 3 * c + d) * t3);
      out.push([f(p0[0], p1[0], p2[0], p3[0]), f(p0[1], p1[1], p2[1], p3[1])]);
    }
  }
  if (!closed) out.push([ctrl[n - 1][0], ctrl[n - 1][1]]);
  return out;
}

function polyline(ctrl, closed, step = 3) {
  const out = [];
  const n = ctrl.length;
  const segs = closed ? n : n - 1;
  for (let i = 0; i < segs; i++) {
    const a = ctrl[i], b = ctrl[(i + 1) % n];
    const k = Math.max(1, Math.ceil(Math.hypot(b[0] - a[0], b[1] - a[1]) / step));
    for (let j = 0; j < k; j++) out.push([lerp(a[0], b[0], j / k), lerp(a[1], b[1], j / k)]);
  }
  if (!closed) out.push([ctrl[n - 1][0], ctrl[n - 1][1]]);
  return out;
}

// Скруглённый прямоугольник (для городских колец трафика)
export function roundedRect(x0, z0, x1, z1, r, step = 3) {
  const pts = [];
  const corners = [[x1 - r, z0 + r, -Math.PI / 2], [x1 - r, z1 - r, 0], [x0 + r, z1 - r, Math.PI / 2], [x0 + r, z0 + r, Math.PI]];
  for (const [cx, cz, a0] of corners) {
    for (let i = 0; i <= 6; i++) { const a = a0 + (i / 6) * Math.PI / 2; pts.push([cx + Math.cos(a) * r, cz + Math.sin(a) * r]); }
  }
  return polyline(pts, true, step);
}

// Смещение полилинии вправо по ходу движения на off метров
export function offsetPath(pts, closed, off) {
  const n = pts.length;
  return pts.map((p, i) => {
    const a = pts[closed ? (i - 1 + n) % n : Math.max(0, i - 1)];
    const b = pts[closed ? (i + 1) % n : Math.min(n - 1, i + 1)];
    let dx = b[0] - a[0], dz = b[1] - a[1];
    const l = Math.hypot(dx, dz) || 1; dx /= l; dz /= l;
    return [p[0] - dz * off, p[1] + dx * off];
  });
}

// ---------- Текстуры ----------
const texCache = {};
function canvasTex(key, w, h, draw, repeat = true) {
  if (texCache[key]) return texCache[key];
  const c = document.createElement('canvas');
  c.width = w; c.height = h;
  const g = c.getContext('2d');
  draw(g, w, h);
  const t = new THREE.CanvasTexture(c);
  t.colorSpace = THREE.SRGBColorSpace;
  if (repeat) t.wrapS = t.wrapT = THREE.RepeatWrapping;
  t.anisotropy = 8;
  texCache[key] = t;
  return t;
}

function speckle(g, w, h, base, amp, n, seed) {
  const r = rng(seed);
  g.fillStyle = base; g.fillRect(0, 0, w, h);
  for (let i = 0; i < n; i++) {
    const v = Math.floor((r() - 0.5) * amp);
    g.fillStyle = v > 0 ? `rgba(255,255,255,${v / 255})` : `rgba(0,0,0,${-v / 255})`;
    g.fillRect(Math.floor(r() * w), Math.floor(r() * h), 1 + Math.floor(r() * 2), 1 + Math.floor(r() * 2));
  }
}

export function roadTexture(style) {
  return canvasTex('road_' + style, 128, 256, (g, w, h) => {
    const bases = { gravel: '#8d806a', mud: '#4b3824', sand: '#c9b07a', wet: '#2b2e35', track: '#3b3c40', city: '#38393d', highway: '#3d3e42', road: '#3d3e42', concrete: '#8c8c8e' };
    speckle(g, w, h, bases[style] || '#3d3e42', style === 'gravel' || style === 'sand' || style === 'mud' ? 90 : 40, 6000, style.length * 7);
    const line = (x, wd, col, dash) => {
      g.fillStyle = col;
      if (dash) g.fillRect(x, 0, wd, h * 0.45); else g.fillRect(x, 0, wd, h);
    };
    if (style === 'highway') {
      line(4, 3, '#e8e8e8'); line(w - 7, 3, '#e8e8e8');
      line(w * 0.25 - 1, 2, '#e8e8e8', true); line(w * 0.75 - 1, 2, '#e8e8e8', true);
      line(w / 2 - 3, 2, '#e3b52b'); line(w / 2 + 1, 2, '#e3b52b');
    } else if (style === 'road') {
      line(4, 3, '#e8e8e8'); line(w - 7, 3, '#e8e8e8'); line(w / 2 - 1, 3, '#e8e8e8', true);
    } else if (style === 'city') {
      g.fillStyle = 'rgba(0,0,0,0.15)'; g.fillRect(0, 0, 6, h); g.fillRect(w - 6, 0, 6, h);
      line(w / 2 - 1, 3, '#dedede', true);
    } else if (style === 'track' || style === 'wet') {
      for (let y = 0; y < h; y += 32) {
        g.fillStyle = (y / 32) % 2 ? '#d22' : '#eee';
        g.fillRect(0, y, 7, 32); g.fillRect(w - 7, y, 7, 32);
      }
      line(9, 2, '#e8e8e8'); line(w - 11, 2, '#e8e8e8');
      if (style === 'wet') { g.fillStyle = 'rgba(120,160,220,0.12)'; g.fillRect(10, 0, w - 20, h); }
    } else if (style === 'gravel' || style === 'mud' || style === 'sand') {
      g.fillStyle = 'rgba(0,0,0,0.13)';
      g.fillRect(w * 0.27, 0, 14, h); g.fillRect(w * 0.73 - 14, 0, 14, h);
    }
  });
}

function detailTexture() {
  return canvasTex('detail', 128, 128, (g, w, h) => speckle(g, w, h, '#e6e6e6', 70, 5000, 3));
}

function windowTexture() {
  return canvasTex('windows', 64, 64, (g, w, h) => {
    g.fillStyle = '#ffffff'; g.fillRect(0, 0, w, h);
    g.fillStyle = '#e4e4e4'; g.fillRect(0, 56, w, 8);
    g.fillStyle = '#3e5a78'; g.fillRect(12, 14, 40, 30);
    g.fillStyle = 'rgba(255,255,255,0.25)'; g.fillRect(12, 14, 40, 6);
  });
}

// ---------- Мир ----------
export class World {
  constructor(zoneId, quality = 'high') {
    this.zoneId = zoneId;
    this.quality = quality;
    this.density = quality === 'low' ? 0.35 : quality === 'medium' ? 0.65 : 1;
    this.roads = [];
    this.circles = []; this.boxes = [];
    this.trees = []; this.rocks = []; this.buildings = []; this.lamps = []; this.cones = []; this.pads = [];
    this.trafficPaths = []; this.repairPoints = []; this.routes = {};
    this.water = null;
    this.sky = { top: 0x5d93d6, bottom: 0xcfe3f2, fog: 0xc6dbe9, fogNear: 150, fogFar: 900, sun: [0.5, 0.8, 0.3] };
    this.group = new THREE.Group();
    this._nr = { d: 0, road: null, si: 0, t: 0, h: 0, s: 0, tx: 0, tz: 1 };

    const B = ZONE_BUILDERS[zoneId];
    B.setup(this);
    this.half = this.size / 2;
    this.cell = this.size / this.res;
    this.N = this.res + 1;
    this._prepareRoads();
    this._buildHeights();
    this.circleGrid = new Map(); this.boxGrid = new Map();
    B.decorate(this);
    this._indexObstacles();
    this._buildMeshes();
  }

  // ----- API для конструкторов зон -----
  addRoad(ctrl, o = {}) {
    const closed = !!o.closed;
    const pts = o.straight ? polyline(ctrl, closed, 3) : catmull(ctrl, closed, 3);
    const road = Object.assign({
      closed, width: 10, surface: SURF.ASPHALT, style: 'road', ribbon: true, flatten: 1, blend: 12,
      smooth: 40, shoulder: null, shoulderW: 0, raise: 0, sections: null, markings: true, hFixed: null,
    }, o);
    road.xs = pts.map(p => p[0]); road.zs = pts.map(p => p[1]);
    road.pts = pts;
    this.roads.push(road);
    return road;
  }

  addTree(x, z, kind = 0, s = 1) {
    this.trees.push({ x, z, kind, s, y: this.heightAt(x, z) });
    this.circles.push({ x, z, r: 0.35 * s + 0.15, kind: 'tree' });
  }
  addRock(x, z, r) {
    this.rocks.push({ x, z, r, y: this.heightAt(x, z), rot: (x * 13.1 + z * 7.7) % 6.28 });
    this.circles.push({ x, z, r: r * 0.85, kind: 'rock', h: r * 1.2 });
  }
  addBuilding(x0, z0, x1, z1, h, color) {
    const y = Math.min(this.heightAt(x0, z0), this.heightAt(x1, z1), this.heightAt(x0, z1), this.heightAt(x1, z0)) - 0.5;
    this.buildings.push({ x0, z0, x1, z1, h, color, y });
    this.boxes.push({ x0, z0, x1, z1 });
  }
  addLamp(x, z, yaw) {
    this.lamps.push({ x, z, yaw, y: this.heightAt(x, z) });
    this.circles.push({ x, z, r: 0.2, kind: 'lamp' });
  }
  addCone(x, z) { this.cones.push({ x, z, y: 0, vx: 0, vy: 0, vz: 0, rx: 0, rz: 0, wx: 0, wz: 0, hit: false, x0: x, z0: z }); }

  clearOfRoads(x, z, margin) {
    const nr = this.nearestRoad(x, z);
    return !nr || nr.d > nr.road.width / 2 + margin;
  }

  pointOnRoad(road, s, lane = 0, reverse = false) {
    const L = road.length;
    s = road.closed ? ((s % L) + L) % L : clamp(s, 0, L);
    let i = 0;
    while (i < road.cum.length - 2 && road.cum[i + 1] < s) i++;
    const j = road.closed ? (i + 1) % road.xs.length : Math.min(i + 1, road.xs.length - 1);
    const seg = road.seg[i] || 1;
    const t = clamp((s - road.cum[i]) / seg, 0, 1);
    let dx = road.xs[j] - road.xs[i], dz = road.zs[j] - road.zs[i];
    const l = Math.hypot(dx, dz) || 1; dx /= l; dz /= l;
    if (reverse) { dx = -dx; dz = -dz; }
    const x = lerp(road.xs[i], road.xs[j], t) - dz * lane;
    const z = lerp(road.zs[i], road.zs[j], t) + dx * lane;
    return { x, z, yaw: Math.atan2(dx, dz), s };
  }

  loopCheckpoints(road, s0, count, lane = 0) {
    const cps = [];
    for (let i = 1; i <= count; i++) cps.push(this.pointOnRoad(road, s0 + road.length * i / count, lane));
    return { cps, length: road.length, closed: true };
  }

  // ----- Подготовка дорог -----
  _prepareRoads() {
    for (const r of this.roads) {
      const n = r.xs.length;
      r.seg = new Float32Array(n); r.cum = new Float32Array(n + 1);
      let acc = 0;
      for (let i = 0; i < n; i++) {
        const j = (i + 1) % n;
        const l = (!r.closed && i === n - 1) ? 0 : Math.hypot(r.xs[j] - r.xs[i], r.zs[j] - r.zs[i]);
        r.seg[i] = l; r.cum[i] = acc; acc += l;
      }
      r.cum[n] = acc;
      r.length = acc;
      // высоты: базовый рельеф + сглаживание
      let hs = new Float32Array(n);
      for (let i = 0; i < n; i++) hs[i] = r.hFixed !== null ? r.hFixed : this.baseH(r.xs[i], r.zs[i]) + r.raise;
      const R = Math.max(1, Math.round(r.smooth / 3));
      for (let pass = 0; pass < 3; pass++) {
        const o = new Float32Array(n);
        for (let i = 0; i < n; i++) {
          let s = 0, c = 0;
          for (let k = -R; k <= R; k++) {
            let j = i + k;
            if (r.closed) j = (j + n) % n; else if (j < 0 || j >= n) continue;
            s += hs[j]; c++;
          }
          o[i] = s / c;
        }
        hs = o;
      }
      r.hs = hs;
      if (r.sections) r.sections = r.sections.map(s => ({ from: s[0] * acc, to: s[1] * acc, surface: s[2], style: s[3] }));
    }
    // пространственный индекс сегментов
    this.roadGrid = new Map();
    this.roads.forEach((r, ri) => {
      const infl = r.width / 2 + 1.5 + r.blend + 3;
      const n = r.xs.length;
      const segs = r.closed ? n : n - 1;
      for (let i = 0; i < segs; i++) {
        const j = (i + 1) % n;
        const x0 = Math.min(r.xs[i], r.xs[j]) - infl, x1 = Math.max(r.xs[i], r.xs[j]) + infl;
        const z0 = Math.min(r.zs[i], r.zs[j]) - infl, z1 = Math.max(r.zs[i], r.zs[j]) + infl;
        for (let cx = Math.floor(x0 / GRID); cx <= Math.floor(x1 / GRID); cx++)
          for (let cz = Math.floor(z0 / GRID); cz <= Math.floor(z1 / GRID); cz++) {
            const key = (cx + 4096) * 8192 + (cz + 4096);
            let l = this.roadGrid.get(key);
            if (!l) { l = []; this.roadGrid.set(key, l); }
            l.push(ri * 65536 + i);
          }
      }
    });
  }

  roadSurface(road, s) {
    if (road.sections) for (const sec of road.sections) if (s >= sec.from && s < sec.to) return sec.surface;
    return road.surface;
  }

  // Ближайшая дорога (только в зоне влияния). Возвращает переиспользуемый объект.
  nearestRoad(x, z, except = null) {
    const list = this.roadGrid.get((Math.floor(x / GRID) + 4096) * 8192 + (Math.floor(z / GRID) + 4096));
    if (!list) return null;
    let best = Infinity, br = -1, bs = 0, bt = 0;
    for (let k = 0; k < list.length; k++) {
      const code = list[k];
      const ri = (code / 65536) | 0, si = code - ri * 65536;
      const r = this.roads[ri];
      if (r === except) continue;
      const j = (si + 1) % r.xs.length;
      const ax = r.xs[si], az = r.zs[si];
      const dx = r.xs[j] - ax, dz = r.zs[j] - az;
      const l2 = dx * dx + dz * dz || 1;
      let t = ((x - ax) * dx + (z - az) * dz) / l2;
      t = t < 0 ? 0 : t > 1 ? 1 : t;
      const px = ax + dx * t - x, pz = az + dz * t - z;
      const d2 = px * px + pz * pz;
      if (d2 < best) { best = d2; br = ri; bs = si; bt = t; }
    }
    if (br < 0) return null;
    const r = this.roads[br];
    const j = (bs + 1) % r.xs.length;
    const nr = this._nr;
    nr.d = Math.sqrt(best); nr.road = r; nr.si = bs; nr.t = bt;
    nr.h = lerp(r.hs[bs], r.hs[j], bt);
    nr.s = r.cum[bs] + r.seg[bs] * bt;
    const sl = r.seg[bs] || 1;
    nr.tx = (r.xs[j] - r.xs[bs]) / sl; nr.tz = (r.zs[j] - r.zs[bs]) / sl;
    return nr;
  }

  // ----- Рельеф -----
  _buildHeights() {
    const N = this.N, c = this.cell, h0 = -this.half;
    const H = new Float32Array(N * N);
    const S = new Uint8Array(N * N);
    for (let j = 0; j < N; j++) {
      const z = h0 + j * c;
      for (let i = 0; i < N; i++) {
        const x = h0 + i * c;
        let h = this.baseH(x, z);
        const nr = this.nearestRoad(x, z);
        if (nr && nr.road.flatten > 0) {
          const r = nr.road;
          const inner = r.width / 2 + 1.5, outer = inner + r.blend;
          if (nr.d < outer) {
            const t = nr.d < inner ? 0 : smoothstep(inner, outer, nr.d);
            h = lerp(h, lerp(nr.h, h, t), r.flatten);
          }
        }
        if (this.postH) h = this.postH(x, z, h);
        H[j * N + i] = h;
      }
    }
    this.H = H;
    for (let j = 0; j < N; j++) for (let i = 0; i < N; i++) S[j * N + i] = this.surfaceAt(h0 + i * c, h0 + j * c);
    this.S = S;
  }

  heightAt(x, z) {
    const N = this.N;
    let fx = (x + this.half) / this.cell, fz = (z + this.half) / this.cell;
    fx = fx < 0 ? 0 : fx > N - 1.001 ? N - 1.001 : fx;
    fz = fz < 0 ? 0 : fz > N - 1.001 ? N - 1.001 : fz;
    const i = fx | 0, j = fz | 0, u = fx - i, v = fz - j;
    const H = this.H, k = j * N + i;
    // та же триангуляция, что и у меша (диагональ i,j+1 — i+1,j)
    if (u + v <= 1) return H[k] + (H[k + 1] - H[k]) * u + (H[k + N] - H[k]) * v;
    return H[k + N + 1] + (H[k + N] - H[k + N + 1]) * (1 - u) + (H[k + 1] - H[k + N + 1]) * (1 - v);
  }

  normalAt(x, z, out) {
    const e = this.cell * 0.6;
    const hl = this.heightAt(x - e, z), hr = this.heightAt(x + e, z);
    const hd = this.heightAt(x, z - e), hu = this.heightAt(x, z + e);
    out.set(hl - hr, 2 * e, hd - hu).normalize();
    return out;
  }

  surfaceAt(x, z) {
    if (this.water && this.heightAt(x, z) < this.water.level - 0.12) return SURF.WATER;
    const nr = this.nearestRoad(x, z);
    if (nr) {
      const r = nr.road;
      if (nr.d < r.width / 2) return this.roadSurface(r, nr.s);
      if (r.shoulder !== null && nr.d < r.width / 2 + r.shoulderW) return r.shoulder;
    }
    return this.ground(x, z);
  }

  // ----- Препятствия -----
  _indexObstacles() {
    const put = (grid, x0, z0, x1, z1, o) => {
      for (let cx = Math.floor(x0 / GRID); cx <= Math.floor(x1 / GRID); cx++)
        for (let cz = Math.floor(z0 / GRID); cz <= Math.floor(z1 / GRID); cz++) {
          const key = (cx + 4096) * 8192 + (cz + 4096);
          let l = grid.get(key);
          if (!l) { l = []; grid.set(key, l); }
          l.push(o);
        }
    };
    for (const c of this.circles) put(this.circleGrid, c.x - c.r, c.z - c.r, c.x + c.r, c.z + c.r, c);
    for (const b of this.boxes) put(this.boxGrid, b.x0, b.z0, b.x1, b.z1, b);
  }

  obstaclesNear(x, z) {
    const key = (Math.floor(x / GRID) + 4096) * 8192 + (Math.floor(z / GRID) + 4096);
    return [this.circleGrid.get(key), this.boxGrid.get(key)];
  }

  // ----- Меши -----
  _buildMeshes() {
    const g = this.group;
    g.add(this._terrainMesh());
    for (const r of this.roads) if (r.ribbon) for (const m of this._ribbons(r)) g.add(m);
    if (this.intersections) g.add(this._intersectionMesh());
    if (this.water) g.add(this._waterMesh());
    if (this.trees.length) this._treeMeshes();
    if (this.rocks.length) this._rockMesh();
    if (this.buildings.length) g.add(this._buildingMesh());
    if (this.lamps.length) this._lampMeshes();
    if (this.pads.length) this._padMeshes();
    if (this.cones.length) this._coneMeshes();
  }

  _terrainMesh() {
    const N = this.N, c = this.cell, h0 = -this.half;
    const pos = new Float32Array(N * N * 3), col = new Float32Array(N * N * 3), uv = new Float32Array(N * N * 2);
    const nz = makeNoise(this.seed + 99);
    for (let j = 0; j < N; j++) for (let i = 0; i < N; i++) {
      const k = j * N + i, x = h0 + i * c, z = h0 + j * c;
      pos[k * 3] = x; pos[k * 3 + 1] = this.H[k]; pos[k * 3 + 2] = z;
      uv[k * 2] = x / 6; uv[k * 2 + 1] = z / 6;
      let rgb = this.colorAt ? this.colorAt(x, z) : null;
      if (!rgb) rgb = SURFACES[this.S[k]].color;
      const v = 0.9 + 0.12 * nz(x / 25, z / 25) + 0.05 * nz(x / 4, z / 4);
      col[k * 3] = rgb[0] * v; col[k * 3 + 1] = rgb[1] * v; col[k * 3 + 2] = rgb[2] * v;
    }
    const idx = new Uint32Array((N - 1) * (N - 1) * 6);
    let p = 0;
    for (let j = 0; j < N - 1; j++) for (let i = 0; i < N - 1; i++) {
      const a = j * N + i, b = a + 1, d = a + N, e = d + 1;
      idx[p++] = a; idx[p++] = d; idx[p++] = b;
      idx[p++] = b; idx[p++] = d; idx[p++] = e;
    }
    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    geo.setAttribute('color', new THREE.BufferAttribute(col, 3));
    geo.setAttribute('uv', new THREE.BufferAttribute(uv, 2));
    geo.setIndex(new THREE.BufferAttribute(idx, 1));
    geo.computeVertexNormals();
    const mat = new THREE.MeshLambertMaterial({ vertexColors: true, map: detailTexture() });
    const m = new THREE.Mesh(geo, mat);
    m.receiveShadow = true;
    return m;
  }

  _ribbons(r) {
    // разбиваем на куски по секциям покрытия
    const n = r.xs.length;
    const styleAt = (s) => {
      if (r.sections) for (const sec of r.sections) if (s >= sec.from && s < sec.to) return sec.style || r.style;
      return r.style;
    };
    const chunks = [];
    let cur = null;
    const count = r.closed ? n + 1 : n;
    for (let k = 0; k < count; k++) {
      const i = k % n;
      const st = styleAt(k === n ? r.length - 0.01 : r.cum[i]);
      if (!cur || cur.style !== st) {
        if (cur) cur.idx.push(k);
        cur = { style: st, idx: [] };
        chunks.push(cur);
      }
      cur.idx.push(k);
    }
    const meshes = [];
    for (const ch of chunks) {
      if (ch.idx.length < 2) continue;
      const pos = [], uv = [], ind = [];
      ch.idx.forEach((k, q) => {
        const i = k % n;
        const a = r.closed ? (i - 1 + n) % n : Math.max(0, i - 1);
        const b = r.closed ? (i + 1) % n : Math.min(n - 1, i + 1);
        let dx = r.xs[b] - r.xs[a], dz = r.zs[b] - r.zs[a];
        const l = Math.hypot(dx, dz) || 1; dx /= l; dz /= l;
        const w = r.width / 2, y = r.hs[i] + 0.05;
        pos.push(r.xs[i] + dz * w, y, r.zs[i] - dx * w, r.xs[i] - dz * w, y, r.zs[i] + dx * w);
        const v = (k === n ? r.length : r.cum[i]) / 16;
        uv.push(0, v, 1, v);
        if (q > 0) { const o = (q - 1) * 2; ind.push(o, o + 1, o + 2, o + 1, o + 3, o + 2); }
      });
      const geo = new THREE.BufferGeometry();
      geo.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
      geo.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
      geo.setIndex(ind);
      geo.computeVertexNormals();
      const mat = new THREE.MeshLambertMaterial({ map: roadTexture(ch.style), polygonOffset: true, polygonOffsetFactor: -2, polygonOffsetUnits: -2 });
      const m = new THREE.Mesh(geo, mat);
      m.receiveShadow = true;
      meshes.push(m);
    }
    return meshes;
  }

  _intersectionMesh() {
    const pos = [], ind = [];
    for (const it of this.intersections) {
      const o = pos.length / 3, w = it.w / 2, y = this.heightAt(it.x, it.z) + 0.07;
      pos.push(it.x - w, y, it.z - w, it.x + w, y, it.z - w, it.x - w, y, it.z + w, it.x + w, y, it.z + w);
      ind.push(o, o + 2, o + 1, o + 1, o + 2, o + 3);
    }
    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
    geo.setIndex(ind);
    geo.computeVertexNormals();
    const m = new THREE.Mesh(geo, new THREE.MeshLambertMaterial({ color: 0x3a3b3f, polygonOffset: true, polygonOffsetFactor: -3, polygonOffsetUnits: -3 }));
    m.receiveShadow = true;
    return m;
  }

  _waterMesh() {
    const geo = new THREE.PlaneGeometry(this.size, this.size, 1, 1);
    geo.rotateX(-Math.PI / 2);
    const m = new THREE.Mesh(geo, new THREE.MeshPhongMaterial({ color: 0x2f6f8f, transparent: true, opacity: 0.72, shininess: 90, specular: 0x88aacc }));
    m.position.y = this.water.level;
    return m;
  }

  _treeMeshes() {
    const kinds = [
      { trunk: new THREE.CylinderGeometry(0.18, 0.3, 3, 6).translate(0, 1.5, 0),
        crown: new THREE.ConeGeometry(2.2, 7, 7).translate(0, 6, 0), color: 0x2a5a2c },
      { trunk: new THREE.CylinderGeometry(0.2, 0.32, 3.4, 6).translate(0, 1.7, 0),
        crown: new THREE.IcosahedronGeometry(2.6, 0).translate(0, 5, 0), color: 0x3d7a2e },
    ];
    const trunkMat = new THREE.MeshLambertMaterial({ color: 0x5b3f2a });
    const shadows = this.quality !== 'low';
    const m4 = new THREE.Matrix4(), q = new THREE.Quaternion(), s = new THREE.Vector3(), p = new THREE.Vector3(), col = new THREE.Color();
    kinds.forEach((K, ki) => {
      const list = this.trees.filter(t => t.kind === ki);
      if (!list.length) return;
      const crownMat = new THREE.MeshLambertMaterial({ color: 0xffffff, flatShading: true });
      const tm = new THREE.InstancedMesh(K.trunk, trunkMat, list.length);
      const cm = new THREE.InstancedMesh(K.crown, crownMat, list.length);
      list.forEach((t, i) => {
        q.setFromAxisAngle(new THREE.Vector3(0, 1, 0), (t.x * 0.37 + t.z * 0.11) % 6.28);
        s.set(t.s, t.s * (0.85 + ((t.x * 7.3) % 1 + 1) % 1 * 0.4), t.s);
        p.set(t.x, t.y - 0.2, t.z);
        m4.compose(p, q, s);
        tm.setMatrixAt(i, m4); cm.setMatrixAt(i, m4);
        const v = 0.75 + (((t.x * 3.1 + t.z * 1.7) % 1 + 1) % 1) * 0.45;
        col.setHex(K.color).multiplyScalar(v);
        cm.setColorAt(i, col);
      });
      tm.castShadow = cm.castShadow = shadows;
      cm.receiveShadow = true;
      this.group.add(tm, cm);
    });
  }

  _rockMesh() {
    const geo = new THREE.DodecahedronGeometry(1, 0);
    const mat = new THREE.MeshLambertMaterial({ color: 0xffffff, flatShading: true });
    const im = new THREE.InstancedMesh(geo, mat, this.rocks.length);
    const m4 = new THREE.Matrix4(), q = new THREE.Quaternion(), e = new THREE.Euler(), s = new THREE.Vector3(), p = new THREE.Vector3(), col = new THREE.Color();
    this.rocks.forEach((r, i) => {
      e.set(r.rot * 0.3, r.rot, r.rot * 0.7); q.setFromEuler(e);
      s.set(r.r, r.r * 0.75, r.r * 1.1);
      p.set(r.x, r.y - r.r * 0.25, r.z);
      m4.compose(p, q, s); im.setMatrixAt(i, m4);
      col.setRGB(0.5, 0.49, 0.47).multiplyScalar(0.8 + (i % 7) * 0.05); im.setColorAt(i, col);
    });
    im.castShadow = this.quality !== 'low'; im.receiveShadow = true;
    this.group.add(im);
  }

  _buildingMesh() {
    const pos = [], uv = [], col = [], ind = [];
    const c = new THREE.Color();
    const quad = (a, b, cc, d, u1, v1, rgb) => {
      const o = pos.length / 3;
      pos.push(...a, ...b, ...cc, ...d);
      uv.push(0, 0, u1, 0, u1, v1, 0, v1);
      for (let k = 0; k < 4; k++) col.push(rgb.r, rgb.g, rgb.b);
      ind.push(o, o + 1, o + 2, o, o + 2, o + 3);
    };
    for (const b of this.buildings) {
      c.setHex(b.color);
      const y0 = b.y, y1 = b.y + b.h;
      const W = b.x1 - b.x0, D = b.z1 - b.z0, V = b.h / 3.2;
      quad([b.x0, y0, b.z1], [b.x1, y0, b.z1], [b.x1, y1, b.z1], [b.x0, y1, b.z1], Math.max(1, Math.round(W / 4)), V, c);
      quad([b.x1, y0, b.z0], [b.x0, y0, b.z0], [b.x0, y1, b.z0], [b.x1, y1, b.z0], Math.max(1, Math.round(W / 4)), V, c);
      quad([b.x1, y0, b.z1], [b.x1, y0, b.z0], [b.x1, y1, b.z0], [b.x1, y1, b.z1], Math.max(1, Math.round(D / 4)), V, c);
      quad([b.x0, y0, b.z0], [b.x0, y0, b.z1], [b.x0, y1, b.z1], [b.x0, y1, b.z0], Math.max(1, Math.round(D / 4)), V, c);
      const roof = c.clone().multiplyScalar(0.55);
      quad([b.x0, y1, b.z1], [b.x1, y1, b.z1], [b.x1, y1, b.z0], [b.x0, y1, b.z0], 0.01, 0.01, roof);
    }
    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
    geo.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
    geo.setAttribute('color', new THREE.Float32BufferAttribute(col, 3));
    geo.setIndex(ind);
    geo.computeVertexNormals();
    const tex = windowTexture();
    const m = new THREE.Mesh(geo, new THREE.MeshLambertMaterial({ map: tex, vertexColors: true }));
    m.castShadow = this.quality !== 'low'; m.receiveShadow = true;
    return m;
  }

  _lampMeshes() {
    const pole = new THREE.CylinderGeometry(0.08, 0.12, 7, 6).translate(0, 3.5, 0);
    const head = new THREE.BoxGeometry(0.4, 0.2, 1.6).translate(0, 7, 0.7);
    const pm = new THREE.InstancedMesh(pole, new THREE.MeshLambertMaterial({ color: 0x55585e }), this.lamps.length);
    const hm = new THREE.InstancedMesh(head, new THREE.MeshLambertMaterial({ color: 0xfff3c4, emissive: 0x887744 }), this.lamps.length);
    const m4 = new THREE.Matrix4(), q = new THREE.Quaternion(), s = new THREE.Vector3(1, 1, 1), p = new THREE.Vector3(), up = new THREE.Vector3(0, 1, 0);
    this.lamps.forEach((l, i) => {
      q.setFromAxisAngle(up, l.yaw); p.set(l.x, l.y, l.z);
      m4.compose(p, q, s); pm.setMatrixAt(i, m4); hm.setMatrixAt(i, m4);
    });
    pm.castShadow = this.quality !== 'low';
    this.group.add(pm, hm);
  }

  _padMeshes() {
    for (const pd of this.pads) {
      const geo = new THREE.CylinderGeometry(pd.r, pd.r, 0.3, 24);
      const m = new THREE.Mesh(geo, new THREE.MeshLambertMaterial({ color: pd.color }));
      m.position.set(pd.x, this.heightAt(pd.x, pd.z) + 0.02, pd.z);
      m.receiveShadow = true;
      this.group.add(m);
    }
  }

  _coneMeshes() {
    const geo = new THREE.ConeGeometry(0.25, 0.75, 10).translate(0, 0.4, 0);
    const base = new THREE.BoxGeometry(0.5, 0.05, 0.5).translate(0, 0.025, 0);
    const mat = new THREE.MeshLambertMaterial({ color: 0xff6a10 });
    const bmat = new THREE.MeshLambertMaterial({ color: 0x222222 });
    for (const c of this.cones) {
      const grp = new THREE.Group();
      const a = new THREE.Mesh(geo, mat); a.castShadow = true;
      grp.add(a, new THREE.Mesh(base, bmat));
      c.y = this.heightAt(c.x, c.z);
      grp.position.set(c.x, c.y, c.z);
      c.mesh = grp;
      this.group.add(grp);
    }
  }

  resetCones() {
    for (const c of this.cones) {
      c.x = c.x0; c.z = c.z0; c.y = this.heightAt(c.x, c.z);
      c.vx = c.vy = c.vz = c.rx = c.rz = c.wx = c.wz = 0; c.hit = false;
      c.mesh.position.set(c.x, c.y, c.z); c.mesh.rotation.set(0, 0, 0);
    }
  }

  // конусы, сбитые машиной, летят и падают
  updateCones(dt, car) {
    for (const c of this.cones) {
      const dx = c.x - car.pos.x, dz = c.z - car.pos.z;
      if (!c.hit && dx * dx + dz * dz < 9) {
        // точная проверка в системе машины
        const lx = dx * car.right.x + dz * car.right.z, lz = dx * car.fwd.x + dz * car.fwd.z;
        if (Math.abs(lx) < car.spec.w / 2 + 0.25 && Math.abs(lz) < car.spec.l / 2 + 0.25) {
          const sp = Math.hypot(car.vel.x, car.vel.z);
          c.hit = true;
          c.vx = car.vel.x * 1.1 + dx * 2; c.vz = car.vel.z * 1.1 + dz * 2; c.vy = 2 + sp * 0.25;
          c.wx = (Math.random() - 0.5) * 12; c.wz = (Math.random() - 0.5) * 12;
          if (this.onConeHit) this.onConeHit(sp);
        }
      }
      if (!c.hit) continue;
      c.vy -= 9.81 * dt;
      c.x += c.vx * dt; c.y += c.vy * dt; c.z += c.vz * dt;
      c.rx += c.wx * dt; c.rz += c.wz * dt;
      const g = this.heightAt(c.x, c.z);
      if (c.y < g) {
        c.y = g; c.vy = -c.vy * 0.3; c.vx *= 0.7; c.vz *= 0.7; c.wx *= 0.6; c.wz *= 0.6;
        if (Math.abs(c.vy) < 0.5) { c.vy = 0; c.rx = c.rx > 0.7 ? Math.PI / 2 : c.rx; }
      }
      c.mesh.position.set(c.x, c.y, c.z);
      c.mesh.rotation.set(c.rx, 0, c.rz);
    }
  }

  dispose() {
    this.group.traverse(o => {
      if (o.geometry) o.geometry.dispose();
      if (o.material) { const ms = Array.isArray(o.material) ? o.material : [o.material]; ms.forEach(m => { if (m.map && !Object.values(texCache).includes(m.map)) m.map.dispose(); m.dispose(); }); }
    });
  }
}
