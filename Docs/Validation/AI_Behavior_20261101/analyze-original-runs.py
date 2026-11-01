import csv, json, collections
from pathlib import Path

work = Path(__file__).resolve().parent
def rows(path):
    return list(csv.DictReader(path.open(encoding='utf-8-sig', newline=''))) if path.exists() else []
def cell(value): return int(round(float(value or 0)))
def distance(x, z, gx, gz): return max(abs(cell(x)-cell(gx)), abs(cell(z)-cell(gz)))
results = []
labels = ('behavior-before-t15', 'behavior-after-intellect', 'behavior-after-combat',
          'behavior-final-intellect', 'behavior-final-combat', 'behavior-final-high',
          'behavior-release-combat', 'behavior-release-growth')
for label in labels:
    base = work / ('ai-' + label + '-matches')
    if not base.exists(): continue
    run = json.loads((base / 'run.json').read_text(encoding='utf-8-sig'))
    for folder in sorted(p for p in base.iterdir() if p.is_dir()):
        match = json.loads((folder / 'match.json').read_text(encoding='utf-8-sig'))
        if not match['FinishedUtc']: continue
        actions = rows(folder / 'actions.csv'); orders = rows(folder / 'operation-orders.csv')
        economy = rows(folder / 'nation-economy.csv'); turns = rows(folder / 'turns.csv')
        decisions = rows(folder / 'decisions.csv'); crystals = rows(folder / 'crystal-hp.csv')
        healing = rows(folder / 'healing-events.csv')
        successful_moves = {(r['team'],r['round'],r['actorLifeId'],r['type'],cell(r['afterX']),cell(r['afterZ']))
                            for r in actions if r['succeeded']=='True' and r['moved']=='True'}
        sides = {}
        for team in ('Player','Enemy'):
            a = [r for r in actions if r['team']==team]
            o = [r for r in orders if r['team']==team and r['squadId'] and r['chosenOperation']==r['operationId']]
            # Once per real selection/squad; the observer emits one row per active operation.
            unique = {(r['round'],r['generation'],r['squadId']):r for r in o}.values()
            actual_groups = []
            for r in unique:
                key = (team,r['round'],r['chosenActor'],r['chosenType'],cell(r['destinationX']),cell(r['destinationZ']))
                count = int(r.get('liveSquadSize') or len([x for x in r['squadMembers'].split(';') if x]))
                gain = distance(r['actorX'],r['actorZ'],r['targetX'],r['targetZ']) - distance(r['destinationX'],r['destinationZ'],r['targetX'],r['targetZ'])
                if key in successful_moves and count>=3:
                    actual_groups.append(dict(round=int(r['round']),squad=r['squadId'],operation=r['operationId'],goal=r['goal'],members=count,
                        span=float(r['squadSpan']) if r.get('squadSpan') else None,healers=int(r.get('squadHealers') or 0),gain=gain,actor=r['chosenActor']))
            e = [r for r in economy if r['team']==team]
            relevant = [r for r in decisions if r['team']==team and r['stage']=='selected' and r['action']=='Summon'
                        and r['priority']=='3' and r['APAffordable']=='True' and r['chosenAction']=='Attack' and r['chosenPriority']=='2']
            structure_attacks = [r for r in a if r['type']=='Attack' and r['targetCategory'] in ('SubCrystal','Building')]
            sides[team] = {k: match[team][k] for k in ('Moves','Attacks','Skills','Builds','Summons','EnemyKills','VictoryObjectiveAttacks','FailedActions','Oscillations')}
            sides[team].update(peakArmy=max((int(r['army']) for r in e),default=0),peakHealers=max((int(r['healers']) for r in e),default=0),
                maxSelectedGroup=max((int(r.get('liveSquadSize') or len([x for x in r['squadMembers'].split(';') if x])) for r in o),default=0),
                groupedActualMoves=len(actual_groups),fourMemberActualMoves=sum(r["members"]==4 for r in actual_groups),
                healerSquadActualMoves=sum(r["healers"]>0 for r in actual_groups),fourMemberCohesiveMoves=sum(r["members"]==4 and r["span"] is not None and r["span"]<=8 for r in actual_groups),
                cohesiveActualMoves=sum(r['span'] is not None and r['span']<=8 for r in actual_groups),
                advancingGroupMoveExamples=[r for r in actual_groups if r['gain']>0 and r['span'] is not None and r['span']<=8][:5],
                plannedSummonLostToP2AttackSamples=len(relevant),plannedSummonLostRounds=len(set(r['round'] for r in relevant)),
                actualSkillHealActions=sum(r['type']=='SkillUse' and int(r.get('actualHealingDone') or 0)>0 for r in a),
                unitUpkeepFailureRounds=len(set(r['round'] for r in turns if int(r[team.lower()+'UnpaidUnits'])>0)),
                structureAttackHpDamage=sum(int(r['damageDealt']) for r in structure_attacks),
                healerSkillSelections=[r for r in rows(folder/'skills.csv') if r['team']==team and r['stage']=='chosen'
                    and r['casterKind'] in ('WhiteMage','HolyKnight','Priest')],
                lastRecruitmentRoles=e[-1]['plannedRecruitRoles'] if e else '')
        increments=[{k:r[k] for k in ('round','team','kind','HP','maximumHP','previousMaximumHP','actualHeal','caller')}
                    for r in crystals if int(r['previousMaximumHP'])>0 and int(r['maximumHP'])>int(r['previousMaximumHP'])]
        # Before e687, RecordHeal did not publish HpChangeCommitted. Empty healing streams from
        # that instrumentation are NOT evidence of zero recovery and must not be counted.
        fixed_heal_stream=run['CodeCommit'].startswith('e687e3c')
        result=dict(label=label,folder=folder.name,seed=match['MapSeed'],threat=match['ThreatLevel'],personality=match['EnemyPersonality'],
            code=run['CodeCommit'],runtimeDigest=run['SourceDigest'],assetDigest=run['AssetDigest'],rounds=match['Round'],result=match['Result'],end=match['EndCondition'],
            seconds=match['Seconds'],errors=match['Errors'],exceptions=match['Exceptions'],p95FrameMs=match['P95FrameMs'],worstFrameMs=match['WorstFrameMs'],
            memoryStartMB=match['InitialMemoryMB'],memoryEndMB=match['FinalMemoryMB'],sides=sides,crystalIncreases=increments,
            healingStreamValid=fixed_heal_stream,healingEventCount=len(healing) if fixed_heal_stream else None,
            healingCallers=dict(collections.Counter(r['caller'] for r in healing)) if fixed_heal_stream else None,
            duplicateMembershipSamples=sum(len(ids:=[x for x in r['operationMembers'].split(';') if x])!=len(set(ids)) for r in orders))
        results.append(result)
(work/'behavior-audit-summary.json').write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf-8')
for r in results:
    print(json.dumps({k:r[k] for k in ('label','seed','rounds','result','errors','duplicateMembershipSamples')} | {'enemy':{k:r['sides']['Enemy'][k] for k in ('Summons','peakArmy','peakHealers','maxSelectedGroup','groupedActualMoves','cohesiveActualMoves','VictoryObjectiveAttacks')},'growthEvents':len(r['crystalIncreases']),'validHealingEvents':r['healingEventCount']},ensure_ascii=True))
