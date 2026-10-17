from pathlib import Path
import json
import re
import yaml

ROOT = Path(r'E:\fantasykingdom\Unity\fantasy kingdom')
BASE = ROOT / 'Assets/_FantasyKingdom/GameContent/R1Production'
def read(path):
    raw = path.read_text(encoding='utf-8-sig')
    raw = re.sub(r'^%.*\n', '', raw, flags=re.M)
    raw = re.sub(r'^--- !u!\d+ &\d+.*\n', '---\n', raw, flags=re.M)
    return yaml.safe_load(raw)['MonoBehaviour']

meta_map = {}
for path in (ROOT / 'Assets').rglob('*.meta'):
    found = re.search(r'^guid: (\w+)', path.read_text(encoding='utf-8-sig'), re.M)
    if found:
        meta_map[found.group(1)] = Path(str(path)[:-5])
settings = read(BASE / 'R1ProductionSettings.asset')
assert len(settings['entries']) == 16
assert len(set(e['kind'] for e in settings['entries'])) == 16
assert sum(e['createPlayableUnit'] for e in settings['entries']) == 6
rows = []
for e in settings['entries']:
    assert e['sourceData']['guid'] in meta_map
    assert e['sourcePrefab']['guid'] in meta_map
    assert e['skillDefaults']['guid'] in meta_map
    source = read(meta_map[e['sourceData']['guid']])
    definition_path = meta_map[e['skillDefaults']['guid']]
    d = read(definition_path)
    assert source['baseHP'] > 0 and source['baseATK'] >= 0 and source['baseDEF'] >= 0
    assert d['enabledForPlay'] == 1 and d['apCost'] >= 0 and d['cooldown'] >= 0
    assert d['ability'] == len(rows) + 1
    role = definition_path.stem
    expected = {
        'Knight': [('knightVisibleEnemyAtkPenalty', .1), ('knightPenaltyStackRule', 1)],
        'Magic': [('sacrificeLimit', 3)], 'Assassin': [('assassinBoostDuration', 2), ('assassinBoostStackRule', 1)],
        'Scout': [('scoutFreeMoveScope', 1), ('scoutFreeAttackScope', 1)], 'Guardian': [('guardianAttackCostReset', 1)],
        'Crossbow': [('crossbowInitialAmmo', 2), ('lightningFactionMask', 1)],
        'Bomber': [('bombExplosionRadius', 2), ('bomberAutoExplosionRadius', 2), ('bombFactionMask', 7), ('bombOnHolderDeathPolicy', 1)],
        'HolyKnight': [('holyKnightInterruptionConfigured', 1), ('holyKnightInterruptOnDamage', 0)],
        'Samurai': [('samuraiGaugeGain', 1), ('samuraiGaugeMax', 3)],
    }
    for key, value in expected.get(role, []):
        assert d[key] == value, (role, key, d[key])
    for name in {'Archer': ['stationaryAttackCurve'], 'Magicsniper': ['sniperRecoilCurve'], 'Ninja': ['ninjaChargeCurve'],
                 'Berserker': ['berserkerAttackCurve', 'berserkerDefenceCurve', 'berserkerAttackAPCurve'],
                 'Navigator': ['navigatorAPDamageCurve']}.get(role, []):
        assert len(d[name]['m_Curve']) >= 2
    if role == 'Archer':
        assert [(p['time'], p['value']) for p in d['stationaryAttackCurve']['m_Curve']] == [(0,1),(1,1.15),(2,1.3),(3,1.45),(4,1.6),(5,1.75)]
    if role == 'Berserker':
        assert [(p['time'], p['value']) for p in d['berserkerAttackAPCurve']['m_Curve']] == [(0,1),(1,4)]
    if role == 'HolyKnight':
        assert {(v['x'],v['y']) for v in d['holyKnightArea']} == {(x,z) for x in range(-2,3) for z in range(-2,3)}
    if role == 'Ninja':
        assert read(meta_map[e['attackPatternSource']['guid']])['kind'] == 3
    rows.append({'role': role, 'ability': d['ability'], 'ap': d['apCost'], 'cooldown': d['cooldown'],
                 'source': str(meta_map[e['sourceData']['guid']].relative_to(ROOT)), 'new_role': bool(e['createPlayableUnit'])})
path = ROOT / 'Docs/PerformanceData/r1-production-20261017/settings-static.json'
path.parent.mkdir(parents=True, exist_ok=True)
path.write_text(json.dumps({'status':'PASS', 'scope':'Serialized template schema, GUID references, specified provisional tuning, no Unity execution',
                            'roles':rows}, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(f'PASS: 16 serialized defaults, six inheritance sources, provisional fields and GUID references. Evidence: {path}')
