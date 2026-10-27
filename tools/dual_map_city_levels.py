"""Assign provisional urban tiers without moving sites or changing terrain."""

from collections import Counter
from functools import lru_cache
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


@lru_cache(maxsize=1)
def policy():
    return json.loads((ROOT/'maps/dual_map_city_levels.json').read_text(encoding='utf-8'))


def city_level(key, site):
    if site['kind'] != 'city':
        return site.get('level', 1)
    matches = [int(level) for level, names in policy()[key].items() if site['name'] in names]
    assert len(matches) <= 1, site['name']+' declared in multiple tiers'
    return matches[0] if matches else policy()['defaultCityLevel']


def apply_levels():
    result = {}
    for key in ('japan', 'hre'):
        folder = ROOT/'maps'/key
        sites = json.loads((folder/'buildings.json').read_text(encoding='utf-8'))
        city_names = {s['name'] for s in sites if s['kind'] == 'city'}
        declared = [name for names in policy()[key].values() for name in names]
        assert len(set(declared)) == len(declared), key+' has duplicate declarations'
        assert not set(declared)-city_names, key+' unknown cities: '+str(set(declared)-city_names)
        modern_path = folder/(key+'.map.json')
        modern = json.loads(modern_path.read_text(encoding='utf-8'))
        changes = 0
        for site in sites:
            level = city_level(key, site)
            changes += site['level'] != level
            site['level'] = level
            if site['kind'] == 'city':
                site['sprite'] = f'city_{"east" if key == "japan" else "west"}_lv{level}.png'
            assert modern['cells'][site['cell']]['name'] == site['name']
            modern['cells'][site['cell']]['buildingLevel'] = level
        for target, value in ((folder/'buildings.json', sites), (modern_path, modern)):
            target.write_text(json.dumps(value, ensure_ascii=False, indent=2)+'\n', encoding='utf-8', newline='\n')
        result[key] = dict(changes=changes, levels=dict(sorted(Counter(s['level'] for s in sites if s['kind']=='city').items())),
            cities=[dict(id=s['id'], name=s['name'], cell=s['cell'], level=s['level']) for s in sites if s['kind']=='city'])
    (ROOT/'maps/japan_hre_source_20261026/city-starting-levels.json').write_text(
        json.dumps(result, ensure_ascii=False, indent=2)+'\n', encoding='utf-8', newline='\n')
    print(json.dumps({key:{k:v for k,v in value.items() if k!='cities'} for key,value in result.items()}))


if __name__ == '__main__':
    apply_levels()
