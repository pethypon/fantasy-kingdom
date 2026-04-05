using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

public class UnitClick : MonoBehaviour
{
    [SerializeField] private PlayerMove playermove;
    [FormerlySerializedAs("turngenerater")]
    [SerializeField] private TurnGenerator turnGenerator;
    [SerializeField] private PlayerAttack playerattack;
    [SerializeField] private BattleSystem battlesystem;
    [FormerlySerializedAs("attackpoint")]
    [SerializeField] private AttackGenerator attackGenerator;
    public Status ATKC;

    public RaycastHit attackhit;
    private const float RayDistance = GameConstants.DefaultRayDistance;
    private const float MovePointRayDistance = GameConstants.MovePointRayDistance;
    private int MovePointLayerMask;

    private void Awake()
    {
        MovePointLayerMask = 1 << LayerMask.NameToLayer("MovePoint");
    }

    public void UC(PlayerMove playermove, TurnGenerator turnGenerator, AttackGenerator attackGenerator)
    {
        this.playermove = playermove;
        this.turnGenerator = turnGenerator;
        this.attackGenerator = attackGenerator;
    }

    // ---- 左クリック：プレイヤーユニットまたは建築物選択 ----
    public void Click1()
    {
        if (playermove != null && playermove.BuildMode) return;
        if (!TryGetMouseRay(out Ray ray)) return;
        if (!Physics.Raycast(ray, out playermove.hit, RayDistance)) return;

        playermove.Obj = playermove.hit.transform.GetComponent<Status>();
        playermove.ObjP = playermove.hit.transform.position;

        if (playermove.Obj == null) return;

        // 敵ユニット/建築物をクリック → 情報パネルのみ表示（操作不可）
        if (playermove.Obj.team == Team.Enemy)
        {
            if (turnGenerator.Systems.UnitPanelUI != null)
                turnGenerator.Systems.UnitPanelUI.Show(playermove.Obj);
            playermove.Obj = null; // 選択状態にはしない
            return;
        }

        if (playermove.Obj.team != Team.Player) return;

        // 建築物・壁クリック（移動ポイントは生成しない）
        if (playermove.Obj.type == Type.Building || playermove.Obj.type == Type.Wall)
        {
            turnGenerator.Context.SelectUnit = playermove.Obj;
            if (turnGenerator.Systems.UnitPanelUI != null)
                turnGenerator.Systems.UnitPanelUI.Show(playermove.Obj);
            Debug.Log("<color=#00ff00ff>[Controller]</color> 建築物選択");
            return;
        }

        if (playermove.Obj.type != Type.Unit) return;

        // スタン中のユニットは選択不可（行動不可）
        if (StatusEffectSystem.IsStunned(playermove.Obj))
        {
            ToastMessageUI.Show("スタン中のため行動できません", ToastMessageUI.MessageType.Warning);
            return;
        }

        turnGenerator.Systems.MoveGenerator.MoveCore(playermove.Obj, playermove.ObjP);
        turnGenerator.Context.SelectUnit = playermove.Obj;
        turnGenerator.Context.OldCell = turnGenerator.Context.SelectUnit.transform.position;
        playermove.MenuSwitch = true;

        if (turnGenerator.Systems.UnitPanelUI != null)
            turnGenerator.Systems.UnitPanelUI.Show(playermove.Obj);

        // ユニット選択成功
    }

    // ---- 左クリック（移動確定 or 再選択） ----
    public void Click2()
    {
        if (playermove != null && playermove.BuildMode) return;
        // Click2処理
        if (!TryGetMouseRay(out Ray ray)) return;

        // MovePoint レイヤーのみ検出（他オブジェクトは貫通、一定距離で打ち切り）
        if (Physics.Raycast(ray, out playermove.hit, MovePointRayDistance, MovePointLayerMask))
        {
            // MovePointレイヤーにヒット
            HandleMovePointClick();
            return;
        }

        // MovePoint に当たらなかった場合、全レイヤーでユニット再選択を試みる
        if (!Physics.Raycast(ray, out playermove.hit, RayDistance)) return;

        playermove.MP = playermove.hit.transform.GetComponent<Status>();
        if (playermove.MP == null) return;

        if (playermove.MP.team == Team.Player && playermove.MP.type == Type.Unit)
        {
            HandlePlayerUnitReselect();
        }
    }

