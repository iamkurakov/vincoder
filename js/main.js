// Точка входа: рендерер, меню, гараж, настройки, витрина машины и главный цикл.
import * as THREE from '../vendor/three.module.js';
import { ZONES, VEHICLES, MODE_INFO, UPGRADES, MAX_UPGRADE, PAINTS, upgradeCost, repairCost, xpForLevel } from './data.js';
import { save, persist, level, resetProgress } from './save.js';
import { Game, fmtTime } from './game.js';
import { Input } from './input.js';
import { Audio } from './audio.js';
import { buildCarModel, vehicleSpec } from './vehicle.js';

const $ = (id) => document.getElementById(id);
const DRIVE = { FWD: 'передний', RWD: 'задний', AWD: 'полный' };
const ZONE_ART = {
  TestTrack: 'linear-gradient(160deg,#ff9a3c 0%,#6d8f45 55%,#3b4a2c 100%)',
  Highway: 'linear-gradient(160deg,#7fb2e6 0%,#56708a 50%,#2f3a46 100%)',
  City: 'linear-gradient(160deg,#9fb3c4 0%,#5d6470 55%,#2b2f38 100%)',
  Forest: 'linear-gradient(160deg,#a9c79a 0%,#2f5a2f 55%,#1b2e1c 100%)',
  Offroad: 'linear-gradient(160deg,#e4d9c0 0%,#a07d4f 50%,#4b3824 100%)',
  OpenWorld: 'linear-gradient(160deg,#9fd0ff 0%,#4f8a4a 45%,#8a6a44 100%)',
};
const isTouch = () => matchMedia('(pointer: coarse)').matches || 'ontouchstart' in window;

class App {
  constructor() {
    const canvas = $('c');
    const q0 = this.quality();
    this.renderer = new THREE.WebGLRenderer({ canvas, antialias: q0 !== 'low', powerPreference: 'high-performance' });
    this.renderer.shadowMap.enabled = true;
    this.renderer.shadowMap.type = THREE.PCFShadowMap;
    this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
    this.renderer.toneMappingExposure = 1.0;
    this.camera = new THREE.PerspectiveCamera(62, 1, 0.1, 2000);
    this.input = new Input();
    this.input.bindTouch($('ui'));
    this.audio = new Audio();
    this.audio.setVolume(save.settings.volume);
    this.input.sens = save.settings.steerSens;
    this.state = 'menu';
    this.game = null;
    this.fps = 60;
    this.screen = 'main';
    this.garageSel = save.selected;
    this._buildShowroom();
    this._bindUI();
    this.resize();
    window.addEventListener('resize', () => this.resize());
    document.addEventListener('visibilitychange', () => { if (document.hidden && this.state === 'game') this.pause(); });
    // звук можно запустить только после жеста пользователя
    const unlock = () => this.audio.init();
    window.addEventListener('pointerdown', unlock); window.addEventListener('keydown', unlock);
    this.go('main');
    this.last = performance.now();
    this.renderer.setAnimationLoop((t) => this.tick(t));
  }

  quality() {
    const q = save.settings.quality;
    if (q !== 'auto') return q;
    return isTouch() ? 'medium' : 'high';
  }

  resize() {
    const w = window.innerWidth, h = window.innerHeight;
    const q = this.quality();
    const dpr = window.devicePixelRatio || 1;
    this.renderer.setPixelRatio(q === 'low' ? Math.min(dpr, 1) * 0.85 : q === 'medium' ? Math.min(dpr, 1.5) : Math.min(dpr, 2));
    this.renderer.setSize(w, h, false);
    this.camera.aspect = w / h;
    this._viewOffset();
    this.camera.updateProjectionMatrix();
  }

  _viewOffset() {
    const w = window.innerWidth, h = window.innerHeight;
    if (this.state === 'menu' && (this.screen === 'main' || this.screen === 'garage')) {
      if (w > 760) this.camera.setViewOffset(w, h, this.screen === 'main' ? -w * 0.17 : -w * 0.02, this.screen === 'garage' ? h * 0.06 : 0, w, h);
      else this.camera.setViewOffset(w, h, 0, this.screen === 'garage' ? h * 0.2 : h * 0.22, w, h);
    } else this.camera.clearViewOffset();
    this.camera.updateProjectionMatrix();
  }

