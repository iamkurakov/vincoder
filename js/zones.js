// Конструкторы зон: рельеф, дороги, покрытия, объекты и маршруты для каждого режима.
import { SURF } from './data.js';
import { makeNoise, rng, clamp, lerp, smoothstep } from './noise.js';
import { roundedRect, offsetPath, catmull } from './world.js';

const BUILDING_COLORS = [0xd8cfc4, 0xb9b3a9, 0xc98f6b, 0x9fb3c4, 0xe6dfcf, 0x8a8f99, 0xb57a5a, 0xd9c7a3, 0x7f9aa8];

function distToPolyline(pts, x, z) {
  let best = Infinity;
  for (let i = 0; i < pts.length - 1; i++) {
    const ax = pts[i][0], az = pts[i][1], dx = pts[i + 1][0] - ax, dz = pts[i + 1][1] - az;
    const l2 = dx * dx + dz * dz || 1;
    const t = clamp(((x - ax) * dx + (z - az) * dz) / l2, 0, 1);
    const px = ax + dx * t - x, pz = az + dz * t - z;
    const d = px * px + pz * pz;
    if (d < best) best = d;
  }
  return Math.sqrt(best);
}

function scatter(w, count, rand, fn, region = null) {
  const n = Math.round(count * w.density);
  const ext = w.size * 0.485;
  for (let i = 0; i < n; i++) {
    let x, z;
    if (region) { const p = region(rand); x = p[0]; z = p[1]; } else { x = (rand() * 2 - 1) * ext; z = (rand() * 2 - 1) * ext; }
    if (Math.abs(x) > ext || Math.abs(z) > ext) continue;
    fn(x, z, rand);
  }
}

const nearSpawn = (w, x, z, r = 25) => w.spawn && Math.hypot(x - w.spawn.x, z - w.spawn.z) < r;

function loopCtrl(rand, cx, cz, count, R, jitter, sx = 1, sz = 1) {
  const pts = [];
  for (let i = 0; i < count; i++) {
    const a = (i / count) * Math.PI * 2;
    const r = R + (rand() * 2 - 1) * jitter;
    pts.push([cx + Math.cos(a) * r * sx, cz + Math.sin(a) * r * sz]);
  }
  return pts;
}

function addTraffic(w, pts, closed, lanes, speed, spacing) {
  for (const lane of lanes) {
    const p = offsetPath(lane.reverse ? pts.slice().reverse() : pts, closed, lane.off);
    w.trafficPaths.push({ pts: p, closed, speed: lane.speed || speed, spacing });
  }
}

// ---------- Город (общий для City и OpenWorld) ----------
function cityRoads(w, cx, cz, offs, width) {
  const ext = offs[offs.length - 1];
  w.intersections = w.intersections || [];
  for (const o of offs) {
    w.addRoad([[cx - ext, cz + o], [cx + ext, cz + o]], { straight: true, width, style: 'city', blend: 6, smooth: 9, hFixed: w.cityH });
    w.addRoad([[cx + o, cz - ext], [cx + o, cz + ext]], { straight: true, width, style: 'city', blend: 6, smooth: 9, hFixed: w.cityH });
    for (const o2 of offs) w.intersections.push({ x: cx + o, z: cz + o2, w: width });
  }
}

function cityGround(cx, cz, offs, width, parks) {
  const ext = offs[offs.length - 1] + width / 2 + 4;
  return (x, z) => {
    const lx = x - cx, lz = z - cz;
    if (Math.max(Math.abs(lx), Math.abs(lz)) > ext) return -1;
    for (let i = 0; i < offs.length - 1; i++) for (let j = 0; j < offs.length - 1; j++) {
      if (!parks.has(i + ',' + j)) continue;
      const x0 = offs[i] + width / 2 + 4, x1 = offs[i + 1] - width / 2 - 4;
      const z0 = offs[j] + width / 2 + 4, z1 = offs[j + 1] - width / 2 - 4;
      if (lx > x0 && lx < x1 && lz > z0 && lz < z1) return SURF.GRASS;
    }
    return SURF.CONCRETE;
  };
}

