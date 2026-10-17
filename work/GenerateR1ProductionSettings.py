from pathlib import Path
import re
import uuid

ROOT = Path(r'E:\fantasykingdom\Unity\fantasy kingdom')
OUT = ROOT / 'Assets/_FantasyKingdom/GameContent/R1Production'

def guid(path):
    return re.search(r'^guid: (\w+)', Path(str(path) + '.meta').read_text(encoding='utf-8-sig'), re.M).group(1)

def meta(path, kind='asset'):
    target = Path(str(path) + '.meta')
    if target.exists():
        return guid(path)
    value = uuid.uuid4().hex
    if kind == 'folder':
        content = f'fileFormatVersion: 2\nguid: {value}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
    elif kind == 'script':
        content = f'fileFormatVersion: 2\nguid: {value}\nMonoImporter:\n  externalObjects: {{}}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {{instanceID: 0}}\n'
    else:
        content = f'fileFormatVersion: 2\nguid: {value}\nNativeFormatImporter:\n  externalObjects: {{}}\n  mainObjectFileID: 11400000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
    target.write_text(content, encoding='utf-8')
    return value

def folder(path):
    if not path.exists():
        path.mkdir(parents=True)
    meta(path, 'folder')

folder(OUT)
folder(OUT / 'Defaults')
settings_guid = meta(ROOT / 'Assets/Script/Unit&Battle/R1ProductionContentSettings.cs', 'script')
meta(ROOT / 'Assets/Editor/R1ProductionContentInstaller.cs', 'script')

roles = ['Knight', 'Archer', 'Magic', 'Assassin', 'Scout', 'Priest', 'Guardian', 'Crossbow', 'Magicsniper', 'Bomber',
         'WhiteMage', 'HolyKnight', 'Samurai', 'Ninja', 'Berserker', 'Navigator']
kind_ids = dict(zip(roles, list(range(2, 12)) + list(range(17, 23))))
cooldowns = [2, 3, 3, 3, 3, 0, 2, 0, 2, 3, 2, 5, 4, 2, 0, 2]
aps = [14, 10, 22, 14, 20, 8, 18, 10, 18, 20, 12, 15, 8, 13, 5, 12]
new_sources = dict(WhiteMage='Priest', HolyKnight='Knight', Samurai='Knight', Ninja='Assassin', Berserker='Knight', Navigator='Guardian')
template = (ROOT / 'Assets/_FantasyKingdom/GameContent/SpecialAbilities/Knight_3a6c94b3.asset').read_text(encoding='utf-8-sig')

def curve(points):
    lines = ['    serializedVersion: 2', '    m_Curve:']
    if not points:
        lines[-1] += ' []'
    for i, (time, value) in enumerate(points):
        incoming = 0 if i == 0 else (value - points[i-1][1]) / (time - points[i-1][0])
        outgoing = 0 if i == len(points)-1 else (points[i+1][1] - value) / (points[i+1][0] - time)
        lines += ['    - serializedVersion: 3', f'      time: {time:g}', f'      value: {value:g}',
                  f'      inSlope: {incoming:.8g}', f'      outSlope: {outgoing:.8g}', '      tangentMode: 0',
                  '      weightedMode: 0', '      inWeight: 0', '      outWeight: 0']
    lines += ['    m_PreInfinity: 2', '    m_PostInfinity: 2', '    m_RotationOrder: 4']
    return '\n'.join(lines) + '\n'

def scalar(text, name, value):
    pattern = re.compile(r'^  ' + re.escape(name) + r':.*$', re.M)
    if pattern.search(text):
        return pattern.sub('  ' + name + ': ' + str(value), text)
    return text.rstrip() + '\n  ' + name + ': ' + str(value) + '\n'

def replace_curve(text, name, points):
    return re.sub(r'^  ' + name + r':\n(?:    .*\n)*', '  ' + name + ':\n' + curve(points), text, flags=re.M)

