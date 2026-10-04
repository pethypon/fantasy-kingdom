using System.Collections.Generic;
using UnityEngine;

/// <summary>Bounded probability distributions derived only from past observations and public movement rules.</summary>
public sealed class AIBeliefMap
{
    public const int MaxContacts = 24, MaxCellsPerContact = 256;
    public sealed class Contact
    {
        public int Id;
        public Kind Kind;
        public Direction Direction;
        public int ObservedAttackPower;
        public BoardActionProfile ActionProfile;
        public float UnknownProbability;
        public readonly Dictionary<Vector3Int, float> Cells = new Dictionary<Vector3Int, float>();
    }
    readonly AIBoardState board;
    readonly Contact[] pool = new Contact[MaxContacts];
    readonly Dictionary<Vector3Int,float> expectedRisk = new Dictionary<Vector3Int,float>(), worstRisk = new Dictionary<Vector3Int,float>();
    readonly Dictionary<Vector3Int,float> contactRisk = new Dictionary<Vector3Int,float>();
    readonly HashSet<Vector2Int> customAttackOffsets = new HashSet<Vector2Int>();
    static readonly Dictionary<Kind,Vector2Int[]> attackOffsets = AttackOffsets();
    float unknownRisk;
    int evidenceTurn=-1, evidenceHash;
    readonly List<Contact> contacts = new List<Contact>();
    readonly Dictionary<Vector3Int, float> combined = new Dictionary<Vector3Int, float>();
    readonly Dictionary<Vector3Int, float> next = new Dictionary<Vector3Int, float>();
    readonly HashSet<int> visible = new HashSet<int>();
    readonly List<KeyValuePair<int, AIBoardState.LastKnownInfo>> memories = new List<KeyValuePair<int, AIBoardState.LastKnownInfo>>();
    int generation = -1, level = -1;
    public int Rebuilds { get; private set; }
    public AIBeliefMap(AIBoardState board) { this.board = board; }
    public IReadOnlyList<Contact> Contacts { get { Prepare(); return contacts; } }
    public float ProbabilityAt(Vector3 cell) { Prepare(); return combined.TryGetValue(GridHelper.ToGridXZ(cell), out var p) ? p : 0; }

    void Prepare()
    {
        if (generation == board.Generation && level == board.ReconThreatLevel) return;
        generation = board.Generation; level = board.ReconThreatLevel;
        int stamp=board.BeliefEvidenceStamp;
        if(evidenceTurn==board.TurnCount && evidenceHash==stamp) return;
        evidenceTurn=board.TurnCount;evidenceHash=stamp; Rebuilds++;
        combined.Clear(); expectedRisk.Clear(); worstRisk.Clear(); unknownRisk=0; visible.Clear(); memories.Clear();
        foreach (var unit in board.AlivePlayerUnits)
            if (unit != null && board.IsVisibleToEnemy(unit.transform.position)) visible.Add(unit.GetInstanceID());
        foreach (var pair in board.ObservedHistory)
            if (pair.Value.Valid && pair.Value.Type == Type.Unit && !visible.Contains(pair.Key)
                && board.TurnCount - pair.Value.Turn < 7) memories.Add(pair);
        // Recent sightings first; a stable tie-breaker makes pruning reproducible.
        memories.Sort((a, b) => a.Value.Turn == b.Value.Turn ? a.Key.CompareTo(b.Key) : b.Value.Turn.CompareTo(a.Value.Turn));
        int count = level >= 10 ? Mathf.Min(MaxContacts, memories.Count) : 0;
        contacts.Clear();
        for(int i=0;i<count;i++)
        {
            var contact=pool[i]??(pool[i]=new Contact());contacts.Add(contact);
            Build(contact, memories[i].Key, memories[i].Value);
            BuildRisk(contact);
        }
    }

    void Build(Contact contact, int id, AIBoardState.LastKnownInfo memory)
    {
        contact.Id = id; contact.Kind = memory.Kind; contact.Direction = memory.Direction; contact.ObservedAttackPower=memory.ObservedAttackPower; contact.ActionProfile = memory.ActionProfile;
        contact.Cells.Clear(); contact.Cells[GridHelper.ToGridXZ(memory.Position)] = 1;
        int age = Mathf.Clamp(board.TurnCount - memory.Turn, 0, 6);
        int passes = Mathf.Min(4, age * (memory.Kind == Kind.Scout || memory.Kind == Kind.Assassin ? 2 : 1));
        for (int pass = 0; pass < passes; pass++)
        {
            next.Clear();
            foreach (var cell in contact.Cells)
            {
                var offsets = MovePatterns.Offsets(memory.Kind, memory.ActionProfile);
                int possibilities = 1 + offsets.Count * (MovePatterns.IsDirectionIndependent(memory.Kind, memory.ActionProfile) ? 1 : 2);
                float share = cell.Value / possibilities;
                Add(next, cell.Key, share);
                for(int oi=0;oi<offsets.Count;oi++)
                {
                    var offset=offsets[oi];
                    var destination = cell.Key + new Vector3Int(offset.x, 0, offset.y * MovePatterns.DirZ(memory.Direction));
                    if (Possible(cell.Key, destination)) Add(next, destination, share);
                    if (!MovePatterns.IsDirectionIndependent(memory.Kind, memory.ActionProfile))
                    {
                        destination = cell.Key + new Vector3Int(offset.x, 0, -offset.y * MovePatterns.DirZ(memory.Direction));
                        if (Possible(cell.Key, destination)) Add(next, destination, share);
                    }
                }
            }
            contact.Cells.Clear();
            // The cap retains an explicit unknown mass rather than renormalizing uncertainty away.
            foreach (var cell in next)
                if (contact.Cells.Count < MaxCellsPerContact) contact.Cells[cell.Key] = cell.Value;
        }
        next.Clear(); float retained = 0;
        float confidence = Mathf.Clamp01(1 - age * .10f);
        foreach (var cell in contact.Cells)
        {
            if (board.IsVisibleToEnemy(cell.Key)) continue; // Empty visible cells rule out this missing identity.
            float p = cell.Value * confidence;
            next[cell.Key] = p; retained += p;
            combined.TryGetValue(cell.Key, out var prior);
            combined[cell.Key] = 1 - (1 - prior) * (1 - p);
        }
        contact.Cells.Clear(); foreach (var cell in next) contact.Cells.Add(cell.Key, cell.Value);
        contact.UnknownProbability = Mathf.Clamp01(1 - retained);
    }

