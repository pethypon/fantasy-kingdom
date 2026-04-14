using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  SimBoardState — 完全シミュレーション可能な盤面状態
//  GameObjectに一切依存せず、Clone()で効率的に複製可能
//  移動・攻撃・建築・召喚・ステータス効果をシミュレーション実行できる
//
//  実装は以下の partial ファイルに分離されている:
//    - SimBoardState.Actions.cs   行動適用 (Move/Attack/Build/Summon/Skill)
//    - SimBoardState.Combat.cs    ダメージ計算・シールド・AP式
//    - SimBoardState.Turn.cs      ターン遷移 (AP/DoT/Tick/CD)
//    - SimBoardState.Vision.cs    視界推定 (Raycast不要近似)
// =====================================================================
public partial class SimBoardState
{
    // ---- ユニットデータ ----
    public List<SimUnit> Units;

    // ---- AP ----
    public int EnemyAP;
    public int PlayerAP;

    // ---- AP基礎値（ターン開始リセット用） ----
    public int EnemyAPReset;
    public int PlayerAPReset;

    // ---- クリスタル ----
    public Vector3Int EnemyCrystalPos;
    public Vector3Int PlayerCrystalPos;

    // ---- 建築 ----
    public Dictionary<FacilityKind, int> EnemyBuildingCounts;
    public Dictionary<FacilityKind, int> PlayerBuildingCounts;

    // ---- マップデータ (共有・変更しない) ----
    public HashSet<Vector3Int> MapTiles; // 有効なタイル座標 (Y=0化済み)

    // ---- 占有セル ----
    HashSet<Vector3Int> _occupiedCells;

    // ---- ターン数 ----
    public int TurnCount;

    // ================================================================
    //  生成: 実際のゲーム状態からスナップショットを作成
    // ================================================================
    public static SimBoardState CreateFromGame(AIBoardState realBoard, MoveGenerator moveGen,
        UnitSetting unitSet, CrystalSystem crystalSystem, APSystem apSystem)
    {
        var state = new SimBoardState();
        state.Units = new List<SimUnit>();
        state.TurnCount = realBoard.TurnCount;

        // マップタイル
        state.MapTiles = new HashSet<Vector3Int>();
        if (moveGen != null && moveGen.mapcreate != null)
        {
            foreach (var sp in moveGen.mapcreate.SetPos)
            {
                state.MapTiles.Add(GridHelper.ToGridXZ(sp));
            }
        }

        // 敵ユニット
        int idCounter = 0;
        foreach (var u in realBoard.AliveEnemyUnits)
        {
            if (u == null || !u.gameObject.activeInHierarchy) continue;
            state.Units.Add(CaptureUnit(u, idCounter++));
        }

        // プレイヤーユニット (視界内のみ)
        foreach (var u in realBoard.AlivePlayerUnits)
        {
            if (u == null || !u.gameObject.activeInHierarchy) continue;
            state.Units.Add(CaptureUnit(u, idCounter++));
        }

        // クリスタル
        state.EnemyCrystalPos = ToCell(crystalSystem.ECP);
        state.PlayerCrystalPos = ToCell(crystalSystem.PCP);

        // クリスタルをユニットとして追加 (まだ追加されていなければ)
        var eCrystal = FindCrystalStatus(unitSet.EnemyUnit);
        if (eCrystal != null && !HasCrystal(state, Team.Enemy))
            state.Units.Add(CaptureUnit(eCrystal, idCounter++));

        var pCrystal = FindCrystalStatus(unitSet.PlayerUnit);
        if (pCrystal != null && realBoard.PlayerCrystalVisible && !HasCrystal(state, Team.Player))
            state.Units.Add(CaptureUnit(pCrystal, idCounter++));

        // AP
        state.EnemyAP = realBoard.EnemyAP;
        state.PlayerAP = 25; // プレイヤーAPは概算
        state.EnemyAPReset = Mathf.Max(15, realBoard.EnemyAP);
        state.PlayerAPReset = 25;

        // 建築カウント
        state.EnemyBuildingCounts = new Dictionary<FacilityKind, int>(realBoard.EnemyBuildingCounts);
        state.PlayerBuildingCounts = new Dictionary<FacilityKind, int>();

        state.RebuildOccupied();
        return state;
    }

