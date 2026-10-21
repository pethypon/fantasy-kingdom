using UnityEngine;
using TMPro;

/// <summary>
/// 攻撃ヒット時にターゲット上にフローティングダメージ数値を表示する。
/// BattleSystem / SkillSystem から静的メソッドで呼び出す。
/// </summary>
public class FloatingDamageUI : MonoBehaviour
{
    public static FloatingDamageUI Instance { get; private set; }

    // ---- プール管理 ----
    private const int PoolSize = 10;
    private DamagePopup[] _pool;
    private int _nextIndex;

    private struct DamagePopup
    {
        public GameObject Go;
        public TextMeshPro Text;
        public float ExpireTime;
        public float Lifetime;
        public Vector3 StartPos;
    }

    // ---- 設定 ----
    private const float Duration = 1.2f;
    private const float RiseSpeed = 1.5f;
    private const float FontSize = 5f;
    private const float YOffset = 1.2f;

    private static readonly Color DamageColor = BrandGuide.FeedbackDamage;
    private static readonly Color HealColor = BrandGuide.FeedbackHeal;
    private static readonly Color ShieldColor = BrandGuide.FeedbackShield;
    private static readonly Color KillColor = BrandGuide.FeedbackKill;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            enabled = false;
            Destroy(this);
            return;
        }
        Instance = this;
        InitPool();
        enabled = false; // No per-frame camera lookup when no popup is visible.
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void InitPool()
    {
        _nextIndex = 0;
        _pool = new DamagePopup[PoolSize];
        var popupFont = UIFactory.LoadDefaultFont();
        for (int i = 0; i < PoolSize; i++)
        {
            var existing = transform.Find($"DmgPopup_{i}");
            var go = existing != null ? existing.gameObject : new GameObject($"DmgPopup_{i}");
            go.transform.SetParent(transform);

            var tmp = go.GetComponent<TextMeshPro>();
            if (tmp == null) tmp = go.AddComponent<TextMeshPro>();
            if (popupFont != null) tmp.font = popupFont;
            tmp.fontSize = FontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.sortingOrder = 100;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;

            go.SetActive(false);
            _pool[i] = new DamagePopup
            {
                Go = go,
                Text = tmp,
                ExpireTime = 0f,
                StartPos = Vector3.zero
            };
        }
    }

    // ================================================================
    //  公開API
    // ================================================================

    /// <summary>ダメージポップアップを表示する</summary>
    public static void ShowDamage(Vector3 worldPos, int damage, bool isKill = false)
    {
        if (Instance == null) return;
        Color c = isKill ? KillColor : DamageColor;
        string text = isKill ? $"{damage}\nKILL!" : damage.ToString();
        Instance.Spawn(worldPos, text, c);
    }

    /// <summary>一つの確定した命中結果を、HP損失・バリア損失に分けて表示する。</summary>
    public static void ShowCombatDamage(Status actor, int hpDamage, int barrierDamage, bool isKill = false)
    {
        if (Instance == null || actor == null) return;
        hpDamage = Mathf.Max(0, hpDamage); barrierDamage = Mathf.Max(0, barrierDamage);
        if (hpDamage == 0 && barrierDamage == 0) return;
        string text = barrierDamage > 0 ? $"<color=#80D1FF>バリア -{barrierDamage}</color>" : "";
        if (hpDamage > 0)
            text += (text.Length > 0 ? "\n" : "") + $"<color=#FF7065>HP -{hpDamage}</color>";
        if (barrierDamage > 0 && actor.Barrier == 0)
            text += "\n<color=#FFD36B>バリア破壊</color>";
        if (isKill) text += "\n撃破";
        Instance.Spawn(actor.transform.position, text, isKill ? KillColor : hpDamage > 0 ? DamageColor : ShieldColor);
    }

    /// <summary>侍などの固有能力で現在バリアだけを解除した時の表示。</summary>
    public static void ShowBarrierBreak(Status actor)
    {
        if (Instance == null || actor == null) return;
        Instance.Spawn(actor.transform.position, "バリア破壊", new Color(1, .83f, .42f));
    }

    /// <summary>HP回復と区別し、実際に回復したバリア量を表示する。</summary>
    public static void ShowBarrierRecovery(Status actor, int amount)
    {
        if (Instance == null || actor == null || amount <= 0) return;
        Instance.Spawn(actor.transform.position, $"バリア +{amount}", new Color(.35f, 1, .95f));
    }

    /// <summary>回復ポップアップを表示する</summary>
    public static void ShowHeal(Vector3 worldPos, int amount)
    {
        if (Instance == null) return;
        Instance.Spawn(worldPos, $"HP +{amount}", HealColor);
    }

    /// <summary>シールドブロック表示</summary>
    public static void ShowShield(Vector3 worldPos)
    {
        if (Instance == null) return;
        Instance.Spawn(worldPos, "完全防御", ShieldColor);
    }

    /// <summary>ミス・無効表示</summary>
    public static void ShowMiss(Vector3 worldPos)
    {
        if (Instance == null) return;
        Instance.Spawn(worldPos, "MISS", BrandGuide.FeedbackMiss);
    }

    // ================================================================
    //  内部処理
    // ================================================================

    /// <summary>
    /// プレイヤー視界内にこのワールド座標があるか。視界外の演出を抑制し
    /// フォグオブウォーの情報漏洩を防ぐ。VisionGenerator は遅延キャッシュ。
    /// </summary>
    private static VisionGenerator _cachedVisionGen;
    private static bool IsPositionVisibleToPlayer(Vector3 worldPos)
    {
        if (_cachedVisionGen == null)
            _cachedVisionGen = Object.FindFirstObjectByType<VisionGenerator>();
        if (_cachedVisionGen == null) return true; // 未起動時は表示する
        var cell = GridHelper.ToGridXZ(worldPos);
        return _cachedVisionGen.IsInVision(Team.Player, cell);
    }

    private void Spawn(Vector3 worldPos, string text, Color color)
    {
        // 視界外の位置で発生した演出は抑制（フォグオブウォーの整合性）
        if (!IsPositionVisibleToPlayer(worldPos)) return;

        ActionLogUI.Present(new AIPresentationEvent { Kind = color == KillColor ? AIPresentationKind.Kill : AIPresentationKind.Damage,
            Popup = true, Position = worldPos, Text = text, Color = color });
    }

    /// <summary>Presentation sink only. Damage and learning have already completed before this is called.</summary>
    public static void Deliver(Vector3 worldPos, string text, Color color, float durationMultiplier)
    { if (Instance != null) Instance.SpawnDirect(worldPos, text, color, durationMultiplier); }

    private void SpawnDirect(Vector3 worldPos, string text, Color color, float durationMultiplier)
    {

        enabled = true;
        if (_pool == null || _pool[_nextIndex].Go == null || _pool[_nextIndex].Text == null) InitPool();
        ref var popup = ref _pool[_nextIndex];

        // 古いのが使用中でも強制リサイクル
        popup.Go.SetActive(true);
        popup.Text.text = text;
        popup.Text.color = color;
        popup.Text.alpha = 1f;
        popup.StartPos = worldPos + Vector3.up * YOffset;
        popup.Go.transform.position = popup.StartPos;
        popup.Lifetime = Duration * Mathf.Clamp(durationMultiplier, .01f, 1f);
        popup.ExpireTime = Time.time + popup.Lifetime;

        // ビルボード：カメラの方を向く
        if (Camera.main != null)
            popup.Go.transform.rotation = Camera.main.transform.rotation;

        _nextIndex = (_nextIndex + 1) % PoolSize;
    }

    private void Update()
    {
        if (_pool == null) { InitPool(); return; }
        float now = Time.time;
        Camera cam = Camera.main;
        bool anyActive = false;

        for (int i = 0; i < PoolSize; i++)
        {
            ref var popup = ref _pool[i];
            if (popup.Go == null || popup.Text == null || !popup.Go.activeSelf) continue;

            if (now >= popup.ExpireTime)
            {
                popup.Go.SetActive(false);
                continue;
            }

            // 上昇 + フェードアウト
            anyActive = true;
            float elapsed = popup.Lifetime - (popup.ExpireTime - now);
            float t = elapsed / popup.Lifetime;

            Vector3 pos = popup.StartPos + Vector3.up * (RiseSpeed * elapsed);
            popup.Go.transform.position = pos;
            popup.Text.alpha = 1f - (t * t); // ease-out fade

            // ビルボード
            if (cam != null)
                popup.Go.transform.rotation = cam.transform.rotation;
        }
        if (!anyActive) enabled = false;
    }
}