    static void Add(Dictionary<Vector3Int, float> into, Vector3Int cell, float value)
    { into.TryGetValue(cell, out float prior); into[cell] = prior + value; }

    bool Possible(Vector3Int from, Vector3Int to)
    {
        var map = board.ReconMap;
        if (map == null || to.x < 0 || to.z < 0 || to.x >= map.maxX || to.z >= map.maxZ) return false;
        // Inspect terrain only if every traversed cell has already been observed.
        int steps = Mathf.Max(Mathf.Abs(to.x - from.x), Mathf.Abs(to.z - from.z));
        for (int step = 1; step <= steps; step++)
        {
            var p = GridHelper.ToGridXZ(Vector3.Lerp(from, to, step / (float)steps));
            if (!board.IsTerrainKnown(p)) return true;
        }
        return map.CanTraverse(from, to);
    }

    public float InformationGain(Status unit, Vector3 destination)
    {
        Prepare(); float value = 0;
        foreach (var offset in VisionGenerator.BaseVisionOffsets(unit)) value += ProbabilityAt(destination + offset);
        return Mathf.Min(60, value * 60);
    }

    static Dictionary<Kind,Vector2Int[]> AttackOffsets()
    {
        var map=new Dictionary<Kind,Vector2Int[]>();
        foreach(var pair in AttackPatterns.NormalMap)
        {
            var list=new List<Vector2Int>();
            for(int x=-8;x<=8;x++)for(int z=-8;z<=8;z++)
                if(AttackPatterns.CanAttack(pair.Key,Direction.N,x,z)||AttackPatterns.CanAttack(pair.Key,Direction.S,x,z))list.Add(new Vector2Int(x,z));
            map[pair.Key]=list.ToArray();
        }
        return map;
    }
    void BuildRisk(Contact contact)
    {
        float damage=contact.ObservedAttackPower>0 ? contact.ObservedAttackPower : UnitStaticData.Table.TryGetValue(contact.Kind,out var u)?Mathf.Max(1,u.BaseATK):8;
        contactRisk.Clear();
        if (contact.ActionProfile != null && !contact.ActionProfile.CanAttack) return;
        if (contact.ActionProfile?.attack?.useCustom == true)
        {
            var mask = contact.ActionProfile.attack;
            customAttackOffsets.Clear();
            foreach (var offset in mask.Offsets)
            {
                if (offset == Vector2Int.zero) continue;
                customAttackOffsets.Add(offset);
                if (!mask.directionIndependent) customAttackOffsets.Add(new Vector2Int(offset.x, -offset.y));
            }
            foreach (var cell in contact.Cells) foreach (var offset in customAttackOffsets)
                Add(contactRisk, cell.Key + new Vector3Int(offset.x, 0, offset.y), cell.Value);
        }
        else if(attackOffsets.TryGetValue(contact.Kind,out var offsets))
            foreach(var cell in contact.Cells)
                for(int i=0;i<offsets.Length;i++)Add(contactRisk,cell.Key+new Vector3Int(offsets[i].x,0,offsets[i].y),cell.Value);
        foreach(var pair in contactRisk)
        {
            Add(expectedRisk,pair.Key,Mathf.Min(1,pair.Value)*damage);
            if(pair.Value>.02f) Add(worstRisk,pair.Key,damage);
        }
        unknownRisk+=contact.UnknownProbability*damage*.03f;
    }
    public float RiskAt(Vector3 destination,int threatLevel)
    {
        Prepare();var cell=GridHelper.ToGridXZ(destination);
        expectedRisk.TryGetValue(cell,out var expected);worstRisk.TryGetValue(cell,out var worst);
        float caution=Mathf.Lerp(.10f,.45f,Mathf.InverseLerp(10,100,threatLevel));
        // Several independent contacts may threaten the same cell. Caution must not
        // reduce the expected danger just because a single contact has low probability.
        return expected*(1-caution)+Mathf.Max(expected,worst)*caution+unknownRisk;
    }

    public bool TryGetPriority(out Vector3 target)
    {
        Prepare(); target = default; float best = 0;
        foreach (var cell in combined)
            if (cell.Value > best) { best = cell.Value; target = cell.Key; }
        return best > 0;
    }
}