  toast(text) {
    const t = $('toast');
    t.textContent = text; t.classList.add('show');
    clearTimeout(this._toastT);
    this._toastT = setTimeout(() => t.classList.remove('show'), 2200);
  }

  // ---------- Витрина ----------
  _buildShowroom() {
    const s = this.show = new THREE.Scene();
    s.background = new THREE.Color(0x10151f);
    s.fog = new THREE.Fog(0x10151f, 14, 40);
    s.add(new THREE.HemisphereLight(0xbcd2ff, 0x1a1410, 1.4));
    const key = new THREE.DirectionalLight(0xffffff, 3);
    key.position.set(5, 9, 6); key.castShadow = true; key.shadow.mapSize.set(1024, 1024);
    Object.assign(key.shadow.camera, { left: -6, right: 6, top: 6, bottom: -6 });
    s.add(key);
    const rim = new THREE.DirectionalLight(0xff9a3c, 2.2); rim.position.set(-6, 3, -6); s.add(rim);
    const floor = new THREE.Mesh(new THREE.CircleGeometry(40, 48).rotateX(-Math.PI / 2), new THREE.MeshStandardMaterial({ color: 0x161c28, roughness: 0.9 }));
    floor.receiveShadow = true; s.add(floor);
    const table = new THREE.Mesh(new THREE.CylinderGeometry(3.6, 3.7, 0.12, 64), new THREE.MeshStandardMaterial({ color: 0x2a303b, metalness: 0.6, roughness: 0.35 }));
    table.position.y = 0.06; table.receiveShadow = true; s.add(table);
    const ring = new THREE.Mesh(new THREE.TorusGeometry(3.65, 0.03, 8, 96).rotateX(Math.PI / 2), new THREE.MeshBasicMaterial({ color: 0xff8a1f }));
    ring.position.y = 0.12; s.add(ring);
    this.turn = new THREE.Group(); this.turn.position.y = 0.12; s.add(this.turn);
    this.showAngle = 0.6;
    this._showCar(save.selected);
  }

  _showCar(id) {
    const def = VEHICLES.find(v => v.id === id);
    const paint = PAINTS[save.cars[id].paint || 0];
    if (this.showModel) { this.turn.remove(this.showModel.root); }
    const m = buildCarModel(def, paint);
    const S = vehicleSpec(def, {});
    const fx = S.track / 2, fz = S.wb / 2;
    [[fx, fz], [-fx, fz], [fx, -fz], [-fx, -fz]].forEach(([x, z], i) => {
      m.wheels[i].pivot.position.set(x, -S.comH + S.r, z);
      if (i < 2) m.wheels[i].pivot.rotation.y = -0.3;
    });
    m.root.position.y = S.comH;
    this.turn.add(m.root);
    this.showModel = m;
    this.showDef = def;
  }

  // ---------- Экраны ----------
  go(name) {
    this.screen = name;
    for (const s of ['main', 'zones', 'garage', 'settings', 'help']) $('scr-' + s).hidden = s !== name;
    if (name === 'main') this.renderProfile();
    if (name === 'zones') this.renderZones();
    if (name === 'garage') { this.garageSel = save.selected; this.renderGarage(); }
    if (name === 'settings') this.renderSettings();
    if (name !== 'garage') this._showCar(save.selected);
    this._viewOffset();
  }

  renderProfile() {
    const lv = level();
    const a = xpForLevel(lv), b = xpForLevel(lv + 1);
    $('profile').innerHTML = `<div class="row"><span class="lvl">Уровень ${lv}</span><span class="credits">${save.credits.toLocaleString('ru')} ₵</span></div>
      <div class="xpbar"><div style="width:${Math.round((save.xp - a) / (b - a) * 100)}%"></div></div>
      <div class="row small muted" style="margin-top:6px"><span>${save.xp - a} / ${b - a} XP</span><span>${VEHICLES.find(v => v.id === save.selected).name}</span></div>`;
  }

