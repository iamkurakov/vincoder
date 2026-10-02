// Игровая сессия: сцена зоны, машина, трафик, режим (чекпоинты, таймер, медали, награды), HUD и миникарта.
import * as THREE from '../vendor/three.module.js';
import { World } from './world.js';
import { Vehicle } from './vehicle.js';
import { Traffic } from './traffic.js';
import { Particles, ChaseCamera } from './effects.js';
import { SURFACES, getRules, VEHICLES } from './data.js';
import { save, persist, level } from './save.js';
import { clamp, lerp } from './noise.js';

const $ = (id) => document.getElementById(id);
export const fmtTime = (t) => {
  if (!isFinite(t)) return '--:--.--';
  t = Math.max(0, t);
  const m = Math.floor(t / 60), s = t - m * 60;
  return `${m}:${s < 10 ? '0' : ''}${s.toFixed(2)}`;
};
const MEDALS = ['', 'Бронза', 'Серебро', 'Золото'];

function skyDome(top, bottom) {
  const geo = new THREE.SphereGeometry(1, 24, 12);
  const mat = new THREE.ShaderMaterial({
    side: THREE.BackSide, depthWrite: false, fog: false,
    uniforms: { top: { value: new THREE.Color(top) }, bottom: { value: new THREE.Color(bottom) } },
    vertexShader: 'varying vec3 vp; void main(){ vp = position; gl_Position = projectionMatrix * modelViewMatrix * vec4(position,1.0); }',
    fragmentShader: 'uniform vec3 top; uniform vec3 bottom; varying vec3 vp; void main(){ float h = clamp(vp.y*1.6+0.05,0.0,1.0); gl_FragColor = vec4(mix(bottom, top, pow(h,0.7)),1.0); }',
  });
  const m = new THREE.Mesh(geo, mat);
  m.renderOrder = -10;
  m.frustumCulled = false;
  return m;
}

export class Game {
  constructor(app, zone, mode) {
    this.app = app; this.zone = zone; this.mode = mode;
    const quality = app.quality();
    const scene = this.scene = new THREE.Scene();
    const world = this.world = new World(zone.id, quality);
    scene.add(world.group);
    const sky = world.sky;
    scene.fog = new THREE.Fog(sky.fog, sky.fogNear, sky.fogFar);
    this.sky = skyDome(sky.top, sky.bottom);
    this.sky.scale.setScalar(sky.fogFar * 1.5);
    scene.add(this.sky);
    scene.add(new THREE.HemisphereLight(0xdfeeff, 0x5a4a32, 1.3));
    const sun = this.sun = new THREE.DirectionalLight(0xfff1d8, 2.3);
    this.sunDir = new THREE.Vector3(...sky.sun).normalize();
    if (quality !== 'low') {
      sun.castShadow = true;
      const sz = quality === 'high' ? 2048 : 1024;
      sun.shadow.mapSize.set(sz, sz);
      const e = quality === 'high' ? 70 : 55;
      Object.assign(sun.shadow.camera, { left: -e, right: e, top: e, bottom: -e, near: 1, far: 400 });
      sun.shadow.bias = -0.0006; sun.shadow.normalBias = 0.04;
    }
    scene.add(sun, sun.target);

    this.route = world.routes[mode === 'CityChallenge' ? 'city' : 'default'];
    this.rules = getRules(zone, mode, this.route ? this.route.length : 1000);
    if (!this.route) this.rules.useCheckpoints = false;

    const def = VEHICLES.find(v => v.id === save.selected);
    this.def = def;
    this.car = new Vehicle(def, save.cars[def.id], world);
    scene.add(this.car.mesh);
    this.traffic = new Traffic(world, scene, this.rules.traffic);
    this.traffic.onHonk = () => app.audio.honk();
    this.car.traffic = this.traffic;
    this.car.onImpact = (sp, kind) => this._onImpact(sp, kind);
    world.onConeHit = () => app.audio.burst(0.15, 1600, 0.15);

    this.particles = new Particles(scene, quality === 'low' ? 160 : 320);
    this.cam = new ChaseCamera(app.camera, world);
    this.cam.mode = save.settings.camera || 0;
    app.camera.far = sky.fogFar * 1.6;
    app.camera.updateProjectionMatrix();

    this._buildCheckpoints();
    this._buildRepairPads();
    this._buildMinimap();
    this.restart();
  }

