// Игровые данные: покрытия, машины, зоны, правила режимов.
// Значения перенесены из Assets/_Game (ProjectBootstrapper) и подогнаны под браузерную физику.

export const SURF = {
  ASPHALT: 0, WET: 1, GRAVEL: 2, MUD: 3, GRASS: 4, SAND: 5, SOIL: 6, ROCKS: 7, WATER: 8, CONCRETE: 9,
};

// grip — сцепление, speed — множитель макс. скорости, resist — сопротивление качению,
// brake — эффективность тормозов, bump — тряска, dust — цвет пыли (null — нет пыли)
export const SURFACES = [
  { name: 'Асфальт',         grip: 1.00, speed: 1.00, resist: 0.000, brake: 1.00, bump: 0.00, dust: null,     color: [0.23, 0.23, 0.25], squeal: true },
  { name: 'Мокрый асфальт',  grip: 0.72, speed: 1.00, resist: 0.000, brake: 0.70, bump: 0.00, dust: null,     color: [0.17, 0.18, 0.21], squeal: true },
  { name: 'Гравий',          grip: 0.70, speed: 0.88, resist: 0.020, brake: 0.75, bump: 0.10, dust: 0xb8a888, color: [0.55, 0.50, 0.42], squeal: false },
  { name: 'Грязь',           grip: 0.45, speed: 0.62, resist: 0.090, brake: 0.55, bump: 0.15, dust: 0x4a3a28, color: [0.30, 0.22, 0.14], squeal: false },
  { name: 'Трава',           grip: 0.66, speed: 0.85, resist: 0.030, brake: 0.70, bump: 0.08, dust: 0x6f7a45, color: [0.28, 0.45, 0.20], squeal: false },
  { name: 'Песок',           grip: 0.52, speed: 0.66, resist: 0.080, brake: 0.60, bump: 0.06, dust: 0xd9c38f, color: [0.70, 0.60, 0.40], squeal: false },
  { name: 'Лесной грунт',    grip: 0.64, speed: 0.80, resist: 0.040, brake: 0.70, bump: 0.12, dust: 0x5a4632, color: [0.27, 0.25, 0.15], squeal: false },
  { name: 'Камни',           grip: 0.76, speed: 0.72, resist: 0.050, brake: 0.80, bump: 0.35, dust: 0x8a8580, color: [0.46, 0.45, 0.43], squeal: false },
  { name: 'Вода',            grip: 0.40, speed: 0.45, resist: 0.200, brake: 0.45, bump: 0.10, dust: 0xbfe0ff, color: [0.25, 0.35, 0.40], squeal: false },
  { name: 'Бетон',           grip: 0.95, speed: 1.00, resist: 0.000, brake: 0.95, bump: 0.00, dust: null,     color: [0.55, 0.55, 0.56], squeal: true },
];

// mass кг, torque Нм, top км/ч, offroad 0..1, health — прочность
export const VEHICLES = [
  { id: 'sedan', name: 'Городской седан', desc: 'Послушный и экономный. Хорош в городе, на бездорожье буксует.',
    drive: 'FWD', color: 0xc7c9d1, price: 0, level: 1, mass: 1350, torque: 2400, top: 185, offroad: 0.15,
    health: 100, rating: [6, 5, 2, 5], body: 'sedan', clearance: 0.0, brake: 9000, grip: 1.0 },
  { id: 'suv', name: 'Внедорожник', desc: 'Большой современный внедорожник: полный привод, высокая подвеска, прочный кузов.',
    drive: 'AWD', color: 0x6b0d1f, price: 2500, level: 2, mass: 2300, torque: 4300, top: 190, offroad: 0.85,
    health: 160, rating: [6, 6, 9, 8], body: 'suv', clearance: 0.18, brake: 15000, grip: 1.0 },
  { id: 'pickup', name: 'Пикап', desc: 'Тяжёлый и неубиваемый. Медленно разгоняется, зато везде проедет.',
    drive: 'AWD', color: 0xedede6, price: 3500, level: 3, mass: 2400, torque: 3600, top: 170, offroad: 0.70,
    health: 200, rating: [5, 3, 8, 10], body: 'pickup', clearance: 0.16, brake: 15000, grip: 0.97 },
  { id: 'rally', name: 'Ралли-кар', desc: 'Универсал: быстрый на гравии и уверенный на асфальте.',
    drive: 'AWD', color: 0x1f59d9, price: 4500, level: 3, mass: 1250, torque: 3300, top: 210, offroad: 0.60,
    health: 100, rating: [8, 9, 7, 5], body: 'rally', clearance: 0.06, brake: 10000, grip: 1.08 },
  { id: 'sport', name: 'Спортивный автомобиль', desc: 'Лучший на трассе. Низкий, быстрый и очень нежный к бездорожью.',
    drive: 'RWD', color: 0xd91414, price: 6000, level: 4, mass: 1300, torque: 3700, top: 250, offroad: 0.05,
    health: 70, rating: [10, 9, 1, 3], body: 'sport', clearance: -0.04, brake: 12000, grip: 1.15 },
];

export const PAINTS = [null, 0xd91414, 0x1f59d9, 0x1e8c3a, 0xf2b705, 0x111214, 0xf2f2f2, 0xff6a00, 0x7a3fd1];