  renderZones() {
    const lv = level();
    const def = VEHICLES.find(v => v.id === save.selected);
    $('zones-car').innerHTML = `${def.name} · ${Math.round(save.cars[def.id].health * 100)}%<br><span style="color:var(--gold)">${save.credits} ₵</span> · ур. ${lv}`;
    $('zone-list').innerHTML = ZONES.map(z => {
      const locked = lv < z.unlock;
      return `<div class="zone ${locked ? 'locked' : ''}" data-zone="${z.id}">
        <div class="art" style="background:${ZONE_ART[z.id]}"></div>
        ${locked ? `<div class="lock">🔒 уровень ${z.unlock}</div>` : ''}
        <div class="body"><h3>${z.name}</h3><p>${z.desc}</p><div class="tags">${z.modes.map(m => MODE_INFO[m].title).join(' · ')}</div></div></div>`;
    }).join('');
    $('zone-list').querySelectorAll('.zone').forEach(el => el.addEventListener('click', () => {
      const z = ZONES.find(x => x.id === el.dataset.zone);
      if (lv < z.unlock) { this.toast(`Зона откроется на уровне ${z.unlock}`); return; }
      this.audio.click();
      this.openModes(z);
    }));
  }

  openModes(z) {
    $('mode-zone').textContent = z.name;
    $('mode-zone-desc').textContent = z.desc;
    $('mode-list').innerHTML = z.modes.map(m => {
      const b = save.best[`${z.id}:${m}`];
      const best = b && m !== 'FreeRide' ? `<div class="best">${b.medal ? `<i class="medal m${b.medal}"></i>` : ''}Рекорд: ${fmtTime(b.time)}</div>` : '';
      return `<div class="mode" data-mode="${m}"><div class="t"><b>${MODE_INFO[m].title}</b><span>${MODE_INFO[m].desc}</span>${best}</div><button class="btn small primary">Старт</button></div>`;
    }).join('');
    $('mode-list').querySelectorAll('.mode').forEach(el => el.addEventListener('click', () => {
      $('mode-panel').hidden = true;
      this.startGame(z, el.dataset.mode);
    }));
    $('mode-panel').hidden = false;
  }

