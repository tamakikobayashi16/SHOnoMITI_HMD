// ResultSceneController.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class ResultSceneController : MonoBehaviour
{
    [Header("Parent to attach the incoming gallery root under (optional)")]
    public Transform resultGalleryParent;

    [Header("UI")]
    public TextMeshProUGUI promptLabel;

    [Header("Flow")]
    public string backToTitleSceneName = "Title";

    [Header("Keep As-Is")]
    [Tooltip("ギャラリーをそのまま（ワールド変換を維持したまま）表示します")]
    public bool keepGalleryAsIs = true;

    [Header("Placement Control (Runtime)")]
    [Tooltip("シーン開始時に自動でセンタリングを実行します（既定: OFF）")]
    public bool autoRecenterOnStart = false;

    [Tooltip("原点ではなく、任意のワールド座標に中心を合わせる")]
    public bool useCustomWorldCenter = false;

    [Tooltip("この座標に“書いた文字の中心”が来るように動かします")]
    public Vector3 customWorldCenter = Vector3.zero;

    [Tooltip("実行中に左ドラッグで全体を移動できます")]
    public bool enableMouseDrag = true;

    [Tooltip("ドラッグの基準面を親のZにします（XY平面）。オフなら dragPlaneZ を使用")]
    public bool dragOnParentZPlane = true;

    [Tooltip("ドラッグ面のZ（dragOnParentZPlane=false の時に使用）")]
    public float dragPlaneZ = 0f;

    [Header("Nudge (Runtime)")]
    [Tooltip("矢印キーでの移動ステップ")]
    public float moveStep = 0.1f;

    [Tooltip("Shift併用時の微調整ステップ")]
    public float fineStep = 0.02f;

    // --- internal ---
    Transform transferRoot;   // 受け取ったギャラリーのルート
    bool dragging = false;
    Vector3 dragPrevWorld;

    void Start()
    {
        // お題表示
        if (promptLabel != null)
        {
            string word = !string.IsNullOrEmpty(GlobalData.ResultPromptWord) ? GlobalData.ResultPromptWord : GlobalData.PromptWord;
            promptLabel.text = string.IsNullOrEmpty(word) ? "結果" : $"お題：{word}";
        }

        // ギャラリー受け取り：ルートごとぶら下げ、ワールド変換を保持
        if (GlobalData.SceneTransferRoot != null)
        {
            transferRoot = GlobalData.SceneTransferRoot.transform;

            if (resultGalleryParent != null)
                transferRoot.SetParent(resultGalleryParent, true); // ★ ワールド座標そのまま
            else
                transferRoot.SetParent(null, true);                // どこにもぶら下げない（ルートのまま）

            // keep-as-is のため、並べ替えや破棄は一切しない
            // GlobalData.SceneTransferRoot は引き続き保持（必要なら他で参照可能）
        }

        // もし「そのまま」ではなくセンタリングしたい場合のみ実行
        if (autoRecenterOnStart)
        {
            Vector3 target = useCustomWorldCenter ? customWorldCenter : Vector3.zero;
            RecenterAllAtWorld(target);
        }
    }

    void Update()
    {
        if (transferRoot == null && resultGalleryParent == null) return;

        // ドラッグ移動（実行時）
        if (enableMouseDrag)
            HandleMouseDrag();

        // キー操作（実行時）
        float step = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) ? fineStep : moveStep;

        if (Input.GetKeyDown(KeyCode.O)) RecenterAllAtWorld(Vector3.zero);
        if (Input.GetKeyDown(KeyCode.P)) RecenterAllAtWorld(customWorldCenter);

        Vector3 nudge = Vector3.zero;
        if (Input.GetKey(KeyCode.LeftArrow)) nudge += Vector3.left * step;
        if (Input.GetKey(KeyCode.RightArrow)) nudge += Vector3.right * step;
        if (Input.GetKey(KeyCode.UpArrow)) nudge += Vector3.up * step;
        if (Input.GetKey(KeyCode.DownArrow)) nudge += Vector3.down * step;
        if (nudge.sqrMagnitude > 0f) MoveAllChildrenWorld(nudge);
    }

    // UIボタンから戻る
    public void OnClickBackToTitle()
    {
        SceneManager.LoadScene(backToTitleSceneName, LoadSceneMode.Single);
    }

    // ====== 配置のコア処理 ======

    /// <summary>
    /// 「書いた文字（LineRenderer群）を優先」に包絡を取り、
    /// その中心が targetCenter に来るように全体（transferRoot など）を平行移動。
    /// </summary>
    public void RecenterAllAtWorld(Vector3 targetCenter)
    {
        if (!TryComputeTotalBounds(out Bounds b)) return;
        Vector3 delta = targetCenter - b.center;
        MoveAllChildrenWorld(delta);
    }

    /// <summary>
    /// 総合Bounds（書いた線 優先 → だめならRenderer）を取得
    /// </summary>
    public bool TryComputeTotalBounds(out Bounds total)
    {
        Transform root = transferRoot != null ? transferRoot : (resultGalleryParent != null ? resultGalleryParent : null);
        total = new Bounds(Vector3.zero, Vector3.zero);
        if (root == null) return false;

        // 1) LineRenderer を優先
        var lrs = root.GetComponentsInChildren<LineRenderer>(false);
        if (lrs != null && lrs.Length > 0)
        {
            total = new Bounds(lrs[0].bounds.center, Vector3.zero);
            foreach (var lr in lrs) if (lr != null) total.Encapsulate(lr.bounds);
            return true;
        }

        // 2) フォールバック：Renderer（背景も含む）
        var rens = root.GetComponentsInChildren<Renderer>(false);
        if (rens == null || rens.Length == 0) return false;

        total = new Bounds(rens[0].bounds.center, Vector3.zero);
        foreach (var r in rens) if (r != null) total.Encapsulate(r.bounds);
        return true;
    }

    /// <summary>
    /// 親は動かさず、transferRoot があればそれを、無ければ親直下の子を等しくワールド側で平行移動
    /// </summary>
    public void MoveAllChildrenWorld(Vector3 delta)
    {
        if (transferRoot != null)
        {
            transferRoot.position += delta;
            return;
        }

        if (resultGalleryParent != null)
        {
            foreach (Transform child in resultGalleryParent)
            {
                if (child == null) continue;
                child.position += delta;
            }
        }
    }

    // ====== マウスドラッグ（ランタイム用） ======
    void HandleMouseDrag()
    {
        if (Camera.main == null) return;

        // ドラッグ対象面
        float planeZ = dragOnParentZPlane && resultGalleryParent != null
            ? resultGalleryParent.position.z
            : dragPlaneZ;

        Plane plane = new Plane(Vector3.forward, new Vector3(0f, 0f, planeZ));

        if (Input.GetMouseButtonDown(0))
        {
            if (TryRayToPlane(Input.mousePosition, plane, out Vector3 w))
            {
                dragging = true;
                dragPrevWorld = w;
            }
        }
        else if (Input.GetMouseButton(0) && dragging)
        {
            if (TryRayToPlane(Input.mousePosition, plane, out Vector3 w))
            {
                Vector3 delta = w - dragPrevWorld;
                if (delta.sqrMagnitude > 0f)
                {
                    MoveAllChildrenWorld(delta);
                    dragPrevWorld = w;
                }
            }
        }
        else if (Input.GetMouseButtonUp(0))
        {
            dragging = false;
        }
    }

    bool TryRayToPlane(Vector3 screenPos, Plane plane, out Vector3 world)
    {
        var ray = Camera.main.ScreenPointToRay(screenPos);
        if (plane.Raycast(ray, out float enter))
        {
            world = ray.GetPoint(enter);
            return true;
        }
        world = Vector3.zero;
        return false;
    }

#if UNITY_EDITOR
    // ========== エディタ表示の補助（シーンビューに境界を可視化） ==========
    [Header("Editor Gizmo")]
    public Color gizmoColor = new Color(0, 1, 1, 0.75f); // シアン

    void OnDrawGizmosSelected()
    {
        if (!TryComputeTotalBounds(out Bounds b)) return;
        Gizmos.color = gizmoColor;
        Gizmos.DrawWireCube(b.center, b.size);
        Gizmos.DrawSphere(b.center, Mathf.Max(0.01f, b.size.magnitude * 0.01f));
    }
#endif
}
