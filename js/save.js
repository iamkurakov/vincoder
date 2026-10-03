// Сохранение прогресса и настроек в localStorage.
import { VEHICLES, levelFromXp } from './data.js';

const KEY = 'terrainDrive.save.v1';

function defaults() {
  const cars = {};
  for (const v of VEHICLES) cars[v.id] = { owned: v.price === 0, health: 1, paint: 0, engine: 0, handling: 0, armor: 0 };
  return {
    credits: 1000, xp: 0, selected: 'suv', cars,
    best: {},          // `${zone}:${mode}` -> { time, medal }
    totalKm: 0,
    settings: { quality: 'auto', volume: 0.7, steerSens: 1, camera: 0, touch: 'auto', showFps: false },
  };
}

function load() {
  const d = defaults();
  try {
    const raw = localStorage.getItem(KEY);
    if (!raw) return d;
    const s = JSON.parse(raw);
    Object.assign(d, s);
    d.settings = Object.assign(defaults().settings, s.settings || {});
    for (const v of VEHICLES) {
      d.cars[v.id] = Object.assign(defaults().cars[v.id], (s.cars || {})[v.id] || {});
      if (v.price === 0) d.cars[v.id].owned = true;
    }
    if (!d.cars[d.selected] || !d.cars[d.selected].owned) d.selected = 'suv';
  } catch (e) { /* повреждённое сохранение — начинаем заново */ }
  return d;
}

export const save = load();

export function persist() {
  try { localStorage.setItem(KEY, JSON.stringify(save)); } catch (e) { /* приватный режим */ }
}

export function resetProgress() {
  const keep = save.settings;
  const d = defaults();
  for (const k of Object.keys(save)) delete save[k];
  Object.assign(save, d, { settings: keep });
  persist();
}

export const level = () => levelFromXp(save.xp);
export const selectedVehicle = () => VEHICLES.find(v => v.id === save.selected) || VEHICLES[0];
