// Ввод: клавиатура, сенсорные кнопки, геймпад.
export class Input {
  constructor() {
    this.keys = new Set();
    this.touch = { left: false, right: false, gas: false, brake: false, hand: false };
    this.actions = [];
    this.sens = 1;
    this.prevPad = {};
    const map = { KeyR: 'reset', KeyC: 'camera', Escape: 'pause', KeyP: 'pause', KeyM: 'mute', Enter: 'confirm' };
    window.addEventListener('keydown', (e) => {
      if (e.target && (e.target.tagName === 'INPUT' || e.target.tagName === 'SELECT')) return;
      if (!e.repeat && map[e.code]) this.actions.push(map[e.code]);
      this.keys.add(e.code);
      if (['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Space'].includes(e.code)) e.preventDefault();
    });
    window.addEventListener('keyup', (e) => this.keys.delete(e.code));
    window.addEventListener('blur', () => { this.keys.clear(); for (const k in this.touch) this.touch[k] = false; });
  }

  bindTouch(root) {
    root.querySelectorAll('[data-hold]').forEach(el => {
      const k = el.dataset.hold;
      const on = (e) => { e.preventDefault(); this.touch[k] = true; el.classList.add('down'); };
      const off = (e) => { e.preventDefault(); this.touch[k] = false; el.classList.remove('down'); };
      el.addEventListener('pointerdown', on);
      el.addEventListener('pointerup', off);
      el.addEventListener('pointercancel', off);
      el.addEventListener('pointerleave', off);
    });
    root.querySelectorAll('[data-action]').forEach(el => {
      el.addEventListener('pointerdown', (e) => { e.preventDefault(); this.actions.push(el.dataset.action); });
    });
  }

  takeActions() { const a = this.actions; this.actions = []; return a; }

  read() {
    const k = this.keys;
    let steer = 0, throttle = 0, brake = 0, hand = false;
    if (k.has('KeyA') || k.has('ArrowLeft')) steer -= 1;
    if (k.has('KeyD') || k.has('ArrowRight')) steer += 1;
    if (k.has('KeyW') || k.has('ArrowUp')) throttle = 1;
    if (k.has('KeyS') || k.has('ArrowDown')) brake = 1;
    if (k.has('Space')) hand = true;
    const t = this.touch;
    if (t.left) steer -= 1;
    if (t.right) steer += 1;
    if (t.gas) throttle = 1;
    if (t.brake) brake = 1;
    if (t.hand) hand = true;
    // геймпад
    const pads = navigator.getGamepads ? navigator.getGamepads() : [];
    for (const gp of pads) {
      if (!gp) continue;
      const ax = gp.axes[0] || 0;
      if (Math.abs(ax) > 0.12) steer += Math.sign(ax) * (Math.abs(ax) - 0.12) / 0.88;
      const b = (i) => gp.buttons[i] ? gp.buttons[i].value : 0;
      throttle = Math.max(throttle, b(7), b(0) > 0.5 && gp.buttons.length < 8 ? 1 : 0);
      brake = Math.max(brake, b(6));
      if (b(1) > 0.5 || b(5) > 0.5) hand = true;
      const edge = (i, name) => { const p = b(i) > 0.5; if (p && !this.prevPad[i]) this.actions.push(name); this.prevPad[i] = p; };
      edge(3, 'reset'); edge(2, 'camera'); edge(9, 'pause'); edge(0, 'confirm');
      break;
    }
    steer = Math.max(-1, Math.min(1, steer * this.sens));
    return { steer, throttle, brake, handbrake: hand };
  }
}
