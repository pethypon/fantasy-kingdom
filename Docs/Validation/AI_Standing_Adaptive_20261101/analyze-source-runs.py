from pathlib import Path
import csv,json,collections
work=Path(__file__).resolve().parent
labels=['economy-balance-before','economy-balance-trial','economy-balance-final','economy-army-deadline-probe','economy-adaptive-release','economy-adaptive-completion','economy-adaptive-funded','economy-adaptive-funded-high','economy-foundation-proof','standing-army-release','standing-army-funded-core','standing-army-final','standing-army-final-high']
summary=[]
for label in labels:
 folder=work/('ai-'+label+'-matches')
 if not folder.exists():continue
 run=json.loads((folder/'run.json').read_text(encoding='utf-8-sig'))
 for p in sorted(folder.glob('*/match.json')):
  m=json.loads(p.read_text(encoding='utf-8-sig'))
  if not m.get('FinishedUtc'):continue
  rows=list(csv.DictReader((p.parent/'nation-economy.csv').open(encoding='utf-8-sig')))
  turns=list(csv.DictReader((p.parent/'turns.csv').open(encoding='utf-8-sig')))
  actions=list(csv.DictReader((p.parent/'actions.csv').open(encoding='utf-8-sig')))
  report=dict(label=label,folder=p.parent.name,seed=m['MapSeed'],threat=m['ThreatLevel'],rounds=m['Round'],ownerTurns=m['OwnerTurns'],seconds=m['Seconds'],end=m['EndCondition'],result=m['Result'],gameOver=m['GameOver'],errors=m['Errors'],exceptions=m['Exceptions'],fault=m['Fault'],sourceDigest=run.get('SourceDigest'),assetDigest=run.get('AssetDigest'),codeCommit=run.get('CodeCommit'))
  for team in ['Player','Enemy']:
   rr=[x for x in rows if x['team']==team and x['resource']=='Bread']; prefix=team.lower()
   unpaid=sorted(set(int(x['round']) for x in turns if int(x[prefix+'UnpaidUnits'])>0))
   ar=[x for x in actions if x['team']==team]; adaptive=[float(x.get('adaptiveModifier') or 0) for x in ar if abs(float(x.get('adaptiveModifier') or 0))>.001]
   report[team]=dict(finalArmy=int(rr[-1]['army']),peakArmy=max(int(x['army']) for x in rr),finalHouses=int(rr[-1]['houses']),peakHouses=max(int(x['houses']) for x in rr),finalHouseLevels=int(rr[-1]['houseLevels']),finalAP=int(rr[-1]['currentMaxAP']),unpaidRounds=unpaid,openingUnpaidRounds=[x for x in unpaid if x<=30],firstMine=next((int(x['round']) for x in rr if 'Mine=' in x['facilityCounts']),None),attacks=m[team]['Attacks'],summons=m[team]['Summons'],skills=m[team]['Skills'],actions=m[team]['Actions'],adaptiveSelections=len(adaptive),adaptiveMin=min(adaptive,default=0),adaptiveMax=max(adaptive,default=0),adaptivePolicy=m[team].get('AdaptivePolicy'),finalFacilities=rr[-1]['facilityCounts'])
  for team in ['Player','Enemy']:
   ar=[x for x in actions if x['team']==team and x['type']=='Summon' and x['succeeded']=='True']
   report[team]['firstSummon']=min((int(x['round']) for x in ar),default=None)
   report[team]['firstHealer']=min((int(x['round']) for x in ar if x['targetCategory']=='Healer'),default=None)
   report[team]['scoutSummons']=sum(x['targetCategory']=='Scout' for x in ar)
   rr=[x for x in rows if x['team']==team and x['resource']=='Bread']
   report[team]['peakHealers']=max(int(x['healers']) for x in rr)
   report[team]['peakCitizens']=max(float(x['citizen']) for x in rr)
   report[team]['finalCitizens']=float(rr[-1]['citizen'])
   report[team]['finalCapacity']=int(rr[-1]['capacity'])
   report[team]['checkpoints']={}
   for checkpoint in [10,15,20,30,60]:
    snapshots=[x for x in rr if int(x['round'])==checkpoint]
    if snapshots:
     x=snapshots[-1];report[team]['checkpoints'][checkpoint]={k:x[k] for k in ['army','healers','houses','citizen','capacity','plannedRecruitRoles']}
   heal=list(csv.DictReader((p.parent/'healing-events.csv').open(encoding='utf-8-sig')))
   # R1ActorTurnStart currently records Priest's passive aura. No source ID is provided by that legacy path.
   events=[x for x in heal if x['team']==team and 'R1ActorTurnStart' in x['caller']]
   report[team]['priestAuraHPReceived']=sum(int(x['actualHeal']) for x in events)
   report[team]['priestAuraHealEvents']=len(events)
  summary.append(report)
(work/'economy-adaptive-summary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf-8')
for r in summary:
 print(r['label'],r['seed'],r['rounds'],r['end'],r['errors'],[(t,r[t]['finalArmy'],r[t]['peakArmy'],r[t]['peakHouses'],len(r[t]['openingUnpaidRounds']),r[t]['firstMine'],r[t]['adaptiveSelections']) for t in ['Player','Enemy']])