  renderGarage() {
    const lv = level();
    $('garage-credits').innerHTML = `<span style="color:var(--gold);font-weight:700">${save.credits.toLocaleString('ru')} ₵</span> · уровень ${lv}`;
    $('car-list').innerHTML = VEHICLES.map(v => {
      const cs = save.cars[v.id];
      const status = cs.owned ? `<span class="own">${save.selected === v.id ? '✔ выбрана' : 'в гараже'}</span>` : lv < v.level ? `<span>🔒 уровень ${v.level}</span>` : `<span>${v.price.toLocaleString('ru')} ₵</span>`;
      return `<div class="car-item ${this.garageSel === v.id ? 'sel' : ''}" data-car="${v.id}"><b>${v.name}</b>${status}</div>`;
    }).join('');
    $('car-list').querySelectorAll('.car-item').forEach(el => el.addEventListener('click', () => {
      this.garageSel = el.dataset.car; this.audio.click(); this.renderGarage();
    }));
    const v = VEHICLES.find(x => x.id === this.garageSel), cs = save.cars[v.id];
    if (!this.showDef || this.showDef.id !== v.id || this._shownPaint !== cs.paint) { this._showCar(v.id); this._shownPaint = cs.paint; }
    const names = ['Скорость', 'Разгон', 'Проходимость', 'Прочность'];
    const ups = [cs.engine, cs.engine, 0, cs.armor];
    const stats = v.rating.map((r, i) => `<div class="stat"><span>${names[i]}</span><div class="bar2"><div style="width:${Math.min(100, (r + ups[i] * 0.5) * 10)}%"></div></div></div>`).join('')
      + `<div class="stat"><span>Управляемость</span><div class="bar2"><div style="width:${Math.min(100, (v.grip * 7 + cs.handling * 0.5) * 10 - 20)}%"></div></div></div>`;
    let html = `<h3>${v.name}</h3><div class="muted small">${v.desc}</div>
      <div class="small" style="margin:8px 0">Привод: <b>${DRIVE[v.drive]}</b> · ${v.top} км/ч · ${v.mass} кг</div>${stats}`;
    if (cs.owned) {
      const missing = 1 - cs.health;
      const rc = repairCost(v, missing);
      html += `<div class="sep"></div>
        <div class="upg"><span class="n">Состояние: <b>${Math.round(cs.health * 100)}%</b></span>
        <button class="btn small" id="g-repair" ${missing < 0.005 ? 'disabled' : ''}>${missing < 0.005 ? 'Исправна' : `Ремонт ${rc} ₵`}</button></div>`;
      html += UPGRADES.map(u => {
        const lvU = cs[u.id];
        const cost = upgradeCost(v, lvU);
        const pips = Array.from({ length: MAX_UPGRADE }, (_, i) => `<i class="${i < lvU ? 'on' : ''}"></i>`).join('');
        return `<div class="upg" title="${u.desc}"><span class="n">${u.name}</span><div class="pips">${pips}</div>
          <button class="btn small" data-upg="${u.id}" ${lvU >= MAX_UPGRADE ? 'disabled' : ''}>${lvU >= MAX_UPGRADE ? 'Макс.' : `${cost} ₵`}</button></div>`;
      }).join('');
      html += `<div class="sep"></div><div class="small muted">Цвет кузова</div><div class="paints">${PAINTS.map((p, i) =>
        `<div data-paint="${i}" class="${(cs.paint || 0) === i ? 'sel' : ''}" style="background:#${(p || v.color).toString(16).padStart(6, '0')}"></div>`).join('')}</div>`;
      html += save.selected === v.id ? `<button class="btn big" disabled>Выбрана</button>` : `<button class="btn big primary" id="g-select">Выбрать</button>`;
    } else {
      const can = lv >= v.level && save.credits >= v.price;
      const why = lv < v.level ? `Нужен уровень ${v.level}` : save.credits < v.price ? `Не хватает ${v.price - save.credits} ₵` : '';
      html += `<div class="sep"></div><button class="btn big primary" id="g-buy" ${can ? '' : 'disabled'}>Купить за ${v.price.toLocaleString('ru')} ₵</button>${why ? `<div class="small muted" style="text-align:center">${why}</div>` : ''}`;
    }
    $('car-info').innerHTML = html;
    const info = $('car-info');
    const buy = info.querySelector('#g-buy');
    if (buy) buy.onclick = () => {
      save.credits -= v.price; cs.owned = true; cs.health = 1; save.selected = v.id; persist();
      this.audio.tone([523, 784, 1046], 0.1, 'triangle', 0.15); this.toast(`${v.name} теперь в гараже`); this.renderGarage();
    };
    const sel = info.querySelector('#g-select');
    if (sel) sel.onclick = () => { save.selected = v.id; persist(); this.audio.click(); this.renderGarage(); };
    const rep = info.querySelector('#g-repair');
    if (rep) rep.onclick = () => {
      const c = repairCost(v, 1 - cs.health);
      if (save.credits < c) return this.toast('Не хватает кредитов');
      save.credits -= c; cs.health = 1; persist(); this.audio.click(); this.renderGarage();
    };
    info.querySelectorAll('[data-upg]').forEach(b => b.onclick = () => {
      const id = b.dataset.upg, c = upgradeCost(v, cs[id]);
      if (save.credits < c) return this.toast('Не хватает кредитов');
      save.credits -= c; cs[id]++; persist(); this.audio.tone([660, 880], 0.08, 'triangle', 0.12); this.renderGarage();
    });
    info.querySelectorAll('[data-paint]').forEach(p => p.onclick = () => {
      cs.paint = +p.dataset.paint; persist();
      this.showModel.paintMat.color.setHex(PAINTS[cs.paint] || v.color); this._shownPaint = cs.paint;
      this.renderGarage();
    });
  }

  renderSettings() {
    const st = save.settings;
    $('set-quality').value = st.quality;
    $('set-volume').value = st.volume;
    $('set-steer').value = st.steerSens;
    $('set-camera').value = st.camera;
    $('set-touch').value = st.touch;
    $('set-fps').checked = st.showFps;
  }