  // ---------- Чекпоинты ----------
  _buildCheckpoints() {
    this.gates = [];
    if (!this.rules.useCheckpoints) return;
    const W = this.zone.id === 'Highway' ? 10 : this.zone.id === 'Offroad' ? 11 : this.zone.id === 'Forest' ? 11 : 15;
    this.gateW = W;
    const postGeo = new THREE.BoxGeometry(0.45, 6, 0.45).translate(0, 3, 0);
    const topGeo = new THREE.BoxGeometry(W + 0.4, 0.5, 0.45);
    const beamGeo = new THREE.CylinderGeometry(0.7, 0.7, 60, 10, 1, true).translate(0, 30, 0);
    this.route.cps.forEach((cp, i) => {
      const last = i === this.route.cps.length - 1;
      const mat = new THREE.MeshStandardMaterial({ color: last ? 0xff8a00 : 0x2ee66b, emissive: last ? 0xff6a00 : 0x1fd65a, emissiveIntensity: 0.5, transparent: true, opacity: 1 });
      const beamMat = new THREE.MeshBasicMaterial({ color: last ? 0xffa030 : 0x40ff80, transparent: true, opacity: 0.22, depthWrite: false, side: THREE.DoubleSide });
      const g = new THREE.Group();
      const y = this.world.heightAt(cp.x, cp.z);
      const rx = Math.cos(cp.yaw), rz = -Math.sin(cp.yaw);
      for (const s of [-1, 1]) {
        const p = new THREE.Mesh(postGeo, mat);
        const px = cp.x + rx * s * W / 2, pz = cp.z + rz * s * W / 2;
        p.position.set(px, this.world.heightAt(px, pz) - 0.3, pz);
        p.castShadow = true;
        g.add(p);
      }
      const top = new THREE.Mesh(topGeo, mat);
      top.position.set(cp.x, y + 6, cp.z); top.rotation.y = cp.yaw;
      g.add(top);
      const beam = new THREE.Mesh(beamGeo, beamMat);
      beam.position.set(cp.x, y, cp.z);
      g.add(beam);
      this.scene.add(g);
      this.gates.push({ cp, g, mat, beam, beamMat, last });
    });
  }

  _refreshGates() {
    this.gates.forEach((gt, i) => {
      const passed = i < this.cpIndex;
      const next = i === this.cpIndex;
      gt.g.visible = !passed;
      gt.beam.visible = next;
      gt.mat.opacity = next ? 1 : 0.35;
      gt.mat.emissiveIntensity = next ? 0.9 : 0.1;
    });
  }

  _buildRepairPads() {
    this.repairs = [];
    if (this.mode !== 'FreeRide') return;
    for (const r of this.world.repairPoints) {
      const g = new THREE.Group();
      const y = this.world.heightAt(r.x, r.z);
      const pad = new THREE.Mesh(new THREE.CylinderGeometry(5, 5, 0.12, 32), new THREE.MeshStandardMaterial({ color: 0x1e88e5, emissive: 0x0d47a1, emissiveIntensity: 0.6, transparent: true, opacity: 0.85 }));
      pad.position.set(r.x, y + 0.08, r.z);
      const sign = new THREE.Mesh(new THREE.BoxGeometry(2.4, 2.4, 0.2), new THREE.MeshStandardMaterial({ color: 0xffffff, emissive: 0x1e88e5, emissiveIntensity: 0.4 }));
      sign.position.set(r.x, y + 5, r.z);
      const pole = new THREE.Mesh(new THREE.CylinderGeometry(0.1, 0.1, 4), new THREE.MeshStandardMaterial({ color: 0x777777 }));
      pole.position.set(r.x, y + 2, r.z);
      g.add(pad, sign, pole);
      this.scene.add(g);
      this.repairs.push({ x: r.x, z: r.z, sign, cool: 0 });
    }
  }