function cityDecorate(w, cx, cz, offs, width, parks, rand) {
  const sw = width / 2 + 4;
  for (let i = 0; i < offs.length - 1; i++) for (let j = 0; j < offs.length - 1; j++) {
    const x0 = cx + offs[i] + sw + 2, x1 = cx + offs[i + 1] - sw - 2;
    const z0 = cz + offs[j] + sw + 2, z1 = cz + offs[j + 1] - sw - 2;
    if (parks.has(i + ',' + j)) {
      for (let k = 0; k < 26; k++) w.addTree(lerp(x0 + 4, x1 - 4, rand()), lerp(z0 + 4, z1 - 4, rand()), rand() < 0.7 ? 1 : 0, 0.8 + rand() * 0.5);
      continue;
    }
    const central = 1 - Math.max(Math.abs((x0 + x1) / 2 - cx), Math.abs((z0 + z1) / 2 - cz)) / (offs[offs.length - 1] + 1);
    const div = rand() < 0.25 ? 1 : rand() < 0.7 ? 2 : 3;
    const lw = (x1 - x0) / div, ld = (z1 - z0) / div;
    for (let a = 0; a < div; a++) for (let b = 0; b < div; b++) {
      const m = 2 + rand() * 2;
      const h = 8 + rand() * 18 + central * central * (20 + rand() * 45);
      w.addBuilding(x0 + a * lw + m, z0 + b * ld + m, x0 + (a + 1) * lw - m, z0 + (b + 1) * ld - m, h, BUILDING_COLORS[Math.floor(rand() * BUILDING_COLORS.length)]);
    }
  }
  // фонари вдоль улиц
  const ext = offs[offs.length - 1];
  for (const o of offs) {
    for (let t = -ext + 20; t < ext - 10; t += 34) {
      if (offs.some(o2 => Math.abs(o2 - t) < 14)) continue;
      w.addLamp(cx + t, cz + o + width / 2 + 1.5, Math.PI);
      w.addLamp(cx + t, cz + o - width / 2 - 1.5, 0);
      w.addLamp(cx + o + width / 2 + 1.5, cz + t, -Math.PI / 2);
      w.addLamp(cx + o - width / 2 - 1.5, cz + t, Math.PI / 2);
    }
  }
}

function cityRoute(w, cx, cz, offs, startI, startJ, firstDir, count, seed) {
  const rand = rng(seed);
  const dirs = [[1, 0], [0, 1], [-1, 0], [0, -1]];
  let i = startI, j = startJ, d = firstDir, length = 0;
  const cps = [];
  const n = offs.length;
  // первая точка — ближайший перекрёсток впереди
  cps.push({ x: cx + offs[i], z: cz + offs[j], yaw: Math.atan2(dirs[d][0], dirs[d][1]) });
  for (let k = 1; k < count; k++) {
    const opts = [];
    for (let nd = 0; nd < 4; nd++) {
      if (nd === (d + 2) % 4) continue;
      for (let st = 1; st <= 2; st++) {
        const ni = i + dirs[nd][0] * st, nj = j + dirs[nd][1] * st;
        if (ni >= 0 && ni < n && nj >= 0 && nj < n) opts.push([nd, ni, nj, st]);
      }
    }
    const [nd, ni, nj] = opts[Math.floor(rand() * opts.length)];
    length += Math.abs(offs[ni] - offs[i]) + Math.abs(offs[nj] - offs[j]);
    i = ni; j = nj; d = nd;
    cps.push({ x: cx + offs[i], z: cz + offs[j], yaw: Math.atan2(dirs[d][0], dirs[d][1]) });
  }
  return { cps, length: length + 70, closed: false };
}