export const UPGRADES = [
  { id: 'engine',   name: 'Двигатель',     desc: '+8% мощности, +4% макс. скорости за уровень' },
  { id: 'handling', name: 'Управляемость', desc: '+6% сцепления и тормозов за уровень' },
  { id: 'armor',    name: 'Броня',         desc: '+15% прочности за уровень' },
];
export const MAX_UPGRADE = 3;
export const upgradeCost = (veh, lvl) => Math.round((300 + veh.price / 15) * (lvl + 1) / 10) * 10;
export const repairCost = (veh, missing01) => Math.ceil((200 + veh.price / 20) * missing01 / 5) * 5;

export const MODE_INFO = {
  FreeRide:         { title: 'Свободная езда',     desc: 'Катайтесь без ограничений. Награда за каждый пройденный километр.' },
  TimeTrial:        { title: 'Заезд на время',     desc: 'Пройдите все чекпоинты как можно быстрее. Медали за время.' },
  CheckpointRace:   { title: 'Гонка по чекпоинтам', desc: 'Время ограничено — каждый чекпоинт добавляет секунды.' },
  OffroadChallenge: { title: 'Испытание бездорожьем', desc: 'Чекпоинты по холмам, грязи и броду. Берегите машину.' },
  CityChallenge:    { title: 'Городское испытание', desc: 'Проедьте по городу с трафиком. Не больше 3 сильных ударов.' },
  HighwayRun:       { title: 'Рывок по трассе',    desc: 'Проедьте 5 км по шоссе с трафиком, пока не кончилось время.' },
};

export const ZONES = [
  { id: 'TestTrack', name: 'Тестовый полигон', unlock: 1, desc: 'Все покрытия, трамплины и слалом',
    modes: ['FreeRide', 'TimeTrial', 'CheckpointRace'], refSpeed: 22 },
  { id: 'Highway', name: 'Трасса', unlock: 1, desc: 'Длинное шоссе с плавными поворотами и трафиком',
    modes: ['HighwayRun', 'TimeTrial', 'CheckpointRace', 'FreeRide'], refSpeed: 38 },
  { id: 'City', name: 'Город', unlock: 2, desc: 'Кварталы, перекрёстки, бордюры и машины',
    modes: ['CityChallenge', 'CheckpointRace', 'TimeTrial', 'FreeRide'], refSpeed: 17 },
  { id: 'Forest', name: 'Лес', unlock: 2, desc: 'Узкая гравийка между деревьями',
    modes: ['TimeTrial', 'CheckpointRace', 'FreeRide'], refSpeed: 21 },
  { id: 'Offroad', name: 'Бездорожье', unlock: 3, desc: 'Холмы, грязь, песок, камни и брод',
    modes: ['OffroadChallenge', 'TimeTrial', 'FreeRide'], refSpeed: 12 },
  { id: 'OpenWorld', name: 'Открытый мир', unlock: 4, desc: 'Большая карта со всеми типами местности',
    modes: ['FreeRide'], refSpeed: 20 },
];

// Правила режима для зоны. routeLength — длина маршрута в метрах (известна после генерации мира).
export function getRules(zone, mode, routeLength) {
  const r = {
    mode, title: MODE_INFO[mode].title, desc: MODE_INFO[mode].desc,
    useCheckpoints: true, laps: 1, timeLimit: 0, timeBonus: 0, targetKm: 0,
    gold: 60, silver: 80, bronze: 110,
    damage: true, maxHeavy: 0, collisionPenalty: 0, respawnPenalty: 0,
    traffic: false, reward: 250, xp: 200, xpPerKm: 25, creditsPerKm: 15,
  };
  const ref = routeLength / zone.refSpeed;
  r.gold = Math.round(ref);
  r.silver = Math.round(ref * 1.25);
  r.bronze = Math.round(ref * 1.6);
  r.traffic = zone.id === 'Highway' || zone.id === 'City' || zone.id === 'OpenWorld';

  switch (mode) {
    case 'FreeRide':
      r.useCheckpoints = false; r.reward = 0; r.xp = 0; break;
    case 'TimeTrial':
      r.respawnPenalty = 5; r.reward = 300; r.xp = 220; break;
    case 'CheckpointRace':
      r.timeLimit = Math.round(ref * 0.32); r.timeBonus = Math.round(ref * 0.12);
      r.respawnPenalty = 3; r.reward = 350; r.xp = 250; break;
    case 'OffroadChallenge':
      r.maxHeavy = 0; r.respawnPenalty = 5; r.reward = 450; r.xp = 320;
      r.gold = Math.round(ref * 1.0); r.silver = Math.round(ref * 1.3); r.bronze = Math.round(ref * 1.7); break;
    case 'CityChallenge':
      r.maxHeavy = 3; r.collisionPenalty = 5; r.respawnPenalty = 5; r.traffic = true; r.reward = 400; r.xp = 280; break;
    case 'HighwayRun':
      r.useCheckpoints = false; r.targetKm = 5; r.timeLimit = 210; r.traffic = true; r.maxHeavy = 3;
      r.collisionPenalty = 0; r.reward = 400; r.xp = 260;
      r.gold = 130; r.silver = 160; r.bronze = 190; break;
  }
  return r;
}

export const xpForLevel = (lvl) => 400 * (lvl - 1) * lvl / 2;
export function levelFromXp(xp) {
  let l = 1;
  while (xp >= xpForLevel(l + 1)) l++;
  return l;
}
