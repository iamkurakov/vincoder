// AI-трафик: машины едут по полосам, держат дистанцию, тормозят перед игроком, останавливаются после удара.
import * as THREE from '../vendor/three.module.js';
import { VEHICLES } from './data.js';
import { buildCarModel, vehicleSpec } from './vehicle.js';
import { rng, clamp, lerp } from './noise.js';

const AI_COLORS = [0xc7c9d1, 0x2b2d33, 0xf0f0ee, 0x8a1c1c, 0x1f4f8f, 0x3d6b3a, 0xb59a6a, 0x5b6470, 0xd4a017, 0x6e2a6e];

// Склеиваем модель машины в две геометрии: окрашиваемый кузов и всё остальное (с цветами вершин)
function mergeModel(def) {
  const model = buildCarModel(def, 0xffffff);
  const S = vehicleSpec(def, {});
  const fx = S.track / 2, fz = S.wb / 2;
  [[fx, fz], [-fx, fz], [fx, -fz], [-fx, -fz]].forEach(([x, z], i) => model.wheels[i].pivot.position.set(x, -S.comH + S.r, z));
  model.root.updateMatrixWorld(true);
  const parts = { paint: { pos: [], nor: [] }, rest: { pos: [], nor: [], col: [] } };
  model.root.traverse(o => {
    if (!o.isMesh) return;
    const g = o.geometry.index ? o.geometry.toNonIndexed() : o.geometry.clone();
    g.applyMatrix4(o.matrixWorld);
    const isPaint = o.material === model.paintMat;
    const P = isPaint ? parts.paint : parts.rest;
    const pa = g.attributes.position.array, na = g.attributes.normal.array;
    for (let i = 0; i < pa.length; i++) { P.pos.push(pa[i]); P.nor.push(na[i]); }
    if (!isPaint) {
      const c = o.material.color.clone();
      if (o.material.emissive && o.material.emissiveIntensity > 0.3) c.lerp(o.material.emissive, 0.6);
      for (let i = 0; i < pa.length / 3; i++) P.col.push(c.r, c.g, c.b);
    }
  });
  const mk = (P) => {
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.Float32BufferAttribute(P.pos, 3));
    g.setAttribute('normal', new THREE.Float32BufferAttribute(P.nor, 3));
    if (P.col) g.setAttribute('color', new THREE.Float32BufferAttribute(P.col, 3));
    return g;
  };
  return { paint: mk(parts.paint), rest: mk(parts.rest), comH: S.comH, len: S.l };
}

let MODELS = null;