    // ---- 攻撃クリック ----
    public void AttackClick(
        BattleSystem battlesystem,
        PlayerAttack playerattack,
        AttackGenerator attackGenerator,
        PlayerMove playermove)
    {
        this.playermove = playermove;
        this.battlesystem = battlesystem;
        this.playerattack = playerattack;
        this.attackGenerator = attackGenerator;

        if (!TryGetMouseRay(out Ray ray)) return;
        if (!Physics.Raycast(ray, out attackhit, RayDistance)) return;
        if (attackGenerator.AttackP == null) return;

        // スキルモード判定
        bool isSkillMode = playerattack.attackmode == PlayerMove.AttackMode.Skill
            && playermove.Obj != null
            && playermove.Obj.AssignedSkillId >= 0
            && SkillData.Table.ContainsKey(playermove.Obj.AssignedSkillId)
            && turnGenerator.Systems.SkillSystem != null;

        if (isSkillMode)
        {
            HandleSkillClick(attackhit);
        }
        else
        {
            HandleNormalAttackClick(attackhit);
        }
    }

    // ---- 通常攻撃クリック処理 ----
    private void HandleNormalAttackClick(RaycastHit hit)
    {
        if (!hit.transform.TryGetComponent<Status>(out ATKC)) return;
        if (ATKC.team != Team.Enemy || ATKC.type != Type.Unit) return;

        Vector3 attackSame = ATKC.transform.position;
        bool isInRange = IsInAttackRange(attackGenerator.AttackP, attackSame);
        if (!isInRange) return;

        if (!turnGenerator.Systems.APSystem.CanAct(Team.Player, APSystem.ActionType.Attack, playermove.Obj))
        {
            ToastMessageUI.Show("AP不足：攻撃できません", ToastMessageUI.MessageType.Warning);
            return;
        }

        battlesystem.target = ATKC;
        playermove.MenuSwitch = false;
        int hpBefore = ATKC.HP;
        battlesystem.DamageGenerater(turnGenerator);
        int dmgDealt = hpBefore - ATKC.HP;
        ActionLogUI.LogAttack(KindNameJP.Get(playermove.Obj.kind), KindNameJP.Get(ATKC.kind), dmgDealt, ATKC.HP <= 0);
        turnGenerator.Systems.APSystem.Consume(Team.Player, APSystem.ActionType.Attack, playermove.Obj);
        playerattack.AttackSuccess = true;
    }