  _bindUI() {
    document.querySelectorAll('[data-go]').forEach(b => b.addEventListener('click', () => { this.audio.init(); this.audio.click(); this.go(b.dataset.go); }));
    $('mode-close').onclick = () => { $('mode-panel').hidden = true; };
    $('mode-panel').addEventListener('click', (e) => { if (e.target.id === 'mode-panel') $('mode-panel').hidden = true; });
    const st = save.settings;
    $('set-quality').onchange = (e) => { st.quality = e.target.value; persist(); this.resize(); };
    $('set-volume').oninput = (e) => { st.volume = +e.target.value; this.audio.setVolume(st.volume); persist(); };
    $('set-steer').oninput = (e) => { st.steerSens = +e.target.value; this.input.sens = st.steerSens; persist(); };
    $('set-camera').onchange = (e) => { st.camera = +e.target.value; persist(); };
    $('set-touch').onchange = (e) => { st.touch = e.target.value; persist(); };
    $('set-fps').onchange = (e) => { st.showFps = e.target.checked; persist(); $('hud-fps').textContent = ''; };
    $('set-reset').onclick = () => {
      if (!confirm('Сбросить весь прогресс: кредиты, машины, рекорды?')) return;
      resetProgress(); this.toast('Прогресс сброшен'); this.go('main');
    };
    $('settings-back').onclick = () => { this.audio.click(); if (this.settingsFromPause) { this.settingsFromPause = false; $('scr-settings').hidden = true; $('scr-pause').hidden = false; } else this.go('main'); };
    $('p-resume').onclick = () => this.resume();
    $('p-restart').onclick = () => { this.game.restart(); this.state = 'game'; this._touchVisible(true); };
    $('p-respawn').onclick = () => { this.game.doRespawn(); this.resume(); };
    $('p-finish').onclick = () => { $('scr-pause').hidden = true; this.state = 'result'; this.game.finish(true); };
    $('p-menu').onclick = () => this.exitGame('main');
    $('r-again').onclick = () => { $('scr-result').hidden = true; this.game.restart(); this.state = 'game'; this._touchVisible(true); };
    $('r-zones').onclick = () => this.exitGame('zones');
    $('r-garage').onclick = () => this.exitGame('garage');
  }

  // ---------- Игра ----------
  startGame(zone, mode) {
    this.audio.init();
    for (const s of ['main', 'zones', 'garage', 'settings', 'help']) $('scr-' + s).hidden = true;
    $('loading-text').textContent = `Загрузка: ${zone.name}…`;
    $('scr-loading').hidden = false;
    this.state = 'loading';
    this._viewOffset();
    setTimeout(() => {
      try {
        this.game = new Game(this, zone, mode);
      } catch (e) {
        console.error(e);
        $('scr-loading').hidden = true;
        this.state = 'menu'; this.go('zones');
        this.toast('Не удалось загрузить зону: ' + e.message);
        return;
      }
      $('scr-loading').hidden = true;
      this.state = 'game';
      this._viewOffset();
      this._touchVisible(true);
      this.input.takeActions();
    }, 60);
  }

  _touchVisible(on) {
    const t = save.settings.touch;
    const show = on && (t === 'on' || (t === 'auto' && isTouch()));
    $('touch').hidden = !show;
    document.body.classList.toggle('touch', show);
  }

  hideOverlays() { $('scr-pause').hidden = true; $('scr-result').hidden = true; }

  pause() {
    if (this.state !== 'game' || !this.game) return;
    this.state = 'paused';
    $('p-finish').hidden = this.game.mode !== 'FreeRide';
    $('p-respawn').textContent = ['FreeRide', 'HighwayRun'].includes(this.game.mode) ? 'Вернуться на дорогу' : 'Вернуться к чекпоинту';
    $('scr-pause').hidden = false;
    this._touchVisible(false);
  }

  resume() {
    if (!this.game) return;
    $('scr-pause').hidden = true;
    this.state = 'game';
    this._touchVisible(true);
    this.last = performance.now();
  }

  exitGame(to) {
    if (this.game) {
      if (this.game.phase !== 'finished') {
        save.cars[this.game.def.id].health = Math.max(0, Math.min(1, this.game.car.health / this.game.car.maxHealth));
        persist();
      }
      this.game.dispose(); this.game = null;
    }
    this.hideOverlays();
    $('hud').hidden = true;
    this._touchVisible(false);
    this.state = 'menu';
    this.camera.fov = 45; this.camera.far = 200; this.camera.up.set(0, 1, 0); this.camera.updateProjectionMatrix();
    this.go(to);
  }

