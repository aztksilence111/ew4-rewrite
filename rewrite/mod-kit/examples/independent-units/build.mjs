import { readFileSync, writeFileSync, mkdirSync, existsSync } from 'node:fs';
import { resolve, join, dirname } from 'node:path';

const data = resolve(process.argv[2] || 'data');
const output = resolve(process.argv[3] || 'independent-units');
if (existsSync(output)) throw Error('Output already exists; choose a new directory.');
const read = path => JSON.parse(readFileSync(join(data, path), 'utf8'));
const units = read('legacy/units.json'), manifest = read('client/manifest.json'), visuals = read('client/unit-visuals.json'), effects = read('client/effects.json');
const offers = [];
for (const [id, template, name, kind, money, industry] of [
  ['example.unit.rifle.0', 'ew4.unit.fra.militia.0', '独立步兵示例', 'city', 90, 10],
  ['example.unit.cruiser.0', 'ew4.unit.fra.frigate.0', '独立巡洋舰示例', 'port', 180, 40],
]) {
  if (units.units.some(u => u.id === id) || manifest.profiles[id]) throw Error('Profile already exists: ' + id);
  const source = units.units.find(u => u.id === template); if (!source) throw Error('Missing template: ' + template);
  units.units.push({ ...structuredClone(source), id, max_health: 300 });
  const visual = structuredClone(manifest.profiles[template]);
  Object.assign(visual, { name, description: '与原兵种共存，复用现有美术的接口示例。', recruitGroup: id });
  visual.stats = { ...visual.stats, strength: '300' }; manifest.profiles[id] = visual;
  const motion = visuals.profiles[template] ?? effects.bindings[template];
  if (motion) visuals.profiles[id] = motion;
  if (visuals.composites?.[template]) visuals.composites[id] = structuredClone(visuals.composites[template]);
  offers.push({ id: id + '.recruit', profile: id, buildingKinds: [kind], minimumLevel: 1, cost: { money, industry, food: 0 }, countries: ['fra'], scenarios: ['conquest1'] });
}
const files = { 'legacy/units.json': units, 'client/manifest.json': manifest, 'client/unit-visuals.json': visuals,
  'rewrite/extension.json': { schema: 1, apiVersion: 3, unitRecruitment: 'rewrite/unit-recruitment.json' },
  'rewrite/unit-recruitment.json': { schema: 1, offers } };
for (const [path, value] of Object.entries(files)) { const target = join(output, path); mkdirSync(dirname(target), { recursive: true }); writeFileSync(target, JSON.stringify(value, null, 2) + '\n'); }
console.log(JSON.stringify({ files: Object.keys(files).length, newProfiles: offers.length, output }));
