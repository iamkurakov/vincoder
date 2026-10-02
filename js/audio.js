// Синтезированный звук: двигатель, визг шин, шорох покрытия, ветер, удары, сигналы.
export class Audio {
  constructor() { this.ctx = null; this.volume = 0.7; this.muted = false; this.engineOn = false; }

  init() {
    if (this.ctx) { if (this.ctx.state === 'suspended') this.ctx.resume(); return; }
    const AC = window.AudioContext || window.webkitAudioContext;
    if (!AC) return;
    const ctx = this.ctx = new AC();
    this.master = ctx.createGain(); this.master.connect(ctx.destination);
    this.setVolume(this.volume);
    // шум
    const buf = ctx.createBuffer(1, ctx.sampleRate * 2, ctx.sampleRate);
    const d = buf.getChannelData(0);
    for (let i = 0; i < d.length; i++) d[i] = Math.random() * 2 - 1;
    this.noiseBuf = buf;
    const noise = (type, freq, q) => {
      const src = ctx.createBufferSource(); src.buffer = buf; src.loop = true;
      const f = ctx.createBiquadFilter(); f.type = type; f.frequency.value = freq; f.Q.value = q;
      const g = ctx.createGain(); g.gain.value = 0;
      src.connect(f); f.connect(g); g.connect(this.master); src.start();
      return { src, f, g };
    };
    // двигатель
    this.eGain = ctx.createGain(); this.eGain.gain.value = 0;
    this.eFilter = ctx.createBiquadFilter(); this.eFilter.type = 'lowpass'; this.eFilter.frequency.value = 600; this.eFilter.Q.value = 2;
    this.o1 = ctx.createOscillator(); this.o1.type = 'sawtooth';
    this.o2 = ctx.createOscillator(); this.o2.type = 'square';
    this.o3 = ctx.createOscillator(); this.o3.type = 'triangle';
    const g2 = ctx.createGain(); g2.gain.value = 0.35;
    const g3 = ctx.createGain(); g3.gain.value = 0.5;
    this.o1.connect(this.eFilter); this.o2.connect(g2); g2.connect(this.eFilter); this.o3.connect(g3); g3.connect(this.eFilter);
    this.eFilter.connect(this.eGain); this.eGain.connect(this.master);
    this.o1.start(); this.o2.start(); this.o3.start();
    this.skid = noise('bandpass', 1300, 4);
    this.rumble = noise('lowpass', 260, 1);
    this.wind = noise('highpass', 900, 0.5);
  }

  setVolume(v) { this.volume = v; if (this.master) this.master.gain.value = this.muted ? 0 : v; }
  toggleMute() { this.muted = !this.muted; this.setVolume(this.volume); return this.muted; }

  update(car, active) {
    if (!this.ctx) return;
    const t = this.ctx.currentTime;
    if (!active || !car) {
      this.eGain.gain.setTargetAtTime(0, t, 0.1);
      this.skid.g.gain.setTargetAtTime(0, t, 0.05);
      this.rumble.g.gain.setTargetAtTime(0, t, 0.1);
      this.wind.g.gain.setTargetAtTime(0, t, 0.1);
      return;
    }
    const rpm = car.rpm;
    const f = rpm / 60 * 2;
    this.o1.frequency.setTargetAtTime(f, t, 0.03);
    this.o2.frequency.setTargetAtTime(f * 0.5, t, 0.03);
    this.o3.frequency.setTargetAtTime(f * 1.5, t, 0.03);
    this.eFilter.frequency.setTargetAtTime(350 + car.throttleOut * 1400 + rpm * 0.12, t, 0.05);
    this.eGain.gain.setTargetAtTime(0.09 + car.throttleOut * 0.1, t, 0.05);
    const sp = car.vel.length();
    let skid = 0, loose = 0;
    for (const w of car.wheels) {
      if (!w.contact) continue;
      if (w.surface === 0 || w.surface === 1 || w.surface === 9) skid = Math.max(skid, w.skid);
      else loose += 0.25;
    }
    this.skid.g.gain.setTargetAtTime(skid > 0.35 && sp > 3 ? (skid - 0.35) * 0.35 : 0, t, 0.04);
    this.rumble.g.gain.setTargetAtTime(Math.min(0.35, sp / 60) * (0.3 + loose), t, 0.08);
    this.wind.g.gain.setTargetAtTime(Math.min(0.12, sp * sp / 30000), t, 0.1);
  }

  burst(vol = 0.5, freq = 400, dur = 0.3) {
    if (!this.ctx) return;
    const ctx = this.ctx, t = ctx.currentTime;
    const src = ctx.createBufferSource(); src.buffer = this.noiseBuf;
    const f = ctx.createBiquadFilter(); f.type = 'lowpass'; f.frequency.value = freq;
    const g = ctx.createGain(); g.gain.setValueAtTime(Math.min(1, vol), t); g.gain.exponentialRampToValueAtTime(0.001, t + dur);
    src.connect(f); f.connect(g); g.connect(this.master);
    src.start(t, Math.random()); src.stop(t + dur);
  }

  tone(freqs, dur = 0.12, type = 'sine', vol = 0.18) {
    if (!this.ctx) return;
    const ctx = this.ctx;
    freqs.forEach((fr, i) => {
      const t = ctx.currentTime + i * dur;
      const o = ctx.createOscillator(); o.type = type; o.frequency.value = fr;
      const g = ctx.createGain(); g.gain.setValueAtTime(vol, t); g.gain.exponentialRampToValueAtTime(0.001, t + dur * 1.8);
      o.connect(g); g.connect(this.master); o.start(t); o.stop(t + dur * 2);
    });
  }

  honk() { this.tone([392, 392], 0.18, 'square', 0.06); }
  click() { this.tone([880], 0.04, 'triangle', 0.08); }
}