  // ---------- Миникарта ----------
  _buildMinimap() {
    const W = this.world, S = 512;
    const c = document.createElement('canvas'); c.width = c.height = S;
    const g = c.getContext('2d');
    const k = S / W.size;
    const tx = (x) => (x + W.half) * k;
    // рельеф по покрытиям
    const img = g.createImageData(S, S);
    for (let j = 0; j < S; j++) for (let i = 0; i < S; i++) {
      const x = -W.half + (i + 0.5) / k, z = -W.half + (j + 0.5) / k;
      const gi = Math.round((x + W.half) / W.cell), gj = Math.round((z + W.half) / W.cell);
      const s = W.S[gj * W.N + gi];
      const col = SURFACES[s].color;
      const h = W.H[gj * W.N + gi];
      const sh = 0.75 + clamp((h - (W.H[Math.max(0, gj - 1) * W.N + gi])) * 0.12, -0.2, 0.2);
      const o = (j * S + i) * 4;
      img.data[o] = col[0] * 255 * sh; img.data[o + 1] = col[1] * 255 * sh; img.data[o + 2] = col[2] * 255 * sh; img.data[o + 3] = 255;
    }
    g.putImageData(img, 0, 0);
    for (const r of W.roads) {
      g.strokeStyle = r.ribbon ? (r.style === 'gravel' ? '#b9a98a' : '#e8e8e8') : '#8a6a44';
      g.lineWidth = Math.max(2, r.width * k * 1.2);
      g.lineJoin = 'round';
      g.beginPath();
      r.xs.forEach((x, i) => (i ? g.lineTo(tx(x), tx(r.zs[i])) : g.moveTo(tx(x), tx(r.zs[i]))));
      if (r.closed) g.closePath();
      g.stroke();
    }
    g.fillStyle = '#5d6470';
    for (const b of W.buildings) g.fillRect(tx(b.x0), tx(b.z0), (b.x1 - b.x0) * k, (b.z1 - b.z0) * k);
    this.mapCanvas = c; this.mapK = k;
  }

  _drawMinimap() {
    const cv = $('minimap'), g = cv.getContext('2d');
    const S = cv.width, W = this.world;
    const viewR = this.zone.id === 'OpenWorld' || this.zone.id === 'Highway' ? 380 : 230;
    const scale = (S / 2) / viewR;
    const car = this.car;
    g.save();
    g.clearRect(0, 0, S, S);
    g.beginPath(); g.arc(S / 2, S / 2, S / 2 - 2, 0, Math.PI * 2); g.clip();
    g.fillStyle = '#223'; g.fillRect(0, 0, S, S);
    g.translate(S / 2, S / 2);
    g.rotate(this.cam.yaw);
    g.scale(-scale, -scale);
    g.translate(-car.pos.x, -car.pos.z);
    g.imageSmoothingEnabled = true;
    g.drawImage(this.mapCanvas, -W.half, -W.half, W.size, W.size);
    // трафик
    g.fillStyle = '#ffd54a';
    for (const t of this.traffic.cars) { g.beginPath(); g.arc(t.x, t.z, 3 / scale, 0, 6.3); g.fill(); }
    // ремонт
    g.fillStyle = '#1e88e5';
    for (const r of this.repairs) { g.beginPath(); g.arc(r.x, r.z, 6 / scale, 0, 6.3); g.fill(); }
    // чекпоинты
    if (this.rules.useCheckpoints) {
      this.gates.forEach((gt, i) => {
        if (i < this.cpIndex) return;
        g.fillStyle = i === this.cpIndex ? '#40ff80' : 'rgba(255,255,255,0.55)';
        if (gt.last && i === this.cpIndex) g.fillStyle = '#ffa030';
        g.beginPath(); g.arc(gt.cp.x, gt.cp.z, (i === this.cpIndex ? 7 : 4) / scale, 0, 6.3); g.fill();
      });
    }
    g.restore();
    // игрок
    g.save();
    g.translate(S / 2, S / 2);
    g.rotate(this.cam.yaw - car.yaw);
    g.fillStyle = '#ff3b30'; g.strokeStyle = '#fff'; g.lineWidth = 2;
    g.beginPath(); g.moveTo(0, -9); g.lineTo(6, 7); g.lineTo(0, 3); g.lineTo(-6, 7); g.closePath(); g.fill(); g.stroke();
    g.restore();
    g.strokeStyle = 'rgba(255,255,255,0.6)'; g.lineWidth = 3;
    g.beginPath(); g.arc(S / 2, S / 2, S / 2 - 2, 0, Math.PI * 2); g.stroke();
  }