// ---------- Зоны ----------
export const ZONE_BUILDERS = {
  TestTrack: {
    setup(w) {
      w.size = 800; w.res = 400; w.seed = 11;
      const n = makeNoise(11);
      const pool = { x: 40, z: 140, r: 24 };
      const mud = { x: 140, z: 30, r: 30 }, sand = { x: -200, z: -10, r: 30 }, rocks = { x: 150, z: -100, r: 28 };
      const pad = { x: -70, z: -60, r: 36 };
      w.ramps = [
        { x: -10, z: 30, dx: 1, dz: 0, len: 16, w: 7, h: 2.4 },
        { x: -150, z: 120, dx: 0, dz: -1, len: 12, w: 6, h: 1.6 },
        { x: 60, z: -30, dx: -1, dz: 0, len: 22, w: 8, h: 3.2 },
      ];
      w.baseH = (x, z) => {
        let h = 3 + 1.2 * n.fbm(x / 260, z / 260, 3);
        const dp = Math.hypot(x - pool.x, z - pool.z);
        if (dp < pool.r) h = lerp(1.0, h, smoothstep(pool.r * 0.45, pool.r, dp));
        const dm = Math.hypot(x - mud.x, z - mud.z);
        if (dm < mud.r) h -= 0.6 * (1 - smoothstep(mud.r * 0.5, mud.r, dm)) + 0.15 * n(x / 4, z / 4);
        const ds = Math.hypot(x - sand.x, z - sand.z);
        if (ds < sand.r) h += 0.8 * Math.sin(x / 6) * Math.cos(z / 7) * (1 - smoothstep(sand.r * 0.6, sand.r, ds));
        return h;
      };
      const rampAt = (x, z) => {
        for (const r of w.ramps) {
          const a = (x - r.x) * r.dx + (z - r.z) * r.dz, b = -(x - r.x) * r.dz + (z - r.z) * r.dx;
          if (a >= 0 && a <= r.len && Math.abs(b) <= r.w / 2) return r.h * (a / r.len);
        }
        return 0;
      };
      w.postH = (x, z, h) => h + rampAt(x, z);
      w.water = { level: 1.55 };
      w.ground = (x, z) => {
        if (Math.hypot(x - pad.x, z - pad.z) < pad.r) return SURF.CONCRETE;
        if (Math.hypot(x - mud.x, z - mud.z) < mud.r) return SURF.MUD;
        if (Math.hypot(x - sand.x, z - sand.z) < sand.r) return SURF.SAND;
        if (Math.hypot(x - rocks.x, z - rocks.z) < rocks.r) return SURF.ROCKS;
        if (Math.hypot(x - pool.x, z - pool.z) < pool.r + 4) return SURF.MUD;
        if (rampAt(x, z) > 0) return SURF.CONCRETE;
        return SURF.GRASS;
      };
      w.colorAt = (x, z) => rampAt(x, z) > 0.01 ? [0.95, 0.55, 0.12] : null;
      w.rockField = rocks;
      w.loop = w.addRoad([[-250, -210], [-60, -250], [120, -240], [260, -200], [310, -60], [240, 40], [300, 160], [200, 260],
        [40, 250], [-80, 285], [-220, 250], [-300, 120], [-320, -60]], {
        closed: true, width: 12, style: 'track', smooth: 30, blend: 10,
        sections: [[0, 0.3, SURF.ASPHALT, 'track'], [0.3, 0.44, SURF.GRAVEL, 'gravel'], [0.44, 0.54, SURF.MUD, 'mud'],
          [0.54, 0.64, SURF.SAND, 'sand'], [0.64, 0.8, SURF.WET, 'wet'], [0.8, 1.01, SURF.ASPHALT, 'track']],
      });
      w.repairPoints.push({ x: pad.x, z: pad.z });
    },
    decorate(w) {
      const r = rng(111);
      const L = w.loop.length;
      w.spawn = w.pointOnRoad(w.loop, L - 18);
      w.routes.default = w.loopCheckpoints(w.loop, 0, 10);
      for (let i = 0; i < 14; i++) w.addCone(-150 + i * 18, -150 + (i % 2 ? 0 : 0));
      for (let i = 0; i < 8; i++) { w.addCone(-120 + i * 4, -120); w.addCone(-120 + i * 4, -112); }
      const rf = w.rockField;
      for (let i = 0; i < 40; i++) {
        const a = r() * 6.28, d = Math.sqrt(r()) * rf.r;
        w.addRock(rf.x + Math.cos(a) * d, rf.z + Math.sin(a) * d, 0.25 + r() * 0.45);
      }
      scatter(w, 900, r, (x, z, rr) => {
        if (Math.abs(x) < 345 && z > -275 && z < 305) return;
        if (!w.clearOfRoads(x, z, 10)) return;
        w.addTree(x, z, rr() < 0.5 ? 0 : 1, 0.8 + rr() * 0.6);
      });
    },
  },

  Highway: {
    setup(w) {
      w.size = 2400; w.res = 512; w.seed = 22;
      const n = makeNoise(22);
      w.baseH = (x, z) => 30 + 32 * n.fbm(x / 800, z / 800, 4) + 7 * n.fbm(x / 170, z / 170, 3);
      w.ground = (x, z) => n(x / 90 + 3, z / 90) > 0.45 ? SURF.SOIL : SURF.GRASS;
      w.sky.fogFar = 1400;
      const r = rng(222);
      const ctrl = [];
      for (let i = 0; i < 14; i++) {
        const a = (i / 14) * Math.PI * 2;
        const R = 920 + 140 * Math.sin(3 * a + 1) + (r() * 2 - 1) * 70;
        ctrl.push([Math.cos(a) * R, Math.sin(a) * R * 0.86]);
      }
      w.loop = w.addRoad(ctrl, { closed: true, width: 18, style: 'highway', smooth: 220, blend: 30, shoulder: SURF.GRAVEL, shoulderW: 2.5 });
    },
    decorate(w) {
      const r = rng(2222);
      w.spawn = w.pointOnRoad(w.loop, 0, 4.5);
      w.routes.default = w.loopCheckpoints(w.loop, 0, 14, 4.5);
      w.progressRoad = w.loop;
      addTraffic(w, w.loop.pts, true, [
        { off: 6.75, speed: 22 }, { off: 2.25, speed: 28 },
        { off: 6.75, speed: 22, reverse: true }, { off: 2.25, speed: 28, reverse: true },
      ], 25, 260);
      scatter(w, 2200, r, (x, z, rr) => { if (w.clearOfRoads(x, z, 14)) w.addTree(x, z, rr() < 0.6 ? 0 : 1, 0.9 + rr() * 0.7); });
      scatter(w, 160, r, (x, z, rr) => { if (w.clearOfRoads(x, z, 14)) w.addRock(x, z, 0.8 + rr() * 2); });
    },
  },

  City: {
    setup(w) {
      w.size = 900; w.res = 450; w.seed = 33;
      const n = makeNoise(33);
      w.offs = [-300, -180, -60, 60, 180, 300];
      w.parks = new Set(['1,3', '3,1', '2,2']);
      w.cityH = 2;
      w.baseH = (x, z) => {
        const m = Math.max(Math.abs(x), Math.abs(z));
        return 2 + smoothstep(330, 445, m) * (8 + 10 * n.fbm(x / 120, z / 120, 3));
      };
      const cg = cityGround(0, 0, w.offs, 14, w.parks);
      w.ground = (x, z) => { const s = cg(x, z); return s >= 0 ? s : SURF.GRASS; };
      w.sky.fogFar = 800;
      cityRoads(w, 0, 0, w.offs, 14);
    },
    decorate(w) {
      const r = rng(333);
      w.spawn = { x: -250, z: -60 + 3.5, yaw: Math.PI / 2 };
      w.routes.city = cityRoute(w, 0, 0, w.offs, 1, 2, 0, 10, 3333);
      w.routes.default = w.routes.city;
      cityDecorate(w, 0, 0, w.offs, 14, w.parks, r);
      for (const a of [300, 180, 60]) {
        const p = roundedRect(-a, -a, a, a, 12);
        addTraffic(w, p, true, [{ off: 3.5 }, { off: 3.5, reverse: true }], 11, 110);
      }
      w.repairPoints.push({ x: 0, z: 0 });
      scatter(w, 500, r, (x, z, rr) => {
        if (Math.max(Math.abs(x), Math.abs(z)) < 330) return;
        w.addTree(x, z, rr() < 0.5 ? 0 : 1, 0.8 + rr() * 0.6);
      });
    },
  },

  Forest: {
    setup(w) {
      w.size = 1200; w.res = 480; w.seed = 44;
      const n = makeNoise(44);
      w.baseH = (x, z) => 30 + 25 * n.fbm(x / 450, z / 450, 4) + 5 * n.fbm(x / 80, z / 80, 3);
      w.ground = (x, z) => n(x / 60, z / 60) > 0.3 ? SURF.GRASS : SURF.SOIL;
      Object.assign(w.sky, { top: 0x6f98c4, bottom: 0xc9d9cf, fog: 0xa9bfae, fogNear: 60, fogFar: 520 });
      const r = rng(444);
      const ctrl = loopCtrl(r, 0, 0, 16, 410, 110);
      w.loop = w.addRoad(ctrl, { closed: true, width: 8, style: 'gravel', surface: SURF.GRAVEL, smooth: 50, blend: 10, shoulder: SURF.SOIL, shoulderW: 1.5 });
      w.repairPoints.push({ x: 0, z: 0 });
    },
    decorate(w) {
      const r = rng(4444);
      w.spawn = w.pointOnRoad(w.loop, w.loop.length - 18);
      w.routes.default = w.loopCheckpoints(w.loop, 0, 12);
      w.pads.push({ x: 0, z: 0, r: 14, color: 0x7d7d70 });
      scatter(w, 6500, r, (x, z, rr) => {
        if (!w.clearOfRoads(x, z, 3.2) || Math.hypot(x, z) < 18) return;
        w.addTree(x, z, rr() < 0.7 ? 0 : 1, 0.8 + rr() * 0.8);
      });
      scatter(w, 160, r, (x, z, rr) => { if (w.clearOfRoads(x, z, 4)) w.addRock(x, z, 0.6 + rr() * 1.6); });
    },
  },

  Offroad: {
    setup(w) {
      w.size = 1000; w.res = 500; w.seed = 55;
      const n = makeNoise(55);
      const river = catmull([[-540, -80], [-300, -150], [-120, -40], [60, -130], [240, -20], [540, -90]], false, 10);
      w.river = river;
      const base0 = (x, z) => 17 + 15 * n.fbm(x / 260, z / 260, 4) + 4 * n.fbm(x / 50, z / 50, 3);
      w.baseH = base0;
      w.postH = (x, z, h) => {
        const d = distToPolyline(river, x, z);
        return d < 75 ? lerp(-0.2, h, smoothstep(9, 75, d)) : h;
      };
      w.water = { level: 0.5 };
      w.ground = (x, z) => {
        const g = (base0(x + 2, z) - base0(x - 2, z)) / 4, k = (base0(x, z + 2) - base0(x, z - 2)) / 4;
        if (Math.hypot(g, k) > 0.42) return SURF.ROCKS;
        if (distToPolyline(river, x, z) < 16) return SURF.MUD;
        if (n(x / 200 + 5, z / 200) > 0.3) return SURF.SAND;
        if (n(x / 110 - 7, z / 110) > 0.35) return SURF.MUD;
        if (n(x / 70 + 11, z / 70) > 0.5) return SURF.ROCKS;
        return SURF.GRASS;
      };
      Object.assign(w.sky, { top: 0x6a9bd0, bottom: 0xe4d9c0, fog: 0xd5cdb8, fogFar: 750 });
      w.loop = w.addRoad([[-380, -330], [-150, -370], [80, -300], [300, -340], [410, -160], [330, 60], [380, 280], [150, 380],
        [-80, 300], [-300, 370], [-420, 180], [-350, 0], [-420, -160]], {
        closed: true, width: 6, ribbon: false, flatten: 0.55, blend: 8, smooth: 25,
        sections: [[0, 0.2, SURF.MUD], [0.2, 0.45, SURF.GRAVEL], [0.45, 0.6, SURF.SAND], [0.6, 0.8, SURF.MUD], [0.8, 1.01, SURF.GRAVEL]],
      });
      w.repairPoints.push({ x: 0, z: 200 });
    },
    decorate(w) {
      const r = rng(5555);
      w.spawn = w.pointOnRoad(w.loop, w.loop.length - 15);
      w.routes.default = w.loopCheckpoints(w.loop, 0, 12);
      w.pads.push({ x: 0, z: 200, r: 12, color: 0x8a8a7a });
      scatter(w, 600, r, (x, z, rr) => {
        if (!w.clearOfRoads(x, z, 4) || nearSpawn(w, x, z)) return;
        if (w.heightAt(x, z) < 1.2) return;
        w.addRock(x, z, 0.6 + Math.pow(rr(), 2) * 3);
      });
      scatter(w, 900, r, (x, z, rr) => {
        if (!w.clearOfRoads(x, z, 5) || w.heightAt(x, z) < 1.5) return;
        const s = w.surfaceAt(x, z);
        if (s === SURF.SAND || s === SURF.ROCKS) return;
        w.addTree(x, z, rr() < 0.5 ? 0 : 1, 0.8 + rr() * 0.6);
      });
    },
  },

  OpenWorld: {
    setup(w) {
      w.size = 3000; w.res = 512; w.seed = 66;
      const n = makeNoise(66);
      const C = w.C = { x: 550, z: -550 };
      const F = w.F = { x: -620, z: -480 };
      const O = w.O = { x: 0, z: 680 };
      const lake = w.lake = { x: -800, z: 820, r: 130 };
      w.offs = [-180, -60, 60, 180];
      w.parks = new Set(['1,1']);
      const base0 = (x, z) => {
        let h = 38 + 25 * n.fbm(x / 700, z / 700, 4);
        h += smoothstep(250, 500, z) * 22 * Math.abs(n.fbm(x / 160, z / 160, 3));
        h += smoothstep(500, 200, Math.hypot(x - F.x, z - F.z)) * 6 * n.fbm(x / 70, z / 70, 3);
        return h;
      };
      w.cityH = base0(C.x, C.z);
      w.baseH = (x, z) => {
        const db = Math.max(Math.abs(x - C.x), Math.abs(z - C.z));
        return lerp(w.cityH, base0(x, z), smoothstep(215, 380, db));
      };
      w.postH = (x, z, h) => {
        const d = Math.hypot(x - lake.x, z - lake.z);
        return d < lake.r * 1.8 ? lerp(2, h, smoothstep(lake.r * 0.6, lake.r * 1.8, d)) : h;
      };
      w.water = { level: 3.2 };
      const cg = cityGround(C.x, C.z, w.offs, 14, w.parks);
      w.ground = (x, z) => {
        const c = cg(x, z); if (c >= 0) return c;
        if (Math.hypot(x - F.x, z - F.z) < 430) return n(x / 60, z / 60) > 0.3 ? SURF.GRASS : SURF.SOIL;
        if (z > 260) {
          if (n(x / 200 + 5, z / 200) > 0.35) return SURF.SAND;
          if (n(x / 110 - 7, z / 110) > 0.38) return SURF.MUD;
          if (n(x / 70 + 11, z / 70) > 0.45) return SURF.ROCKS;
        }
        return n(x / 90 + 3, z / 90) > 0.5 ? SURF.SOIL : SURF.GRASS;
      };
      w.sky.fogFar = 1300;
      // шоссе по периметру
      const r = rng(666);
      const ctrl = [];
      for (let i = 0; i < 16; i++) {
        const a = (i / 16) * Math.PI * 2;
        ctrl.push([Math.cos(a) * (1250 + (r() * 2 - 1) * 60), Math.sin(a) * (1250 + (r() * 2 - 1) * 60)]);
      }
      w.highway = w.addRoad(ctrl, { closed: true, width: 18, style: 'highway', smooth: 220, blend: 30, shoulder: SURF.GRAVEL, shoulderW: 2.5 });
      // город
      cityRoads(w, C.x, C.z, w.offs, 14);
      // съезды: город → шоссе (восток), город → лес, город → бездорожье
      const hwPt = (tx, tz) => {
        let best = null, bd = Infinity;
        w.highway.pts.forEach(p => { const d = Math.hypot(p[0] - tx, p[1] - tz); if (d < bd) { bd = d; best = p; } });
        const l = Math.hypot(best[0], best[1]);
        return [best[0] * (l - 8) / l, best[1] * (l - 8) / l];
      };
      w.addRoad([[C.x + 180, C.z + 60], [C.x + 330, C.z + 70], [C.x + 480, C.z + 90], hwPt(C.x + 700, C.z + 120)], { width: 10, style: 'road', smooth: 60, blend: 14 });
      w.addRoad([[C.x - 180, C.z - 60], [C.x - 420, C.z - 40], [-100, -470], [F.x + 300, F.z + 10], [F.x + 262, F.z]], { width: 8, style: 'gravel', surface: SURF.GRAVEL, smooth: 50, blend: 12 });
      w.addRoad([[C.x - 60, C.z + 180], [C.x - 80, C.z + 380], [250, 200], [60, O.z - 335]], { width: 8, style: 'gravel', surface: SURF.GRAVEL, smooth: 50, blend: 12 });
      w.addRoad([[-1260 * 0.97, 0], [-900, 120], [-650, 260], [-380, 380], [O.x - 330, O.z]], { width: 8, style: 'gravel', surface: SURF.GRAVEL, smooth: 50, blend: 12 });
      // лесная петля и тропа бездорожья
      w.forestLoop = w.addRoad(loopCtrl(r, F.x, F.z, 12, 262, 40), { closed: true, width: 8, style: 'gravel', surface: SURF.GRAVEL, smooth: 50, blend: 10 });
      w.offLoop = w.addRoad(loopCtrl(r, O.x, O.z, 12, 335, 50), { closed: true, width: 6, ribbon: false, flatten: 0.5, blend: 8, smooth: 25, surface: SURF.MUD });
      w.repairPoints.push({ x: C.x, z: C.z }, { x: F.x, z: F.z }, { x: O.x, z: O.z });
    },
    decorate(w) {
      const r = rng(6666);
      const { C, F, O } = w;
      w.spawn = { x: C.x - 120, z: C.z - 60 + 3.5, yaw: Math.PI / 2 };
      w.routes.default = w.loopCheckpoints(w.highway, 0, 16, 4.5);
      cityDecorate(w, C.x, C.z, w.offs, 14, w.parks, r);
      w.pads.push({ x: F.x, z: F.z, r: 14, color: 0x7d7d70 }, { x: O.x, z: O.z, r: 14, color: 0x8a8a7a });
      addTraffic(w, w.highway.pts, true, [
        { off: 6.75, speed: 22 }, { off: 2.25, speed: 28 },
        { off: 6.75, speed: 22, reverse: true }, { off: 2.25, speed: 28, reverse: true },
      ], 25, 320);
      const p = roundedRect(C.x - 180, C.z - 180, C.x + 180, C.z + 180, 12);
      addTraffic(w, p, true, [{ off: 3.5 }, { off: 3.5, reverse: true }], 11, 120);
      const inCity = (x, z) => Math.max(Math.abs(x - C.x), Math.abs(z - C.z)) < 230;
      scatter(w, 3200, r, (x, z, rr) => {
        if (!w.clearOfRoads(x, z, 3.5) || Math.hypot(x - F.x, z - F.z) < 18) return;
        w.addTree(x, z, rr() < 0.7 ? 0 : 1, 0.8 + rr() * 0.8);
      }, (rr) => { const a = rr() * 6.28, d = Math.sqrt(rr()) * 440; return [F.x + Math.cos(a) * d, F.z + Math.sin(a) * d]; });
      scatter(w, 2600, r, (x, z, rr) => {
        if (inCity(x, z) || !w.clearOfRoads(x, z, 12) || w.heightAt(x, z) < 4) return;
        if (Math.hypot(x - O.x, z - O.z) < 20 || Math.hypot(x - F.x, z - F.z) < 440) return;
        w.addTree(x, z, rr() < 0.5 ? 0 : 1, 0.9 + rr() * 0.7);
      });
      scatter(w, 500, r, (x, z, rr) => {
        if (!w.clearOfRoads(x, z, 5) || w.heightAt(x, z) < 4 || Math.hypot(x - O.x, z - O.z) < 20) return;
        w.addRock(x, z, 0.6 + Math.pow(rr(), 2) * 3);
      }, (rr) => [(rr() * 2 - 1) * 1300, 280 + rr() * 1000]);
    },
  },
};