  showResult(game) {
    if (game !== this.game) return;
    const r = game.result;
    this.state = 'result';
    this._touchVisible(false);
    const free = game.mode === 'FreeRide';
    $('r-title').textContent = free ? 'Поездка завершена' : r.success ? 'Финиш!' : 'Заезд провален';
    $('r-title').style.color = r.success ? 'var(--good)' : 'var(--bad)';
    $('r-reason').textContent = r.reason || (r.newRecord ? 'Новый рекорд!' : '');
    const mc = ['', 'var(--bronze)', 'var(--silver)', 'var(--gold)'][r.medal];
    $('r-medal').innerHTML = r.medal ? `<div class="big-medal" style="background:${mc}"></div>${['', 'Бронза', 'Серебро', 'Золото'][r.medal]}` : '';
    const R = game.rules;
    const rows = [];
    if (free) rows.push(['Пройдено', `${r.km.toFixed(2)} км`]);
    else {
      rows.push(['Время', fmtTime(r.time)]);
      if (game.penalty) rows.push(['Из них штрафы', `${game.penalty} c`]);
      if (r.best) rows.push(['Рекорд', fmtTime(r.best)]);
      rows.push(['Медали', `${fmtTime(R.gold)} / ${fmtTime(R.silver)} / ${fmtTime(R.bronze)}`]);
    }
    rows.push(['Награда', `+${r.reward} ₵`], ['Опыт', `+${r.xp} XP`]);
    if (r.levelUp) rows.push(['Новый уровень', `<b>${r.levelUp}</b>`]);
    rows.push(['Состояние машины', `${Math.round(game.car.healthRatio * 100)}%`]);
    $('r-stats').innerHTML = rows.map(([a, b]) => `<div><span>${a}</span><b>${b}</b></div>`).join('');
    $('r-again').textContent = free ? 'Продолжить кататься' : 'Ещё раз';
    $('scr-result').hidden = false;
    if (r.levelUp) {
      const z = ZONES.filter(z => z.unlock === r.levelUp).map(z => z.name);
      const c = VEHICLES.filter(v => v.level === r.levelUp).map(v => v.name);
      const list = [...z, ...c];
      if (list.length) this.toast(`Открыто: ${list.join(', ')}`);
    }
  }

  // ---------- Цикл ----------
  tick(t) {
    const dt = Math.min(0.05, Math.max(0.0001, (t - this.last) / 1000));
    this.last = t;
    this.fps = this.fps * 0.95 + (1 / dt) * 0.05;
    const actions = this.input.takeActions();
    for (const a of actions) {
      if (a === 'mute') this.toast(this.audio.toggleMute() ? 'Звук выключен' : 'Звук включён');
      if (a === 'pause') { if (this.state === 'game') this.pause(); else if (this.state === 'paused') this.resume(); }
      if (a === 'confirm' && this.state === 'result') $('r-again').click();
    }
    if (this.game && (this.state === 'game' || this.state === 'result')) {
      const inp = this.state === 'game' ? this.input.read() : { steer: 0, throttle: 0, brake: 0, handbrake: false };
      this.game.update(dt, inp, this.state === 'game' ? actions : []);
      this.audio.update(this.game.car, true);
      this.renderer.render(this.game.scene, this.camera);
    } else if (this.game && this.state === 'paused') {
      this.audio.update(null, false);
      this.renderer.render(this.game.scene, this.camera);
    } else if (this.state === 'menu') {
      this.audio.update(null, false);
      this.showAngle += dt * 0.25;
      this.turn.rotation.y = this.showAngle;
      const r = this.screen === 'garage' ? 9.2 : 10;
      this.camera.position.set(Math.sin(0.7) * r, 2.7, Math.cos(0.7) * r);
      this.camera.lookAt(0, 0.75, 0);
      if (this.camera.fov !== 45 || this.camera.far !== 200) { this.camera.fov = 45; this.camera.far = 200; this.camera.updateProjectionMatrix(); }
      this.renderer.render(this.show, this.camera);
    }
  }
}

window.app = new App();