    static bool HasCrystal(SimBoardState state, Team team)
    {
        for (int i = 0; i < state.Units.Count; i++)
            if (state.Units[i].Kind == Kind.Crystal && state.Units[i].Team == team)
                return true;
        return false;
    }

    static SimUnit CaptureUnit(Status s, int id)
    {
        var su = new SimUnit
        {
            Id = id,
            Team = s.team,
            Kind = s.kind,
            Type = s.type,
            HP = s.HP,
            MaxHP = s.MaxHP,
            ATK = s.ATK,
            DEF = s.DEF,
            Position = GridHelper.ToGridXZ(s.transform.position),
            Direction = s.direction,
            IsBoss = s.IsBoss,
            AssignedSkillId = s.AssignedSkillId,
            SkillCooldown = s.SkillCooldown,
            Fatigue = s.Fatigue,
            ShieldTurns = s.ShieldTurns,
            ShieldActivated = s.ShieldActivated,
            Passive = s.passiveskill,
        };

        // ステータス効果をコピー
        if (s.ActiveEffects != null)
        {
            for (int i = 0; i < s.ActiveEffects.Count; i++)
            {
                var e = s.ActiveEffects[i];
                if (e.IsDebuff)
                    su.Effects.Add(new SimEffect(e.debuffType, e.remainingTurns));
                else if (e.IsBuff)
                    su.Effects.Add(new SimEffect(e.buffType, e.remainingTurns));
            }
        }

        return su;
    }

    static Status FindCrystalStatus(Transform parent)
    {
        if (parent == null) return null;
        foreach (Status s in parent.GetComponentsInChildren<Status>())
        {
            if (s.kind == Kind.Crystal && s.HP > 0) return s;
        }
        return null;
    }

    // ================================================================
    //  Clone — 完全な盤面コピー (探索のノード展開で使用)
    // ================================================================
    public SimBoardState Clone()
    {
        var copy = SimBoardPool.RentBoard();
        copy.Units = SimBoardPool.RentUnitList(Units.Count);
        for (int i = 0; i < Units.Count; i++)
            copy.Units.Add(Units[i].Clone());
        copy.EnemyAP = EnemyAP;
        copy.PlayerAP = PlayerAP;
        copy.EnemyAPReset = EnemyAPReset;
        copy.PlayerAPReset = PlayerAPReset;
        copy.EnemyCrystalPos = EnemyCrystalPos;
        copy.PlayerCrystalPos = PlayerCrystalPos;
        // 辞書もプールから取得しコピー
        copy.EnemyBuildingCounts = SimBoardPool.RentDict();
        foreach (var kvp in EnemyBuildingCounts)
            copy.EnemyBuildingCounts[kvp.Key] = kvp.Value;
        copy.PlayerBuildingCounts = SimBoardPool.RentDict();
        foreach (var kvp in PlayerBuildingCounts)
            copy.PlayerBuildingCounts[kvp.Key] = kvp.Value;
        copy.MapTiles = MapTiles; // 共有参照 (変更しない)
        copy.TurnCount = TurnCount;
        copy.RebuildOccupied();
        return copy;
    }

    /// <summary>内部の占有セットHashSetをプールに返却</summary>
    public void ReturnOccupiedSet()
    {
        if (_occupiedCells != null)
        {
            SimBoardPool.ReturnHashSet(_occupiedCells);
            _occupiedCells = null;
        }
    }

    // ================================================================
    //  占有セル再構築
    // ================================================================
    public void RebuildOccupied()
    {
        if (_occupiedCells == null)
            _occupiedCells = SimBoardPool.RentHashSet();
        else
            _occupiedCells.Clear();

        _occupiedCells.Add(EnemyCrystalPos);
        _occupiedCells.Add(PlayerCrystalPos);
        for (int i = 0; i < Units.Count; i++)
        {
            if (Units[i].IsAlive && Units[i].Type == Type.Unit)
                _occupiedCells.Add(Units[i].Position);
        }
    }

