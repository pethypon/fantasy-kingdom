"""Summarize executed army/facility/siege observations, never substitute model predictions."""
import collections, csv, json
from pathlib import Path

work = Path(__file__).resolve().parent
results = []
def read(path):
    return list(csv.DictReader(path.open(encoding='utf-8-sig'))) if path.exists() else []
def census(text):
    return {item.split('=')[0]: dict(zip(('count', 'levelSum'), map(int, item.split('=')[1].split(':'))))
        for item in text.split(';') if '=' in item}

for label in ('army-siege-before', 'army-siege-after', 'army-siege-release', 'army-siege-high', 'army-siege-completion', 'army-siege-final-high'):
    folder = work / ('ai-' + label + '-matches')
    if not (folder / 'run.json').exists(): continue
    run = json.loads((folder / 'run.json').read_text(encoding='utf-8-sig'))
    for match in sorted(folder.glob('*/match.json')):
        raw = json.loads(match.read_text(encoding='utf-8-sig'))
        if not raw['FinishedUtc']: continue
        d = match.parent
        actions = read(d / 'actions.csv'); nations = read(d / 'nation-economy.csv')
        turns = read(d / 'turns.csv'); objectives = read(d / 'objectives-by-turn.csv')
        teams = {}
        for team in ('Player', 'Enemy'):
            a = [x for x in actions if x['team'] == team and x['succeeded'] == 'True']
            n = [x for x in nations if x['team'] == team]
            o = [x for x in objectives if x['team'] == team]
            pairs = collections.defaultdict(list)
            # A SubCrystal construction command is NOT an attack on a crystal.
            for x in a:
                if x['type'] in ('Attack', 'SkillUse') and (x['targetCategory'] in ('Crystal', 'SubCrystal')
                    or x['targetsOpposingMainCrystal'] == 'True') and x['targetLifeId']:
                    pairs[x['targetLifeId']].append(x)
            sieges = []
            for life, hits in pairs.items():
                death = [x for x in hits if x['targetObservedBefore'] == 'True' and int(x['observedTargetHpBefore']) > 0
                    and int(x['damageDealt']) >= int(x['observedTargetHpBefore'])]
                sieges.append(dict(target=life, category=hits[0]['targetCategory'], firstRound=int(hits[0]['round']),
                    lastAttackRound=int(hits[-1]['round']), firstObservedHp=int(hits[0]['observedTargetHpBefore']),
                    attacks=len(hits), hpDamage=sum(int(x['damageDealt']) for x in hits),
                    AP=sum(int(x['ActionAPCost']) for x in hits), killRound=int(death[0]['round']) if death else None,
                    activeAttackRounds=len({x['round'] for x in hits}), attackers=len({x['actorLifeId'] for x in hits}),
                    elapsedRoundsToKill=int(death[0]['round'])-int(hits[0]['round'])+1 if death else None,
                    censored=not bool(death)))
            mix = collections.Counter(x['actionKey'].split(':')[2] for x in a if x['type'] == 'Summon'
                and len(x['actionKey'].split(':')) >= 3)
            last = n[-1]
            teams[team] = dict(peakObservedArmy=max(int(x['army']) for x in n), lastObservedArmy=int(last['army']),
                lastCensusRound=int(last['round']), liveFacilities=census(last['facilityCounts']), summons=raw[team]['Summons'],
                summonMix=dict(mix), builds=raw[team]['Builds'], upgrades=sum(x['type']=='Upgrade' for x in a),
                failedActions=raw[team]['FailedActions'], objectiveAttacks=raw[team]['VictoryObjectiveAttacks'],
                commanderObservedRounds=sum(x['kingObservedAny']=='True' for x in o),
                mainCrystalObservedRounds=sum(x['mainCrystalObservedAny']=='True' for x in o),
                knownTerritoryRounds=sum(x['knownEnemyTerritoryAny']=='True' for x in o),
                unpaidRounds=len({x['round'] for x in turns if int(x[team.lower()+'UnpaidUnits'])>0}),
                lastRecruitment=last['plannedRecruitRoles'], sieges=sieges)
        results.append(dict(label=label, folder=d.name, code=run['CodeCommit'], sourceDigest=run['SourceDigest'],
            assetDigest=run['AssetDigest'], seed=raw['MapSeed'], threat=raw['ThreatLevel'], personality=raw['EnemyPersonality'],
            rounds=raw['Round'], ownerTurns=raw['OwnerTurns'], result=raw['Result'], end=raw['EndCondition'],
            errors=raw['Errors'], exceptions=raw['Exceptions'], seconds=raw['Seconds'],
            p95FrameMs=raw['P95FrameMs'], worstFrameMs=raw['WorstFrameMs'], teams=teams))
(work / 'army-siege-summary.json').write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding='utf-8')
for x in results:
    e=x['teams']['Enemy']
    print(json.dumps(dict(label=x['label'], seed=x['seed'], rounds=x['rounds'], result=x['result'], errors=x['errors'],
        enemy={k:e[k] for k in ('lastObservedArmy','peakObservedArmy','builds','upgrades','summons','summonMix','objectiveAttacks')},
        enemySieges=e['sieges']), ensure_ascii=True))