    // ---- スキル攻撃クリック処理 ----
    private void HandleSkillClick(RaycastHit hit)
    {
        SkillData skill = SkillData.Table[playermove.Obj.AssignedSkillId];

        // APチェック
        if (!turnGenerator.Systems.APSystem.CanUseSkill(Team.Player, skill.APCost))
        {
            ToastMessageUI.Show("AP不足：スキルを使用できません", ToastMessageUI.MessageType.Warning);
            return;
        }

        // クリック対象の Status を取得
        Status clickTarget = hit.transform.GetComponent<Status>();
        Vector3 clickPos = hit.transform.position;

        // 攻撃ポイント範囲内チェック
        bool isInRange = IsInAttackRangeRounded(attackGenerator.AttackP, clickPos);
        if (!isInRange) return;

        switch (skill.Target)
        {
            case SkillTarget.EnemySingle:
                if (clickTarget == null || clickTarget.team != Team.Enemy || clickTarget.type != Type.Unit) return;
                ATKC = clickTarget;
                turnGenerator.Systems.SkillSystem.ExecuteSkill(playermove.Obj, ATKC, skill);
                break;

            case SkillTarget.EnemyOrBuilding:
                if (clickTarget == null) return;
                if (clickTarget.team != Team.Enemy) return;
                if (clickTarget.type != Type.Unit && clickTarget.type != Type.Building) return;
                ATKC = clickTarget;
                turnGenerator.Systems.SkillSystem.ExecuteSkill(playermove.Obj, ATKC, skill);
                break;

            case SkillTarget.AllySingle:
                if (clickTarget == null || clickTarget.team != Team.Player || clickTarget.type != Type.Unit) return;
                ATKC = clickTarget;
                turnGenerator.Systems.SkillSystem.ExecuteSkill(playermove.Obj, ATKC, skill);
                break;

            case SkillTarget.LowHPEnemy:
                if (clickTarget == null || clickTarget.team != Team.Enemy || clickTarget.type != Type.Unit) return;
                if (clickTarget.MaxHP <= 0 || (float)clickTarget.HP / clickTarget.MaxHP > 0.5f)
                {
                    ToastMessageUI.Show("HP50%以下の敵のみ対象です", ToastMessageUI.MessageType.Warning);
                    return;
                }
                ATKC = clickTarget;
                turnGenerator.Systems.SkillSystem.ExecuteSkill(playermove.Obj, ATKC, skill);
                break;

            case SkillTarget.FlyingEnemy:
                if (clickTarget == null || clickTarget.team != Team.Enemy || clickTarget.type != Type.Unit) return;
                ATKC = clickTarget;
                turnGenerator.Systems.SkillSystem.ExecuteSkill(playermove.Obj, ATKC, skill);
                break;

            case SkillTarget.DesignatedTile:
                // クリック位置を中心に範囲内の敵を収集して範囲攻撃
                {
                    Vector3Int center = new Vector3Int(
                        Mathf.RoundToInt(clickPos.x), 0, Mathf.RoundToInt(clickPos.z));
                    List<Status> targets = FindEnemiesInSkillArea(skill, center, playermove.Obj.direction);
                    turnGenerator.Systems.SkillSystem.ExecuteAreaSkill(playermove.Obj, skill, targets);
                }
                break;

            case SkillTarget.AdjacentCenter:
                // クリック位置を中心に範囲内の敵を収集
                {
                    Vector3Int center = new Vector3Int(
                        Mathf.RoundToInt(clickPos.x), 0, Mathf.RoundToInt(clickPos.z));
                    List<Status> targets = FindEnemiesInSkillArea(skill, center, playermove.Obj.direction);
                    turnGenerator.Systems.SkillSystem.ExecuteAreaSkill(playermove.Obj, skill, targets);
                }
                break;

            case SkillTarget.DirectionLine:
            case SkillTarget.DesignatedRow:
                // ライン攻撃: 攻撃者の位置を基準にライン上の敵を収集
                {
                    Vector3Int lineOrigin = new Vector3Int(
                        Mathf.RoundToInt(playermove.ObjP.x), 0, Mathf.RoundToInt(playermove.ObjP.z));
                    List<Status> targets = FindEnemiesInSkillArea(skill, lineOrigin, playermove.Obj.direction);
                    turnGenerator.Systems.SkillSystem.ExecuteAreaSkill(playermove.Obj, skill, targets);
                }
                break;

            default:
                if (clickTarget == null || clickTarget.team != Team.Enemy) return;
                ATKC = clickTarget;
                turnGenerator.Systems.SkillSystem.ExecuteSkill(playermove.Obj, ATKC, skill);
                break;
        }

        playermove.MenuSwitch = false;
        turnGenerator.Systems.APSystem.ConsumeSkill(Team.Player, skill.APCost, playermove.Obj);
        playerattack.AttackSuccess = true;

        // ---- ML観測: プレイヤーのスキル使用をMLシステムに記録 ----
        if (turnGenerator.Systems.AICommander != null)
        {
            turnGenerator.Systems.AICommander.MLIntegration.ObservePlayerSkill(
                playermove.Obj, turnGenerator.Context.Turn);
        }
    }

    /// <summary>
    /// 指定スキルの範囲内にいる敵ユニット/建物を収集する。
    /// エリア攻撃・ライン攻撃で共用（重複排除済み）。
    /// </summary>
    private List<Status> FindEnemiesInSkillArea(SkillData skill, Vector3Int origin, Direction dir)
    {
        var positions = SkillSystem.GetAreaPositions(skill.Area, origin, dir);
        var posSet = new HashSet<Vector3Int>(positions);
        var targets = new List<Status>();

        Transform enemyParent = turnGenerator.Systems.UnitSetting.EnemyUnit;
        if (enemyParent == null) return targets;

        foreach (Status s in enemyParent.GetComponentsInChildren<Status>())
        {
            if (!s.gameObject.activeSelf) continue;
            if (s.type != Type.Unit && s.type != Type.Building) continue;
            Vector3Int cell = new Vector3Int(
                Mathf.RoundToInt(s.transform.position.x), 0,
                Mathf.RoundToInt(s.transform.position.z));
            if (posSet.Contains(cell))
                targets.Add(s);
        }
        return targets;
    }

