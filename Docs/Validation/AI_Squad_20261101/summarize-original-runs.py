import csv, json
from pathlib import Path
from collections import Counter

root = Path(__file__).resolve().parent
results = []
for label in ('squad-release-t15', 'squad-release-t3', 'squad-release-t35'):
    for folder in sorted((root / ('ai-' + label + '-matches')).glob('*/')):
        if not (folder / 'match.json').exists(): continue
        match = json.loads((folder / 'match.json').read_text(encoding='utf-8-sig'))
        actions = list(csv.DictReader((folder / 'actions.csv').open(encoding='utf-8-sig', newline='')))
        orders = list(csv.DictReader((folder / 'operation-orders.csv').open(encoding='utf-8-sig', newline='')))
        duplicate_samples = sum(len(ids := [i for i in r['operationMembers'].split(';') if i]) != len(set(ids)) for r in orders)
        obj = list(csv.DictReader((folder / 'objectives-by-turn.csv').open(encoding='utf-8-sig', newline='')))
        sides = {}
        for team in ('Player', 'Enemy'):
            side = match[team]
            teamorders = [r for r in orders if r['team'] == team]
            teama = [r for r in actions if r['team'] == team]
            selected = {(r['round'], r['generation']): r for r in teamorders}.values()
            sides[team] = {k: side[k] for k in ('Moves', 'Attacks', 'Skills', 'Builds', 'Summons', 'ObjectiveAttacks', 'VictoryObjectiveAttacks', 'CommanderTargetAttacks', 'MainCrystalTargetAttacks', 'EnemyKills', 'FailedActions', 'FailedProgressActions')}
            sides[team]['modeDecisionCounts'] = dict(Counter(r['mode'] for r in selected))
            sides[team]['operatedActionCount'] = sum(r['operationId'] not in ('', '0') for r in teama)
            sides[team]['maxOperationMembers'] = max((int(r['assigned']) for r in teamorders), default=0)
            sides[team]['coordinatedSelectionSamples'] = sum(int(r['coordinationSteps']) >= 2 for r in selected)
            sides[team]['observedMainRounds'] = sum(r['team'] == team and r['mainCrystalObservedAny'] == 'True' for r in obj)
            econ = list(csv.DictReader((folder / 'nation-economy.csv').open(encoding='utf-8-sig', newline='')))
            snapshots = {r['round']: r for r in econ if r['team'] == team}
            sides[team]['economySnapshots'] = [{k: r[k] for k in ('round', 'mode', 'army', 'healers', 'developmentFightingTarget', 'plannedRecruitRoles')} for n, r in snapshots.items() if int(n) % 20 == 0]
        results.append(dict(label=label, seed=match['MapSeed'], threat=match['ThreatLevel'], rounds=match['Round'], result=match['Result'], condition=match['EndCondition'], errors=match['Errors'], exceptions=match['Exceptions'], duplicateSamples=duplicate_samples, p95FrameMs=match['P95FrameMs'], worstFrameMs=match['WorstFrameMs'], initialMemoryMB=match['InitialMemoryMB'], finalMemoryMB=match['FinalMemoryMB'], seconds=match['Seconds'], sides=sides))
(root / 'squad-release-summary.json').write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(results, ensure_ascii=True, indent=2))