  // ---------- Заезд ----------
  restart() {
    const W = this.world, sp = W.spawn;
    this.car.place(sp.x, sp.z, sp.yaw);
    this.car.health = this.car.health <= 0 ? this.car.maxHealth * 0.25 : this.car.health;
    this.startHealth = this.car.health;
    this.phase = 'countdown'; this.countdown = 3.2; this.lastCount = 4;
    this.elapsed = 0; this.penalty = 0; this.bonus = 0; this.heavy = 0;
    this.distance = 0; this.progress = 0; this.lastS = null;
    this.cpIndex = 0;
    this.respawn = { x: sp.x, z: sp.z, yaw: sp.yaw };
    this.upsideTimer = 0; this.shake = 0; this.result = null;
    this.particles.clear();
    if (W.cones.length) W.resetCones();
    this.cam.init = false;
    this._refreshGates();
    this.app.hideOverlays();
    this.message('', 0);
    $('hud').hidden = false;
    this._hudStatic();
  }

  message(text, dur = 1.6, cls = '') {
    const el = $('hud-msg');
    el.textContent = text;
    el.className = cls;
    el.style.opacity = text ? 1 : 0;
    clearTimeout(this._msgT);
    if (text && dur) this._msgT = setTimeout(() => { el.style.opacity = 0; }, dur * 1000);
  }

  _hudStatic() {
    $('hud-mode').textContent = `${this.zone.name} · ${this.rules.title}`;
    $('hud-arrow').hidden = !this.rules.useCheckpoints;
  }

  _onImpact(sp, kind) {
    const a = this.app.audio;
    a.burst(clamp(sp / 20, 0.15, 0.9), kind === 'tree' || kind === 'building' ? 500 : 900, 0.35);
    this.shake = Math.max(this.shake, clamp(sp / 25, 0, 0.6));
    if (this.phase !== 'playing') return;
    const R = this.rules;
    const soft = { lamp: 0.35, wall: 0.5, car: 0.8 }[kind] || 1;
    if (R.damage) this.car.applyDamage(4 + (sp - 4) * soft);
    if (sp * soft > 9) {
      this.heavy++;
      if (R.collisionPenalty) { this.penalty += R.collisionPenalty; this.message(`Удар! +${R.collisionPenalty} c`, 1.2, 'warn'); }
      if (R.maxHeavy > 0) {
        if (this.heavy > R.maxHeavy) return this.finish(false, 'Слишком много сильных ударов');
        if (!R.collisionPenalty) this.message(`Сильный удар ${this.heavy}/${R.maxHeavy}`, 1.2, 'warn');
      }
    }
    if (R.damage && this.car.health <= 0 && this.mode !== 'FreeRide') this.finish(false, 'Машина разбита');
    else if (this.car.health <= 0) this.message('Машина разбита — едем на аварийном ходу', 2.5, 'warn');
  }

  doRespawn() {
    if (this.phase === 'finished') return;
    const car = this.car;
    if (this.mode === 'FreeRide' || this.mode === 'HighwayRun') {
      const nr = this.world.nearestRoad(car.pos.x, car.pos.z);
      if (nr && nr.d < 40) {
        const p = this.world.pointOnRoad(nr.road, nr.s, nr.road.width > 14 ? 4.5 : nr.road.width > 10 ? 3.5 : 0);
        let yaw = p.yaw;
        if (Math.cos(yaw - car.yaw) < 0) { const q = this.world.pointOnRoad(nr.road, nr.s, nr.road.width > 10 ? (nr.road.width > 14 ? 4.5 : 3.5) : 0, true); car.place(q.x, q.z, q.yaw); }
        else car.place(p.x, p.z, yaw);
      } else car.place(car.pos.x, car.pos.z, car.yaw);
      this.lastS = null;
    } else {
      car.place(this.respawn.x, this.respawn.z, this.respawn.yaw);
      if (this.phase === 'playing' && this.rules.respawnPenalty) { this.penalty += this.rules.respawnPenalty; this.message(`Возврат: +${this.rules.respawnPenalty} c`, 1.2, 'warn'); }
    }
    this.cam.init = false;
  }