export class Traffic {
  constructor(world, scene, enabled) {
    this.world = world;
    this.cars = [];
    this.group = new THREE.Group();
    scene.add(this.group);
    this.onHonk = null;
    if (!enabled || !world.trafficPaths.length) return;
    if (!MODELS) MODELS = ['sedan', 'suv', 'pickup'].map(id => mergeModel(VEHICLES.find(v => v.id === id)));
    const restMat = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.6, metalness: 0.2 });
    const r = rng(world.seed * 7 + 1);
    const density = world.quality === 'low' ? 0.6 : 1;
    for (const p of world.trafficPaths) {
      const n = p.pts.length;
      const cum = new Float32Array(n + 1);
      for (let i = 0; i < n; i++) {
        const j = (i + 1) % n;
        cum[i + 1] = cum[i] + ((!p.closed && i === n - 1) ? 0 : Math.hypot(p.pts[j][0] - p.pts[i][0], p.pts[j][1] - p.pts[i][1]));
      }
      p.cum = cum; p.length = cum[n];
      const count = Math.max(1, Math.floor(p.length / p.spacing * density));
      for (let k = 0; k < count; k++) {
        const s = (k + r() * 0.5) * p.length / count;
        const M = MODELS[Math.floor(r() * MODELS.length)];
        const mesh = new THREE.Group();
        const pm = new THREE.Mesh(M.paint, new THREE.MeshStandardMaterial({ color: AI_COLORS[Math.floor(r() * AI_COLORS.length)], metalness: 0.4, roughness: 0.4 }));
        const rm = new THREE.Mesh(M.rest, restMat);
        pm.castShadow = rm.castShadow = world.quality !== 'low';
        mesh.add(pm, rm);
        this.group.add(mesh);
        const car = {
          path: p, s, idx: 0, speed: p.speed * 0.8, factor: 0.85 + r() * 0.3, M, mesh,
          x: 0, z: 0, vx: 0, vz: 0, dx: 0, dz: 1, stop: 0,
          circles: [{ x: 0, z: 0, r: 1.0 }, { x: 0, z: 0, r: 1.0 }],
          hit: (imp) => { if (imp > 1.5) { if (car.stop <= 0 && this.onHonk) this.onHonk(car); car.stop = 3.5; car.speed *= 0.3; } },
        };
        this._place(car);
        this.cars.push(car);
      }
    }
  }

  _place(car) {
    const p = car.path, n = p.pts.length;
    let s = car.s;
    if (p.closed) s = ((s % p.length) + p.length) % p.length; else s = clamp(s, 0, p.length);
    car.s = s;
    // индекс сегмента (машины едут вперёд, поэтому ищем от прошлого)
    let i = car.idx;
    if (p.cum[i] > s) i = 0;
    while (i < n - 1 && p.cum[i + 1] <= s) i++;
    car.idx = i;
    const j = (i + 1) % n;
    const seg = (p.cum[i + 1] - p.cum[i]) || 1;
    const t = (s - p.cum[i]) / seg;
    const a = p.pts[i], b = p.pts[j];
    car.x = lerp(a[0], b[0], t); car.z = lerp(a[1], b[1], t);
    // направление сглаживаем по точке впереди
    const ahead = this._pointAt(p, s + 3);
    let dx = ahead[0] - car.x, dz = ahead[1] - car.z;
    const l = Math.hypot(dx, dz) || 1;
    car.dx = dx / l; car.dz = dz / l;
    const half = car.M.len / 2 - 0.9;
    car.circles[0].x = car.x + car.dx * half; car.circles[0].z = car.z + car.dz * half;
    car.circles[1].x = car.x - car.dx * half; car.circles[1].z = car.z - car.dz * half;
    car.vx = car.dx * car.speed; car.vz = car.dz * car.speed;
    const W = this.world;
    const hf = W.heightAt(car.circles[0].x, car.circles[0].z), hb = W.heightAt(car.circles[1].x, car.circles[1].z);
    const y = (hf + hb) / 2 + car.M.comH;
    car.mesh.position.set(car.x, y, car.z);
    car.mesh.rotation.set(0, 0, 0);
    car.mesh.rotation.order = 'YXZ';
    car.mesh.rotation.y = Math.atan2(car.dx, car.dz);
    car.mesh.rotation.x = -Math.atan2(hf - hb, half * 2);
  }

  _pointAt(p, s) {
    if (p.closed) s = ((s % p.length) + p.length) % p.length; else s = clamp(s, 0, p.length - 0.01);
    let lo = 0, hi = p.pts.length - 1;
    while (lo < hi) { const mid = (lo + hi + 1) >> 1; if (p.cum[mid] <= s) lo = mid; else hi = mid - 1; }
    const i = lo, j = (i + 1) % p.pts.length;
    const t = (s - p.cum[i]) / ((p.cum[i + 1] - p.cum[i]) || 1);
    return [lerp(p.pts[i][0], p.pts[j][0], t), lerp(p.pts[i][1], p.pts[j][1], t)];
  }

  update(dt, player) {
    for (const car of this.cars) {
      let target = car.path.speed * car.factor;
      // дистанция до машин впереди на той же полосе
      for (const o of this.cars) {
        if (o === car || o.path !== car.path) continue;
        let ds = o.s - car.s;
        if (car.path.closed) ds = ((ds % car.path.length) + car.path.length) % car.path.length;
        if (ds > 0 && ds < 45) target = Math.min(target, Math.max(0, (ds - 9) * 0.55), o.speed + (ds - 12) * 0.3);
      }
      // игрок впереди
      if (player) {
        const px = player.pos.x - car.x, pz = player.pos.z - car.z;
        const along = px * car.dx + pz * car.dz, lat = Math.abs(px * car.dz - pz * car.dx);
        if (along > 0 && along < 30 && lat < 3.2) target = Math.min(target, Math.max(0, (along - 7) * 0.6));
      }
      if (car.stop > 0) { car.stop -= dt; target = 0; }
      const acc = target > car.speed ? 3.5 : 9;
      car.speed += clamp(target - car.speed, -acc * dt, acc * dt);
      if (!car.path.closed && car.s + car.speed * dt >= car.path.length - 1) car.s = 0;
      car.s += Math.max(0, car.speed) * dt;
      this._place(car);
    }
  }

  dispose() {
    this.group.parent && this.group.parent.remove(this.group);
    this.group.traverse(o => { if (o.isMesh && o.material && o.material.color && !o.material.vertexColors) o.material.dispose(); });
  }
}