    public bool IsOccupied(Vector3Int pos) => _occupiedCells.Contains(pos);

    // ================================================================
    //  ユニット検索
    // ================================================================
    public SimUnit GetUnit(int id)
    {
        for (int i = 0; i < Units.Count; i++)
            if (Units[i].Id == id) return Units[i];
        return null;
    }

    public List<SimUnit> GetAliveUnits(Team team)
    {
        var list = new List<SimUnit>();
        for (int i = 0; i < Units.Count; i++)
        {
            if (Units[i].IsAlive && Units[i].Team == team && Units[i].Type == Type.Unit)
                list.Add(Units[i]);
        }
        return list;
    }

    /// <summary>GCゼロ版: 呼び出し元が提供するリストに結果を書き込む</summary>
    public void GetAliveUnitsNonAlloc(Team team, List<SimUnit> result)
    {
        result.Clear();
        for (int i = 0; i < Units.Count; i++)
        {
            if (Units[i].IsAlive && Units[i].Team == team && Units[i].Type == Type.Unit)
                result.Add(Units[i]);
        }
    }

    public SimUnit GetCrystal(Team team)
    {
        for (int i = 0; i < Units.Count; i++)
        {
            if (Units[i].Kind == Kind.Crystal && Units[i].Team == team && Units[i].IsAlive)
                return Units[i];
        }
        return null;
    }

    public SimUnit GetKing(Team team)
    {
        for (int i = 0; i < Units.Count; i++)
        {
            if (Units[i].Kind == Kind.King && Units[i].Team == team && Units[i].IsAlive)
                return Units[i];
        }
        return null;
    }

    // ================================================================
    //  SimUnit作成ヘルパー (召喚用)
    // ================================================================
    static SimUnit CreateSimUnitFromKind(Kind kind, Team team, Vector3Int pos, int id)
    {
        int hp = 10, atk = 5, def = 3;
        if (UnitStaticData.Table.TryGetValue(kind, out var data))
        {
            hp = data.BaseHP;
            atk = data.BaseATK;
            def = data.BaseDEF;
        }

        return new SimUnit
        {
            Id = id,
            Team = team,
            Kind = kind,
            Type = Type.Unit,
            HP = hp,
            MaxHP = hp,
            ATK = atk,
            DEF = def,
            Position = pos,
            Direction = team == Team.Enemy ? Direction.S : Direction.N,
            IsBoss = false,
            AssignedSkillId = -1,
            SkillCooldown = 0,
            Fatigue = 0,
            ShieldTurns = 0,
            ShieldActivated = false,
            Passive = PassiveSkill.None,
        };
    }

    // ================================================================
    //  ゲーム終了判定
    // ================================================================
    public bool IsTerminal()
    {
        var ec = GetCrystal(Team.Enemy);
        var pc = GetCrystal(Team.Player);
        if (ec != null && !ec.IsAlive) return true;
        if (pc != null && !pc.IsAlive) return true;
        // King死亡もゲーム終了
        var ek = GetKing(Team.Enemy);
        var pk = GetKing(Team.Player);
        if (ek != null && !ek.IsAlive) return true;
        if (pk != null && !pk.IsAlive) return true;
        return false;
    }

    // ================================================================
    //  ユーティリティ — GridHelper に委譲
    // ================================================================
    /// <summary>ワールド座標をXZ平面セル座標に変換（GridHelper.ToGridXZ のエイリアス）</summary>
    public static Vector3Int ToCell(Vector3 v) => GridHelper.ToGridXZ(v);

    public int GetAP(Team team) => team == Team.Enemy ? EnemyAP : PlayerAP;

    public int NextUnitId()
    {
        int maxId = 0;
        for (int i = 0; i < Units.Count; i++)
            if (Units[i].Id >= maxId) maxId = Units[i].Id + 1;
        return maxId;
    }
}