  finish(success, reason = '') {
    if (this.phase === 'finished') return;
    this.phase = 'finished';
    const R = this.rules;
    const time = this.elapsed + this.penalty;
    const key = `${this.zone.id}:${this.mode}`;
    const prev = save.best[key];
    let medal = 0, reward = 0, xp = 0, newRecord = false;
    const km = this.distance / 1000;
    if (this.mode === 'FreeRide') {
      reward = Math.round(km * R.creditsPerKm);
      xp = Math.round(km * R.xpPerKm);
      success = true;
    } else if (success) {
      medal = time <= R.gold ? 3 : time <= R.silver ? 2 : time <= R.bronze ? 1 : 0;
      reward = Math.round(R.reward * [1, 1.1, 1.25, 1.5][medal] / 5) * 5;
      xp = Math.round(R.xp * [1, 1.1, 1.25, 1.5][medal]);
      if (!prev || time < prev.time) { newRecord = !!prev; save.best[key] = { time, medal: Math.max(medal, prev ? prev.medal : 0) }; }
      else if (medal > prev.medal) prev.medal = medal;
    } else {
      xp = Math.round(R.xp * 0.15);
    }
    const lvlBefore = level();
    save.credits += reward;
    save.xp += xp;
    save.totalKm = (save.totalKm || 0) + km;
    save.cars[this.def.id].health = clamp(this.car.health / this.car.maxHealth, 0, 1);
    persist();
    const lvlAfter = level();
    this.result = { success, reason, time, best: save.best[key] ? save.best[key].time : null, medal, reward, xp, newRecord, km, levelUp: lvlAfter > lvlBefore ? lvlAfter : 0 };
    if (success && this.mode !== 'FreeRide') this.app.audio.tone([523, 659, 784, 1046], 0.12, 'triangle', 0.15);
    else if (!success) this.app.audio.tone([392, 330, 262], 0.16, 'sawtooth', 0.08);
    setTimeout(() => this.app.showResult(this), success ? 900 : 600);
  }

  // ---------- Кадр ----------
  update(dt, input, actions) {
    const car = this.car, R = this.rules;
    for (const a of actions) {
      if (a === 'reset') this.doRespawn();
      if (a === 'camera') this.cam.cycle();
    }
    let inp = input;
    if (this.phase === 'countdown') {
      this.countdown -= dt;
      const n = Math.ceil(this.countdown);
      if (n !== this.lastCount && n > 0 && n <= 3) { this.lastCount = n; this.message(String(n), 0.9, 'count'); this.app.audio.tone([440], 0.12, 'square', 0.08); }
      if (this.countdown <= 0) { this.phase = 'playing'; this.message('Старт!', 1, 'count go'); this.app.audio.tone([880], 0.25, 'square', 0.09); }
      inp = { steer: input.steer, throttle: 0, brake: 0, handbrake: true };
    } else if (this.phase === 'finished') {
      inp = { steer: 0, throttle: 0, brake: 1, handbrake: false };
    }
    car.update(dt, inp, R.damage);
    this.world.updateCones(dt, car);
    this.traffic.update(dt, car);

    if (this.phase === 'playing') {
      this.elapsed += dt;
      const hsp = Math.hypot(car.vel.x, car.vel.z);
      this.distance += hsp * dt;
      this._checkpoints();
      this._highway();
      this._repair(dt);
      if (R.timeLimit && this.timeLeft() <= 0) this.finish(false, 'Время вышло');
    }
    // перевёрнута?
    if (car.up.y < 0.25 && car.vel.length() < 3) { this.upsideTimer += dt; if (this.upsideTimer > 2.5) { this.message('Нажмите R, чтобы вернуться на дорогу', 2); this.upsideTimer = -3; } }
    else if (this.upsideTimer > 0) this.upsideTimer = 0;

    this._effects(dt);
    this.cam.update(dt, car);
    if (this.shake > 0) {
      this.app.camera.position.x += (Math.random() - 0.5) * this.shake;
      this.app.camera.position.y += (Math.random() - 0.5) * this.shake;
      this.shake = Math.max(0, this.shake - dt * 2);
    }
    this.particles.update(dt, this.app.camera);
    this.sky.position.copy(this.app.camera.position);
    this.sun.position.copy(car.pos).addScaledVector(this.sunDir, 150);
    this.sun.target.position.copy(car.pos);
    this._hud();
  }

