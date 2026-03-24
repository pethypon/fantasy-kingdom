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

    private static readonly Color DamageColor = new Color(1f, 0.25f, 0.2f);
    private static readonly Color HealColor = new Color(0.3f, 1f, 0.4f);
    private static readonly Color ShieldColor = new Color(0.5f, 0.8f, 1f);
    private static readonly Color KillColor = new Color(1f, 0.9f, 0.1f);

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
        Instance.Spawn(worldPos, "MISS", new Color(0.6f, 0.6f, 0.6f));
    }

    // ================================================================
    //  内部処理
    // ================================================================

    private void Spawn(Vector3 worldPos, string text, Color color)
    {
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
