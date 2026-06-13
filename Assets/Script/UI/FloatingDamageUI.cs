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
            Destroy(gameObject);
            return;
        }
        Instance = this;
        InitPool();
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void InitPool()
    {
        _pool = new DamagePopup[PoolSize];
        for (int i = 0; i < PoolSize; i++)
        {
            var go = new GameObject($"DmgPopup_{i}");
            go.transform.SetParent(transform);

            var tmp = go.AddComponent<TextMeshPro>();
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

    /// <summary>回復ポップアップを表示する</summary>
    public static void ShowHeal(Vector3 worldPos, int amount)
    {
        if (Instance == null) return;
        Instance.Spawn(worldPos, $"+{amount}", HealColor);
    }

    /// <summary>シールドブロック表示</summary>
    public static void ShowShield(Vector3 worldPos)
    {
        if (Instance == null) return;
        Instance.Spawn(worldPos, "SHIELD", ShieldColor);
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

        ref var popup = ref _pool[_nextIndex];

        // 古いのが使用中でも強制リサイクル
        popup.Go.SetActive(true);
        popup.Text.text = text;
        popup.Text.color = color;
        popup.Text.alpha = 1f;
        popup.StartPos = worldPos + Vector3.up * YOffset;
        popup.Go.transform.position = popup.StartPos;
        popup.ExpireTime = Time.time + Duration;

        // ビルボード：カメラの方を向く
        if (Camera.main != null)
            popup.Go.transform.rotation = Camera.main.transform.rotation;

        _nextIndex = (_nextIndex + 1) % PoolSize;
    }

    private void Update()
    {
        float now = Time.time;
        Camera cam = Camera.main;

        for (int i = 0; i < PoolSize; i++)
        {
            ref var popup = ref _pool[i];
            if (!popup.Go.activeSelf) continue;

            if (now >= popup.ExpireTime)
            {
                popup.Go.SetActive(false);
                continue;
            }

            // 上昇 + フェードアウト
            float elapsed = Duration - (popup.ExpireTime - now);
            float t = elapsed / Duration;

            Vector3 pos = popup.StartPos + Vector3.up * (RiseSpeed * elapsed);
            popup.Go.transform.position = pos;
            popup.Text.alpha = 1f - (t * t); // ease-out fade

            // ビルボード
            if (cam != null)
                popup.Go.transform.rotation = cam.transform.rotation;
        }
    }
}