    // ---- 移動先(MovePoint)クリック時の処理 ----
    private void HandleMovePointClick()
    {
        // 移動先クリック確定

        // 凍結・束縛中は移動不可
        if (playermove.Obj != null && StatusEffectSystem.IsMovementBlocked(playermove.Obj))
        {
            ToastMessageUI.Show("凍結/束縛中のため移動できません", ToastMessageUI.MessageType.Warning);
            return;
        }

        Vector3 from = turnGenerator.Context.OldCell;           // 移動元（選択時に記録済み）
        Vector3 to = playermove.hit.transform.position;
        to.y += GameConstants.MovePointYOffset;           // MovePoint の Y オフセットを戻す

        // ---- APチェック ----
        if (!turnGenerator.Systems.APSystem.CanAct(Team.Player, APSystem.ActionType.Move, playermove.Obj, from, to))
        {
            ToastMessageUI.Show("AP不足：移動できません", ToastMessageUI.MessageType.Warning);
            return;
        }

        // ---- 移動確定 ----
        Status movedUnit = turnGenerator.Context.SelectUnit;

        // ---- Undo用に移動コストを事前計算・記録 ----
        int moveCost = turnGenerator.Systems.APSystem.CalcCost(APSystem.ActionType.Move, playermove.Obj, from, to);
        if (turnGenerator.Systems.MoveUndoSystem != null)
            turnGenerator.Systems.MoveUndoSystem.Record(movedUnit, from, to, moveCost);

        turnGenerator.Context.SelectUnit.transform.position = to;
        turnGenerator.Context.NewCell = turnGenerator.Context.SelectUnit.transform.position;
        turnGenerator.Systems.MoveGenerator.MoveUpdate(turnGenerator.Context.OldCell, turnGenerator.Context.NewCell);
        turnGenerator.Systems.MoveGenerator.MoveReset();

        // Special Ability: 移動フラグセット（迅速体勢・集中維持・鉄壁歩法で使用）
        movedUnit.HasMovedThisTurn = true;

        // ---- AP消費（移動コスト） ----
        turnGenerator.Systems.APSystem.Consume(Team.Player, APSystem.ActionType.Move, playermove.Obj, from, to);

        // ---- アクションログ ----
        ActionLogUI.LogMove(KindNameJP.Get(movedUnit.kind), from, to);

        // ---- ML観測: プレイヤーの移動をMLシステムに記録 ----
        if (turnGenerator.Systems.AICommander != null)
        {
            var ml = turnGenerator.Systems.AICommander.MLIntegration;
            Vector3 ecPos = turnGenerator.Systems.CrystalSystem.ECP;
            ml.ObservePlayerMove(movedUnit, from, to, ecPos, turnGenerator.Context.Turn);
        }

        // 移動後もユニットを選択状態に保持（攻撃など連続行動を可能に）
        turnGenerator.Context.OldCell = to;
        playermove.MP = null;
        playermove.ObjP = to;
        playermove.MenuSwitch = true;

        // 移動後の位置で移動範囲を再表示
        turnGenerator.Systems.MoveGenerator.MoveCore(movedUnit, to);

        if (turnGenerator.Systems.UnitPanelUI != null)
            turnGenerator.Systems.UnitPanelUI.Refresh();
    }

    // ---- プレイヤーユニット再選択時の処理 ----
    private void HandlePlayerUnitReselect()
    {
        turnGenerator.Systems.MoveGenerator.MoveReset();
        playermove.Obj = playermove.hit.transform.GetComponent<Status>();
        playermove.ObjP = playermove.hit.transform.position;
        turnGenerator.Systems.MoveGenerator.MoveCore(playermove.Obj, playermove.ObjP);
        turnGenerator.Context.SelectUnit = playermove.Obj;
        turnGenerator.Systems.MoveGenerator.UnitPointData.Remove(
            turnGenerator.Systems.MoveGenerator.Cell(turnGenerator.Context.OldCell));
        turnGenerator.Context.OldCell = turnGenerator.Context.SelectUnit.transform.position;

        if (turnGenerator.Systems.UnitPanelUI != null)
            turnGenerator.Systems.UnitPanelUI.Show(playermove.Obj);

        // ユニット選択成功
    }

    // ---- LINQ排除: 攻撃範囲内判定 ----
    private static bool IsInAttackRange(List<Vector3> attackP, Vector3 pos)
    {
        if (attackP == null) return false;
        for (int i = 0, count = attackP.Count; i < count; i++)
        {
            if (attackP[i].x == pos.x && attackP[i].z == pos.z)
                return true;
        }
        return false;
    }

    private static bool IsInAttackRangeRounded(List<Vector3> attackP, Vector3 pos)
    {
        if (attackP == null) return false;
        int px = Mathf.RoundToInt(pos.x);
        int pz = Mathf.RoundToInt(pos.z);
        for (int i = 0, count = attackP.Count; i < count; i++)
        {
            if (Mathf.RoundToInt(attackP[i].x) == px && Mathf.RoundToInt(attackP[i].z) == pz)
                return true;
        }
        return false;
    }

    // ---- マウス位置からRayを生成（新入力システム対応） ----
    private bool TryGetMouseRay(out Ray ray)
    {
        ray = default;
        if (Mouse.current == null) return false;
        Vector2 mousePos = Mouse.current.position.ReadValue();
        ray = Camera.main.ScreenPointToRay(mousePos);
        return true;
    }
}