default_guids = {}
for index, role in enumerate(roles):
    current_path = OUT / 'Defaults' / (role + '.asset')
    if current_path.exists():
        default_guids[role] = guid(current_path)
        continue
    asset = template
    asset = scalar(asset, 'm_Name', 'Defaults_' + role)
    asset = scalar(asset, 'ability', index + 1)
    asset = scalar(asset, 'displayName', '""')
    asset = scalar(asset, 'enabledForPlay', 1)
    asset = scalar(asset, 'apCost', aps[index])
    asset = scalar(asset, 'cooldown', cooldowns[index])
    for field, value in dict(knightPenaltyStackRule=0, assassinBoostDuration=-1, assassinBoostStackRule=0,
                             scoutFreeMoveScope=0, scoutFreeAttackScope=0, guardianAttackCostReset=0,
                             bomberAutoExplosionRadius=-1, crossbowInitialAmmo=-1).items():
        asset = scalar(asset, field, value)
    values = {
        'Knight': dict(knightVisibleEnemyAtkPenalty=.1, knightPenaltyStackRule=1),
        'Magic': dict(sacrificeLimit=3),
        'Assassin': dict(assassinBoostDuration=2, assassinBoostStackRule=1),
        'Scout': dict(scoutFreeMoveScope=1, scoutFreeAttackScope=1),
        'Guardian': dict(guardianAttackCostReset=1),
        'Crossbow': dict(crossbowInitialAmmo=2, lightningFactionMask=1),
        'Bomber': dict(bombExplosionRadius=2, bombFactionMask=7, bombOnHolderDeathPolicy=1, bomberAutoExplosionRadius=2),
        'HolyKnight': dict(holyKnightInterruptionConfigured=1, holyKnightInterruptOnDamage=0),
        'Samurai': dict(samuraiGaugeGain=1, samuraiGaugeMax=3),
    }.get(role, {})
    for name, value in values.items():
        asset = scalar(asset, name, value)
    curves = {
        'Archer': {'stationaryAttackCurve': [(0, 1), (1, 1.15), (2, 1.3), (3, 1.45), (4, 1.6), (5, 1.75)]},
        'Magicsniper': {'sniperRecoilCurve': [(0, 0), (25, .15)]},
        'Ninja': {'ninjaChargeCurve': [(0, 1), (5, 2)]},
        'Berserker': {
            'berserkerAttackCurve': list(enumerate([1, 1.3, 1.6, 1.9, 2.2, 2.5])),
            'berserkerDefenceCurve': list(enumerate([1, .9, .8, .7, .6, .5])),
            'berserkerAttackAPCurve': [(0, 1), (1, 4)],
        },
        'Navigator': {'navigatorAPDamageCurve': [(0, 1), (60, 1.5)]},
    }.get(role, {})
    for name, points in curves.items():
        asset = replace_curve(asset, name, points)
    if role == 'HolyKnight':
        area = '  holyKnightArea:\n' + ''.join(f'  - {{x: {x}, y: {z}}}\n' for z in range(-2, 3) for x in range(-2, 3))
        asset = asset.replace('  holyKnightArea: []\n', area)
    path = OUT / 'Defaults' / (role + '.asset')
    if path.exists():
        raise RuntimeError('Defaults already exist; refusing to overwrite tuning: ' + str(path))
    path.write_text(asset, encoding='utf-8')
    default_guids[role] = meta(path)

header = f'''%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 0}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {settings_guid}, type: 3}}
  m_Name: R1ProductionSettings
  m_EditorClassIdentifier: Assembly-CSharp::R1ProductionContentSettings
  version: R1-production-provisional-v1-20261017
  balanceStatus: "Provisional initial tuning v1. Skill costs, cooldowns, curves and inherited new-role stats are editable proposals, not confirmed R1 requirements."
  entries:
'''
for role in roles:
    source = new_sources.get(role, role)
    data_path = ROOT / 'Assets/Data/Unit' / (('Magic Sniper' if source == 'Magicsniper' else source) + '.asset')
    prefab_path = ROOT / 'Assets/Prefabs/Units/Player' / (('MagicSniper' if source == 'Magicsniper' else source) + '.prefab')
    prefab_id = re.search(r'--- !u!1 &(\d+)', prefab_path.read_text(encoding='utf-8-sig')).group(1)
    note = 'Provisional v1 skill tuning; existing baseline stats, costs, upkeep and masks are retained.'
    if role in new_sources:
        note = f'Provisional v1: base stats, growth, recruitment cost, upkeep and model inherited from {source}; editable independent production assets.'
    if role == 'Ninja':
        note += ' Movement and vision inherit Assassin; ranged kunai attack mask inherits Archer.'
    elif role == 'Navigator':
        note += ' Guardian attack mask matches the specified single forward cell.'
    attack_ref = f'{{fileID: 11400000, guid: {guid(ROOT / "Assets/Data/Unit/Archer.asset")}, type: 2}}' if role == 'Ninja' else '{fileID: 0}'
    header += f'''  - kind: {kind_ids[role]}
    sourceData: {{fileID: 11400000, guid: {guid(data_path)}, type: 2}}
    sourcePrefab: {{fileID: {prefab_id}, guid: {guid(prefab_path)}, type: 3}}
    attackPatternSource: {attack_ref}
    skillDefaults: {{fileID: 11400000, guid: {default_guids[role]}, type: 2}}
    createPlayableUnit: {int(role in new_sources)}
    balanceNote: "{note}"
'''
header += '  installed: []\n'
settings_path = OUT / 'R1ProductionSettings.asset'
if settings_path.exists():
    raise RuntimeError('Settings already exist; refusing to overwrite tuning.')
settings_path.write_text(header, encoding='utf-8')
meta(settings_path)
print('Created 16 provisional defaults and the explicit production installation settings.')