  timeLeft() { return this.rules.timeLimit + this.bonus - this.elapsed - this.penalty; }

  _checkpoints() {
    if (!this.rules.useCheckpoints) return;
    const gt = this.gates[this.cpIndex];
    if (!gt) return;
    const dx = this.car.pos.x - gt.cp.x, dz = this.car.pos.z - gt.cp.z;
    if (Math.hypot(dx, dz) < this.gateW / 2 + 2.5) {
      this.respawn = { x: gt.cp.x, z: gt.cp.z, yaw: gt.cp.yaw };
      this.cpIndex++;
      if (this.cpIndex >= this.gates.length) return this.finish(true);
      let msg = `Чекпоинт ${this.cpIndex}/${this.gates.length}`;
      if (this.rules.timeBonus) { this.bonus += this.rules.timeBonus; msg += `  +${this.rules.timeBonus} c`; }
      this.message(msg, 1.3, 'good');
      this.app.audio.tone([660, 990], 0.08, 'sine', 0.16);
      this._refreshGates();
    }
  }

  _highway() {
    if (this.mode !== 'HighwayRun') return;
    const road = this.world.progressRoad;
    const nr = this.world.nearestRoad(this.car.pos.x, this.car.pos.z);
    if (nr && nr.road === road && nr.d < road.width / 2 + 3) {
      if (this.lastS !== null) {
        let ds = nr.s - this.lastS;
        if (ds > road.length / 2) ds -= road.length; else if (ds < -road.length / 2) ds += road.length;
        if (ds > 0 && ds < 60) this.progress += ds;
      }
      this.lastS = nr.s;
    } else this.lastS = null;
    if (this.progress >= this.rules.targetKm * 1000) this.finish(true);
  }

  _repair(dt) {
    for (const r of this.repairs) {
      r.sign.rotation.y += dt;
      if (r.cool > 0) { r.cool -= dt; continue; }
      if (Math.hypot(this.car.pos.x - r.x, this.car.pos.z - r.z) < 5 && Math.abs(this.car.speed) < 3 && this.car.health < this.car.maxHealth) {
        this.car.health = this.car.maxHealth;
        r.cool = 5;
        this.message('Машина отремонтирована', 1.5, 'good');
        this.app.audio.tone([523, 784], 0.1, 'triangle', 0.15);
      }
    }
  }

  _effects(dt) {
    const car = this.car, P = this.particles;
    const sp = car.vel.length();
    for (const w of car.wheels) {
      if (!w.contact) continue;
      const S = SURFACES[w.surface];
      let rate = 0, color = 0xdddddd, size = 0.8;
      if (S.dust !== null && sp > 3) { rate = Math.min(1, sp / 25) * 0.6 + w.skid * 0.4; color = S.dust; size = w.surface === 8 ? 0.6 : 1.1; }
      else if (S.squeal && w.skid > 0.45 && sp > 4) { rate = (w.skid - 0.45) * 1.5; color = 0xe8e8e8; size = 1.0; }
      if (Math.random() < rate * dt * 40) {
        const p = w.world;
        P.emit(p.x, p.y - car.spec.rest * 0.6, p.z, -car.vel.x * 0.15 + (Math.random() - 0.5), 0.6 + Math.random() * (w.surface === 8 ? 2.5 : 0.8), -car.vel.z * 0.15 + (Math.random() - 0.5), color, size, 1.1, 2.5);
      }
    }
    const hr = car.healthRatio;
    if (hr < 0.3 && Math.random() < dt * (hr <= 0 ? 25 : 10)) {
      const p = car.pos.clone().addScaledVector(car.fwd, car.spec.l * 0.38).addScaledVector(car.up, 0.5);
      P.emit(p.x, p.y, p.z, (Math.random() - 0.5) * 0.5, 1.5, (Math.random() - 0.5) * 0.5, hr <= 0 ? 0x222222 : 0x777777, 0.7, 1.6, 3);
    }
  }

