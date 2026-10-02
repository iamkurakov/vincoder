// Частицы (пыль, брызги, дым) и камера.
import * as THREE from '../vendor/three.module.js';
import { clamp, lerp } from './noise.js';

export class Particles {
  constructor(scene, max = 320) {
    this.max = max;
    const mat = new THREE.MeshBasicMaterial({ transparent: true, opacity: 0.4, depthWrite: false, map: Particles.puff() });
    this.mesh = new THREE.InstancedMesh(new THREE.PlaneGeometry(1, 1), mat, max);
    this.mesh.frustumCulled = false;
    this.mesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
    this.p = [];
    const c = new THREE.Color(1, 1, 1);
    for (let i = 0; i < max; i++) { this.p.push({ x: 0, y: 0, z: 0, vx: 0, vy: 0, vz: 0, life: 0, max: 1, size: 1, grow: 1 }); this.mesh.setColorAt(i, c); }
    this.next = 0;
    this._m = new THREE.Matrix4(); this._s = new THREE.Vector3(); this._v = new THREE.Vector3(); this._c = new THREE.Color();
    scene.add(this.mesh);
  }

  static puff() {
    if (Particles._tex) return Particles._tex;
    const c = document.createElement('canvas'); c.width = c.height = 64;
    const g = c.getContext('2d');
    const gr = g.createRadialGradient(32, 32, 2, 32, 32, 30);
    gr.addColorStop(0, 'rgba(255,255,255,1)'); gr.addColorStop(1, 'rgba(255,255,255,0)');
    g.fillStyle = gr; g.fillRect(0, 0, 64, 64);
    Particles._tex = new THREE.CanvasTexture(c);
    return Particles._tex;
  }

  emit(x, y, z, vx, vy, vz, color, size = 1, life = 1.2, grow = 2.5) {
    const i = this.next; this.next = (this.next + 1) % this.max;
    const p = this.p[i];
    p.x = x; p.y = y; p.z = z; p.vx = vx; p.vy = vy; p.vz = vz; p.life = life; p.max = life; p.size = size; p.grow = grow;
    this.mesh.setColorAt(i, this._c.setHex(color));
    this.mesh.instanceColor.needsUpdate = true;
  }

  update(dt, camera) {
    const q = camera.quaternion;
    for (let i = 0; i < this.max; i++) {
      const p = this.p[i];
      if (p.life <= 0) { this._m.makeScale(0, 0, 0); this.mesh.setMatrixAt(i, this._m); continue; }
      p.life -= dt;
      p.vy -= 0.6 * dt; p.vx *= 1 - 1.5 * dt; p.vz *= 1 - 1.5 * dt;
      p.x += p.vx * dt; p.y += p.vy * dt; p.z += p.vz * dt;
      const t = clamp(p.life / p.max, 0, 1);
      const s = p.size * (1 + (1 - t) * p.grow) * Math.min(1, t * 3);
      this._m.compose(this._v.set(p.x, p.y, p.z), q, this._s.set(s, s, s));
      this.mesh.setMatrixAt(i, this._m);
    }
    this.mesh.instanceMatrix.needsUpdate = true;
  }

  clear() { for (const p of this.p) p.life = 0; }
}

export class ChaseCamera {
  constructor(camera, world) {
    this.cam = camera; this.world = world;
    this.mode = 0;
    this.pos = new THREE.Vector3(); this.look = new THREE.Vector3();
    this.yaw = 0; this.init = false;
  }
  cycle() { this.mode = (this.mode + 1) % 3; this.init = false; }

  update(dt, car) {
    const f = car.fwd;
    const sp = car.vel.length();
    let fy = Math.atan2(f.x, f.z);
    // при движении назад камера не разворачивается
    if (!this.init) this.yaw = fy;
    let d = fy - this.yaw;
    d = Math.atan2(Math.sin(d), Math.cos(d));
    this.yaw += d * (1 - Math.exp(-dt * (this.mode === 2 ? 30 : 4.5)));
    const portrait = this.cam.aspect < 1 ? 1 : 0;
    const dist = (this.mode === 0 ? 6.8 + sp * 0.03 : this.mode === 1 ? 10.5 + sp * 0.04 : 0) + (this.mode < 2 ? portrait * 3 : 0);
    const height = this.mode === 0 ? 2.3 : this.mode === 1 ? 3.8 : 0;
    const sx = Math.sin(this.yaw), sz = Math.cos(this.yaw);
    const target = new THREE.Vector3();
    if (this.mode === 2) {
      target.copy(car.pos).addScaledVector(car.up, car.spec.h * 0.55 - car.spec.comH + 0.55).addScaledVector(car.fwd, 0.2);
      this.pos.copy(target);
      this.look.copy(target).addScaledVector(car.fwd, 10);
      this.cam.position.copy(this.pos);
      this.cam.up.copy(car.up);
      this.cam.lookAt(this.look);
    } else {
      target.set(car.pos.x - sx * dist, car.pos.y + height, car.pos.z - sz * dist);
      const gy = this.world.heightAt(target.x, target.z) + 1.0;
      if (target.y < gy) target.y = gy;
      if (!this.init) this.pos.copy(target);
      const k = 1 - Math.exp(-dt * 10);
      this.pos.lerp(target, k);
      this.pos.y = Math.max(this.pos.y, this.world.heightAt(this.pos.x, this.pos.z) + 0.8);
      this.look.set(car.pos.x + sx * 2.5, car.pos.y + 0.9, car.pos.z + sz * 2.5);
      this.cam.position.copy(this.pos);
      this.cam.up.set(0, 1, 0);
      this.cam.lookAt(this.look);
    }
    this.init = true;
    const fov = lerp(62, 74, clamp(sp / 60, 0, 1)) + (this.cam.aspect < 1 ? 14 : 0);
    if (Math.abs(this.cam.fov - fov) > 0.05) { this.cam.fov = fov; this.cam.updateProjectionMatrix(); }
  }
}