  _hud() {
    const car = this.car, R = this.rules;
    const kmh = Math.round(Math.abs(car.speed) * 3.6);
    this._set('hud-speed', kmh);
    this._set('hud-gear', car.gear < 0 ? 'R' : (Math.abs(car.speed) < 0.5 && car.throttleOut === 0 ? 'N' : car.gear));
    $('hud-rpm').style.width = `${clamp((car.rpm - 800) / 6400, 0, 1) * 100}%`;
    const hr = clamp(car.healthRatio, 0, 1);
    const hb = $('hud-health');
    hb.style.width = `${hr * 100}%`;
    hb.style.background = hr > 0.6 ? '#3ddc84' : hr > 0.3 ? '#ffc107' : '#ff4d4d';
    const w0 = car.wheels.find(w => w.contact);
    this._set('hud-surface', w0 ? SURFACES[w0.surface].name : 'В воздухе');

    let main = '', sub = '';
    if (this.mode === 'FreeRide') {
      main = `${(this.distance / 1000).toFixed(2)} км`;
      sub = `+${Math.round(this.distance / 1000 * R.creditsPerKm)} ₵ · R — на дорогу`;
    } else if (this.mode === 'HighwayRun') {
      main = fmtTime(Math.max(0, this.timeLeft()));
      sub = `${(this.progress / 1000).toFixed(2)} / ${R.targetKm} км`;
    } else if (R.timeLimit) {
      main = fmtTime(Math.max(0, this.timeLeft()));
      sub = `Чекпоинт ${this.cpIndex}/${this.gates.length}`;
    } else {
      main = fmtTime(this.elapsed + this.penalty);
      sub = `Чекпоинт ${this.cpIndex}/${this.gates.length}`;
      const best = save.best[`${this.zone.id}:${this.mode}`];
      if (best) sub += ` · рекорд ${fmtTime(best.time)}`;
    }
    if (R.maxHeavy) sub += ` · удары ${this.heavy}/${R.maxHeavy}`;
    this._set('hud-main', main);
    this._set('hud-sub', sub);
    $('hud-main').classList.toggle('low', !!R.timeLimit && this.timeLeft() < 10 && this.phase === 'playing');

    if (R.useCheckpoints && this.gates[this.cpIndex]) {
      const cp = this.gates[this.cpIndex].cp;
      const dx = cp.x - car.pos.x, dz = cp.z - car.pos.z;
      const rel = Math.atan2(dx, dz) - this.cam.yaw;
      $('hud-arrow-i').style.transform = `rotate(${-rel}rad)`;
      this._set('hud-dist', `${Math.round(Math.hypot(dx, dz))} м`);
    }
    if (save.settings.showFps) this._set('hud-fps', `${Math.round(this.app.fps)} FPS`);
    this._mm = (this._mm || 0) + 1;
    if (this._mm % 2 === 0) this._drawMinimap();
  }

  _set(id, v) {
    const el = this._els || (this._els = {});
    const e = el[id] || (el[id] = $(id));
    const s = String(v);
    if (e._v !== s) { e.textContent = s; e._v = s; }
  }

  dispose() {
    clearTimeout(this._msgT);
    this.traffic.dispose();
    this.world.dispose();
    this.scene.traverse(o => { if (o.geometry) o.geometry.dispose(); });
  }
}
