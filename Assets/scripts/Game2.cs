using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

// ★ 追加：あいまいさ回避用エイリアス
using URandom = UnityEngine.Random;
using UObject = UnityEngine.Object;

public class Game2 : MonoBehaviour
{
    // ★ 状態
    enum Status { INIT, CONNECTING, CONNECTED, WAIT_FOR_STAND, CALIBRATING, COUNTDOWN, READY_TO_WRITE, FINISHED }
    Status status = Status.INIT;

    RecvBB recv_bb;

    float[] offsets = new float[4];
    List<RecvBB.BBDatum> bbdata = new List<RecvBB.BBDatum>();

    TextMeshProUGUI text;
    CoGLine cogline;

    DrawTrail trailDrawer;

    public GameObject movableObject;

    [Header("Write Area (MoveArea)")]
    public GameObject moveArea;

    public GameObject paperObject;

    [Header("Prompt / Right-side Backgrounds")]
    public List<string> sampleWords = new List<string>() {
        "もも","なし","かき","うめ","ゆず","くり","びわ","きく"
    };
    public Transform rightPanelParent;
    public GameObject backgroundPrefab;
    public TMP_FontAsset brushFont;
    public float rightSlotSpacing = 2.5f;

    public GameObject galleryArea;
    public Transform galleryParent;
    public Vector3 galleryStartLocalPos = new Vector3(-6f, 1f, 0f);
    public float gallerySpacingY = 2.5f;
    public float galleryThumbnailScale = 0.35f;
    public int maxCharsPerGroup = 4;

    [Tooltip("中心から直径3cm以内（半径約1.5cm相当）のブレは無視するしきい値")]
    public float deadZone = 0.1f;
    public float moveSpeed = 1.0f;

    [Range(0.01f, 1f)]
    [Tooltip("下方向へのバイアス量。正にすると、多少上半分が重くても下に動きやすくなります（目安 0〜0.3）。")]
    public float cogForwardOffset = 1.0f;

    public float emaAlpha = 0.2f;
    public int requiredFramesToMove = 3;
    public float weightThresholdKg = 3.0f;
    public bool clampToViewport = true;

    public float presenceThresholdKg = 5.0f;
    public float calibrationDuration = 10.0f;

    [Range(0.01f, 1f)]
    public float pressureEmaAlpha = 0.12f;

    Vector3 initialPosition;
    bool initialPositionSet = false;
    Vector2 emaCog = Vector2.zero;
    bool emaInitialized = false;
    int aboveThresholdFrames = 0;
    Vector3 currentVelocity = Vector3.zero;
    bool isMoving = false;
    Vector3 lockedPosition;
    float lastMeasuredWeightKg = -1f;
    bool warnedNoWeightInfo = false;

    float baselineWeightKg = 0f;
    bool baselineSet = false;

    float calibrationTimer = 0f;
    float calibrationCountdown = 0f;
    float calibrationSum = 0f;
    int calibrationCount = 0;

    float emaPressureKg = 0f;
    bool emaPressureInitialized = false;

    private List<GameObject> savedGroups = new List<GameObject>();
    private int savedCount = 0;
    private int gallerySelectedIndex = -1;

    // 右側の背景スロット
    private List<GameObject> rightSlots = new List<GameObject>();
    private int activeSlotIndex = 0;

    private GameObject tempContainer;

    private Coroutine messageCoroutine;

    public List<RecvBB.BBDatum> BBData => bbdata;
    public float[] Offsets => offsets;

    // --- お題と表示用（書く場所に出す“sample text”） ---
    string currentPromptWord = null;
    int currentPromptIndex = 0;

    [Header("Ghost (Sample Text Prefab)")]
    [Tooltip("ゴーストではなく、表示用の 'sample text' プレハブ（TextMeshPro を含む 3D オブジェクト）")]
    public GameObject sampleTextPrefab;

    GameObject ghostGO;     // 実体は sample text プレハブ
    TMP_Text ghostTMP;      // プレハブ内の TMP_Text
    Vector3 ghostBaseScale = Vector3.one;

    // 果物プール（フォールバック用）
    private static readonly string[] FRUIT_POOL = new string[] {
        "もも","なし","かき","うめ","ゆず","くり","びわ","きく"
    };

    // ===== 旧方式フォールバック用 =====
    class GlyphData
    {
        public string name;
        public List<Vector3[]> lrWorldPts = new List<Vector3[]>();
        public List<float> lrWidths = new List<float>();
        public List<float> lrEndWidths = new List<float>();
        public List<AnimationCurve> lrWidthCurves = new List<AnimationCurve>();
        public List<float> lrWidthMultipliers = new List<float>();
        public List<Gradient> lrColorGradients = new List<Gradient>();
        public List<Color> lrStartColors = new List<Color>();
        public List<Color> lrEndColors = new List<Color>();
        public List<Material> lrMaterials = new List<Material>();
        public List<LineTextureMode> lrTextureModes = new List<LineTextureMode>();
        public List<LineAlignment> lrAlignments = new List<LineAlignment>();
        public List<int> lrNumCapVertices = new List<int>();
        public List<int> lrNumCornerVertices = new List<int>();
        public List<bool> lrUseWorldSpace = new List<bool>();
        public List<int> lrSortingLayerIDs = new List<int>();
        public List<int> lrSortingOrders = new List<int>();
    }

    // ==== 表示対策（ビルドのにじみ回避） ====
    [Header("Gallery Display Fix")]
    [Tooltip("ギャラリーの線を背景より手前に出すための微小オフセット（メートル）")]
    public float galleryStrokeZOffset = 0.002f;
    [Tooltip("ギャラリーの LineRenderer のソート順に加算する値")]
    public int gallerySortingOrderBoost = 100;

    // ==== ギャラリーのスケール ====
    [Header("Gallery Scale")]
    [Tooltip("ギャラリーに表示する際の等倍縮小係数（内容物に適用）")]
    public float gallerySavedScale = 0.35f;

    // ==== 描画頻度（フレーム内サブステップ）====
    [Header("Drawing Frequency")]
    [Tooltip("1フレームの中で UpdateStroke を呼ぶ回数。3にすると約3倍の頂点密度になります。")]
    [Range(1, 8)] public int drawSubSteps = 3;

    // ==== 新方式（オブジェクト保存） ====
    [Header("Gallery Save Mode")]
    [Tooltip("見た目のみ再構築ではなく、書いたオブジェクト自体を保存する（推奨）")]
    public bool saveGlyphAsObject = true;

    [Tooltip("ギャラリー縮小時、線幅もスケールに合わせて縮小する")]
    public bool scaleLineWidthInGallery = true;

    // ==== 完了時アニメーション ====
    [Header("Completion Animations")]
    [Tooltip("最後の一文字保存後に再生する（未指定なら自動取得を試みます）")]
    public Animator galleryAnimator;
    public string galleryAnimTrigger = "Play";
    public string galleryAnimStateName = "";

    public Animator rightPanelAnimator;
    public string rightPanelAnimTrigger = "Play";
    public string rightPanelAnimStateName = "";

    public Animator moveAreaAnimator;
    public string moveAreaAnimTrigger = "Play";
    public string moveAreaAnimStateName = "";

    // ==== 追加：複数の結果シーン名（インスペクターで変更可） ====
    [Header("Scene Navigation")]
    [SerializeField] private string goldResultSceneName = "GoldResult";
    [SerializeField] private string silverResultSceneName = "SilverResult";
    [SerializeField] private string bronzeResultSceneName = "BronzeResult";
    [SerializeField] private string worstResultSceneName = "WorstResult";

    [Header("BB Connection Watchdog")]
    public float bbReconnectIfNoDataSec = 2.0f;
    public float bbReconnectCooldownSec = 1.0f;

    float bbNoDataTimer = 0f;
    float bbReconnectCooldown = 0f;

    // ===== Wiiリモコン入力 =====
    [Header("Wii Remote (via RecvBB)")]
    public bool enableWiiRemote = true;
    [Tooltip("単独モードのときに読むリモコン名（例: WM-1）。デュアルモードOFF時のみ有効")]
    public string targetWMName = "";

    // ★★ デュアルリモコンモード（WM-1/WM-3で描画／WM-2/WM-4で保存） ★★
    [Header("Dual Wii Remotes")]
    [Tooltip("ON: drawWMName / remote3Name のBで描画 / saveWMName / remote4Name のBで保存")]
    public bool dualRemoteMode = true;
    [Tooltip("描画用（Bボタン）を読むリモコン名。例: WM-1")]
    public string drawWMName = "WM-1";
    [Tooltip("保存用（Bボタン）を読むリモコン名。例: WM-2")]
    public string saveWMName = "WM-2";

    string wmActiveName = null;
    bool wmSubscribed = false;

    // 単独モード用
    bool prevWmB = false, prevWmA = false;
    volatile bool curWmB = false, curWmA = false;

    // デュアル基本（WM-1/WM-3:描画B / WM-2/WM-4:保存B） ※prevDrawB/prevSaveA は合成した前回値
    bool prevDrawB = false, prevSaveA = false;
    volatile bool curDrawB = false, curSaveA = false;

    // Remote 3（描画のみ：B）
    [Header("Remote 3 (Draw=B)")]
    [Tooltip("ON にすると WM-3 の B ボタンで描画できます")]
    public bool enableRemote3 = true;
    [Tooltip("WM-3 の名前（例: WM-3）")]
    public string remote3Name = "WM-3";
    bool prevWm3B = false;
    volatile bool curWm3B = false;

    // Remote 4（保存のみ：B）
    [Header("Remote 4 (Save=B)")]
    [Tooltip("ON にすると WM-4 の B ボタンで保存できます")]
    public bool enableRemote4 = true;
    [Tooltip("WM-4 の名前（例: WM-4）")]
    public string remote4Name = "WM-4";
    bool prevWm4B = false;
    volatile bool curWm4B = false;

    private readonly List<Animator> _waitAnims = new List<Animator>();
    private readonly Dictionary<Animator, int> _waitStateHash = new Dictionary<Animator, int>();

    // ==== 追加：結果待ちフラグ ====
    private bool _readyForResult = false;

    // ==== 追加：一文字あたり制限時間＆ボーナス加算 ====
    [Header("Per-Character Time Limit")]
    [Tooltip("1文字あたりの制限時間（オンで有効）")]
    public bool enablePerCharTimeLimit = true;
    [Tooltip("1文字あたりの基本秒数（例：30秒）")]
    public float perCharSeconds = 30f;
    [Tooltip("残り時間表示（中央上部に出したい TextMeshProUGUI を割り当て）")]
    public TextMeshProUGUI timerText;

    [Tooltip("timerText を自動で『画面上部中央』に配置する")]
    public bool autoPlaceTimerAtTopCenter = true;
    [Tooltip("画面上端からの下向き余白（px）")]
    public float timerTopMargin = 40f;

    private float charTimerSec = 0f;
    private float _nextBonusSeconds = 0f; // ←前の文字で余った時間を次へ加算するバッファ

    // ==== 追加：カウントダウン ====
    [Header("Pre-Start Countdown")]
    [Tooltip("キャリブレーション後に 3,2,1,スタート！ を表示してから開始")]
    public bool enableStartCountdown = true;
    [Tooltip("何秒からカウントするか（3→2→1→スタート！）")]
    public int countdownFrom = 3;
    [Tooltip("カウントダウン表示に使う TextMeshProUGUI（大きな中央表示を推奨）")]
    public TextMeshProUGUI countdownText;
    [Tooltip("countdownText を自動で画面中央に配置する")]
    public bool autoPlaceCountdownAtCenter = true;
    [Tooltip("中央からのオフセット（px）")]
    public Vector2 countdownOffset = Vector2.zero;
    [Tooltip("「始め！」の表示時間（秒）")]
    public float startWordHoldSeconds = 0.7f;

    // ==== 追加：オーディオ ====
    [Header("Audio")]
    [Tooltip("カウントダウン用の効果音を鳴らす AudioSource（PlayOnAwake はOFF推奨）")]
    public AudioSource sfxSource;
    [Tooltip("「3」の効果音")]
    public AudioClip sfxThree;
    [Tooltip("「2」の効果音")]
    public AudioClip sfxTwo;
    [Tooltip("「1」の効果音")]
    public AudioClip sfxOne;
    [Tooltip("「始め！」の効果音")]
    public AudioClip sfxStart;
    [Tooltip("カウントダウン後に流すBGM用 AudioSource（Clip を設定、PlayOnAwake はOFF）")]
    public AudioSource bgmSource;

    // ★ 追加：カウントダウン前だけ流すBGM
    [Tooltip("シーン開始〜キャリブレーション/カウントダウン開始までだけ流す BGM 用 AudioSource（PlayOnAwake OFF）")]
    public AudioSource preCountdownBgmSource;

    void Awake()
    {
        tempContainer = new GameObject("TempGlyphContainer");
        tempContainer.hideFlags = HideFlags.HideInHierarchy;
        tempContainer.transform.parent = this.transform;
    }

    void Start()
    {
        var messageObj = GameObject.Find("Message");
        if (messageObj != null)
        {
            text = messageObj.GetComponent<TextMeshProUGUI>();
            if (text == null) Debug.LogWarning("Message に TextMeshProUGUI が見つかりません。");
        }

        var cogLineObj = GameObject.Find("CoGLine");
        if (cogLineObj != null) cogline = cogLineObj.GetComponent<CoGLine>();

        GameObject tdObj = GameObject.Find("TrailDrawer");
        if (tdObj != null) trailDrawer = tdObj.GetComponent<DrawTrail>();
        else Debug.LogWarning("DrawTrail が見つかりません。");

        if (movableObject != null)
        {
            initialPosition = movableObject.transform.position;
            initialPositionSet = true;
            lockedPosition = initialPosition;
        }

        if (moveArea == null)
        {
            GameObject found = GameObject.Find("MoveArea");
            if (found != null) moveArea = found;
        }

        if (trailDrawer != null) trailDrawer.NextGlyph();

        if (galleryAnimator == null)
        {
            if (galleryParent != null) galleryAnimator = galleryParent.GetComponent<Animator>();
            if (galleryAnimator == null && galleryArea != null) galleryAnimator = galleryArea.GetComponent<Animator>();
        }
        if (rightPanelAnimator == null && rightPanelParent != null)
        {
            rightPanelAnimator = rightPanelParent.GetComponent<Animator>();
        }
        if (moveAreaAnimator == null && moveArea != null)
        {
            moveAreaAnimator = moveArea.GetComponent<Animator>();
        }

        FreezeAnimatorAtStart(galleryAnimator);
        FreezeAnimatorAtStart(rightPanelAnimator);
        FreezeAnimatorAtStart(moveAreaAnimator);

        // タイマー初期化＆配置
        UpdateTimerUI();
        if (timerText != null && autoPlaceTimerAtTopCenter) PlaceTimerToTopCenter();

        // カウントダウン UI 初期化
        if (countdownText != null)
        {
            if (autoPlaceCountdownAtCenter) PlaceCountdownToCenter();
            countdownText.text = "";
        }

        // ★ プレカウントダウンBGM開始
        if (preCountdownBgmSource != null)
        {
            preCountdownBgmSource.Play();
        }

        ShowMessage("初期化中...");
    }

    void Update()
    {
        bool gotDataThisFrame = false;

        switch (status)
        {
            case Status.INIT:
                SetupOrReconnectBBIfNeeded();
                status = Status.CONNECTING;
                ShowMessage("バランスボードに接続しています...");
                break;

            case Status.CONNECTING:
                if (recv_bb != null && recv_bb.Connected())
                {
                    status = Status.WAIT_FOR_STAND;
                    ShowMessage("接続完了。乗ってキャリブレーションを開始してください。");
                    offsets = new float[4] { 0, 0, 0, 0 };
                    baselineSet = false;
                    calibrationTimer = 0f;
                    calibrationSum = 0f;
                    calibrationCount = 0;
                }
                break;

            case Status.WAIT_FOR_STAND:
                ProcessRecvBBQueue(ref gotDataThisFrame);
                if (lastMeasuredWeightKg >= 0f && lastMeasuredWeightKg >= presenceThresholdKg)
                {
                    calibrationTimer = 0f;
                    calibrationSum = 0f;
                    calibrationCount = 0;
                    calibrationCountdown = calibrationDuration;
                    ShowMessage($"キャリブレーション開始。");
                    status = Status.CALIBRATING;
                }
                else
                {
                    ShowMessage("バランスボードに乗ってください");
                }
                break;

            case Status.CALIBRATING:
                ProcessRecvBBQueue(ref gotDataThisFrame);

                if (!(lastMeasuredWeightKg >= 0f && lastMeasuredWeightKg >= presenceThresholdKg))
                {
                    ShowMessage("途中で降りたため中断。もう一度乗ってください。");
                    calibrationTimer = 0f;
                    calibrationSum = 0f;
                    calibrationCount = 0;
                    status = Status.WAIT_FOR_STAND;
                    break;
                }

                calibrationTimer += Time.deltaTime;
                calibrationSum += Mathf.Max(0f, lastMeasuredWeightKg);
                calibrationCount++;

                calibrationCountdown = Mathf.Max(0f, calibrationDuration - calibrationTimer);
                ShowMessage($"基準値計測中... 残り {Mathf.CeilToInt(calibrationCountdown)} 秒");

                if (calibrationTimer >= calibrationDuration)
                {
                    if (calibrationCount > 0)
                    {
                        // ★ 基準値：平均の70%
                        baselineWeightKg = (calibrationSum / calibrationCount) * 0.70f;
                        baselineSet = true;
                    }
                    else
                    {
                        baselineWeightKg = 0f;
                        baselineSet = false;
                        ShowMessage("キャリブレーション失敗。再試行してください。");
                    }

                    emaPressureInitialized = false;

                    // ★ カウントダウン前にお題を準備（右スロットとゴーストを表示しておく）
                    PrepareSingleRandomFruit();
                    SetupRandomPrompt();

                    if (enableStartCountdown)
                    {
                        // ★ カウントダウン前 BGM 停止
                        if (preCountdownBgmSource != null) preCountdownBgmSource.Stop();

                        status = Status.COUNTDOWN;
                        StartCoroutine(CoCountdownThenStart());
                    }
                    else
                    {
                        if (preCountdownBgmSource != null) preCountdownBgmSource.Stop();

                        status = Status.READY_TO_WRITE;
                        _nextBonusSeconds = 0f;
                        ResetCharTimer();
                    }
                }
                break;

            // ★ 新規：COUNTDOWN 中は入力不可。表示だけ進行
            case Status.COUNTDOWN:
                ProcessRecvBBQueue(ref gotDataThisFrame);
                break;

            case Status.READY_TO_WRITE:
                ProcessRecvBBQueue(ref gotDataThisFrame);
                break;

            case Status.FINISHED:
                break;
        }

        // ===== BBウォッチドッグ =====
        if (status != Status.FINISHED)
        {
            if (gotDataThisFrame) bbNoDataTimer = 0f;
            else bbNoDataTimer += Time.deltaTime;

            if (bbReconnectCooldown > 0f) bbReconnectCooldown -= Time.deltaTime;

            if (bbNoDataTimer >= bbReconnectIfNoDataSec && bbReconnectCooldown <= 0f)
            {
                Debug.LogWarning("[Game2] BBからデータが来ないため再接続を試みます。");
                ForceReconnectBB();
                status = Status.CONNECTING;
                bbReconnectCooldown = bbReconnectCooldownSec;
                bbNoDataTimer = 0f;
            }
        }

        // ====== Wiiリモコン＋キー入力 ======
        bool wmBDown, wmBUp, wmBHold, wmADown;

        if (enableWiiRemote && dualRemoteMode)
        {
            // --- デュアルモード ---
            // 描画：WM-1(B) + WM-3(B)
            // 保存：WM-2(B) + WM-4(B)
            bool curDrawCombined =
                curDrawB                       // drawWMName の B
                || (enableRemote3 && curWm3B); // remote3Name の B

            bool curSaveCombined =
                curSaveA                       // saveWMName の B（変数名はAだが実体はB）
                || (enableRemote4 && curWm4B); // remote4Name の B

            wmBDown = curDrawCombined && !prevDrawB;
            wmBUp = !curDrawCombined && prevDrawB;
            wmBHold = curDrawCombined;

            // 「保存トリガ」は保存系ボタンの立ち上がりで判定
            wmADown = curSaveCombined && !prevSaveA;

            // 前回値更新（合成）
            prevDrawB = curDrawCombined;
            prevSaveA = curSaveCombined;
        }
        else
        {
            // --- 単独モード：targetWMName(A/B) + （任意で）WM-3(B) / WM-4(B) を合成 ---
            bool curDrawCombined =
                curWmB                        // targetWMName の B
                || (enableRemote3 && curWm3B); // remote3Name の B

            bool curSaveCombined =
                curWmA                        // targetWMName の A
                || (enableRemote4 && curWm4B); // remote4Name の B

            bool prevDrawCombined =
                prevWmB
                || (enableRemote3 && prevWm3B);

            bool prevSaveCombined =
                prevWmA
                || (enableRemote4 && prevWm4B);

            wmBDown = enableWiiRemote && curDrawCombined && !prevDrawCombined;
            wmBUp = enableWiiRemote && !curDrawCombined && prevDrawCombined;
            wmBHold = enableWiiRemote && curDrawCombined;

            wmADown = enableWiiRemote && curSaveCombined && !prevSaveCombined;
        }

        // ====== 制限時間カウント（COUNTDOWN 中は動かない） ======
        if (status == Status.READY_TO_WRITE && enablePerCharTimeLimit)
        {
            charTimerSec -= Time.deltaTime;
            if (charTimerSec < 0f) charTimerSec = 0f;
            UpdateTimerUI();
        }

        if (trailDrawer != null && movableObject != null && status == Status.READY_TO_WRITE)
        {
            if (wmBDown || Input.GetKeyDown(KeyCode.Space)) trailDrawer.StartStroke();

            if (wmBHold || Input.GetKey(KeyCode.Space))
            {
                float measured = Mathf.Max(0f, lastMeasuredWeightKg);
                if (!emaPressureInitialized)
                {
                    emaPressureKg = measured;
                    emaPressureInitialized = true;
                }
                else
                {
                    emaPressureKg = Mathf.Lerp(emaPressureKg, measured, pressureEmaAlpha);
                }

                float adjusted = emaPressureKg - (baselineSet ? baselineWeightKg : 0f);
                if (adjusted < 0f) adjusted = 0f;

                int subSteps = Mathf.Max(1, drawSubSteps);
                Vector3 basePos = movableObject.transform.position;
                Vector3 vel = currentVelocity;
                float dt = Time.deltaTime;

                for (int s = 0; s < subSteps; s++)
                {
                    float t = (subSteps <= 1) ? 0f : ((float)s / subSteps);
                    Vector3 subPos = basePos + vel * (dt * t);
                    trailDrawer.UpdateStroke(subPos, adjusted);
                }
            }

            if (wmBUp || Input.GetKeyUp(KeyCode.Space)) trailDrawer.EndStroke();

            // ====== 保存要求（WM-2のB / WM-4のB / Enter / Tab か、時間切れ） ====== 
            bool timedOutSave = enablePerCharTimeLimit && (charTimerSec <= 0f);
            bool requestSave = wmADown
                               || Input.GetKeyDown(KeyCode.Return)
                               || Input.GetKeyDown(KeyCode.Tab)
                               || timedOutSave;

            if (requestSave)
            {
                trailDrawer.EndStroke();

                bool savedSomething = false;
                bool advanceEvenIfEmpty = timedOutSave; // ★ 時間切れなら未入力でも次へ進める

                if (saveGlyphAsObject)
                {
                    GameObject glyphRoot = trailDrawer.PopLastGlyph();
                    if (glyphRoot == null || !GlyphObjectHasAnyPoints(glyphRoot))
                    {
                        if (glyphRoot != null) Destroy(glyphRoot);

                        if (advanceEvenIfEmpty)
                        {
                            // ★ 何も書かれていなくても、時間切れなら白紙を保存
                            SaveBlankToGallery();
                            trailDrawer.NextGlyph();
                            savedSomething = true;
                        }
                        else
                        {
                            ShowMessage("");
                            goto SkipSaveFlow;
                        }
                    }
                    else
                    {
                        SaveGlyphObjectToGallery(glyphRoot);
                        trailDrawer.NextGlyph();
                        savedSomething = true;
                    }
                }
                else
                {
                    GlyphData gd;
                    if (!TryPopCurrentGlyphData(out gd))
                    {
                        if (advanceEvenIfEmpty)
                        {
                            // ★ 旧方式でも白紙を保存
                            SaveBlankToGallery();
                            savedSomething = true;
                        }
                        else
                        {
                            ShowMessage("");
                            goto SkipSaveFlow;
                        }
                    }
                    else
                    {
                        SaveGlyphToGallery(gd);
                        savedSomething = true;
                    }
                }

                // ====== 次の文字 or 完了 ======
                float bonusForNext = (enablePerCharTimeLimit) ? Mathf.Max(0f, charTimerSec) : 0f;

                if (!string.IsNullOrEmpty(currentPromptWord) && currentPromptIndex < currentPromptWord.Length - 1)
                {
                    currentPromptIndex++;
                    SetActiveSlot(currentPromptIndex);
                    ShowGhostForCurrentChar();

                    _nextBonusSeconds = bonusForNext;
                    ResetCharTimer();
                }
                else
                {
                    ClearGhost();
                    TriggerCompletionAnimations();
                    StartCoroutine(WaitAnimationsThenAwaitResultKey());
                    status = Status.FINISHED;

                    if (timerText != null) timerText.text = "";
                }

            SkipSaveFlow:;
            }

            if (Input.GetKeyDown(KeyCode.Backspace))
            {
                ShowMessage("取り消す文字がありません。");
            }

            if (Input.GetKeyDown(KeyCode.R)) { trailDrawer.ClearStrokes(); ShowMessage("ストロークをクリアしました"); }
            if (Input.GetKeyDown(KeyCode.C)) { trailDrawer.ClearTrail(); ShowMessage("全てのグリフをクリアしました"); }
        }

        if (galleryParent != null && savedGroups.Count > 0)
        {
            if (Input.GetKeyDown(KeyCode.RightArrow)) SelectGalleryIndex(gallerySelectedIndex + 1);
            if (Input.GetKeyDown(KeyCode.LeftArrow)) SelectGalleryIndex(gallerySelectedIndex - 1);
        }

        if (Input.GetKeyDown(KeyCode.Escape)) Application.Quit();

        // ==== 追加：アニメ完了後、1〜4キーで各リザルトへ ====
        if (status == Status.FINISHED && _readyForResult)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
                LoadResultScene(goldResultSceneName);
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
                LoadResultScene(silverResultSceneName);
            if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
                LoadResultScene(bronzeResultSceneName);
            if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4))
                LoadResultScene(worstResultSceneName);
        }

        // ==== 移動＆クランプ（READY_TO_WRITE/CONNECTED のみ） ====
        if ((status == Status.READY_TO_WRITE || status == Status.CONNECTED) && movableObject != null && initialPositionSet)
        {
            if (lastMeasuredWeightKg < weightThresholdKg)
            {
                aboveThresholdFrames = 0;
                currentVelocity = Vector3.zero;
                if (isMoving) { lockedPosition = movableObject.transform.position; isMoving = false; }
            }
            else
            {
                if (emaCog.magnitude < deadZone)
                {
                    aboveThresholdFrames = 0;
                    currentVelocity = Vector3.zero;
                    if (isMoving) { lockedPosition = movableObject.transform.position; isMoving = false; }
                }
                else
                {
                    aboveThresholdFrames++;
                    if (aboveThresholdFrames >= requiredFramesToMove)
                    {
                        isMoving = true;
                        // ★ emaCog.y は「下半分が重いほど +」になるようにバイアス済み
                        Vector3 dir = new Vector3(emaCog.x, -emaCog.y, 0f);
                        if (dir.sqrMagnitude > 0.000001f)
                        {
                            dir.Normalize();
                            currentVelocity = dir * moveSpeed;
                        }
                        else currentVelocity = Vector3.zero;
                    }
                }
            }

            Vector3 newPos = movableObject.transform.position;
            newPos = isMoving ? movableObject.transform.position + currentVelocity * Time.deltaTime : lockedPosition;
            newPos.z = initialPosition.z;

            bool wasClamped = false;
            if (moveArea != null)
            {
                Renderer rend = moveArea.GetComponent<Renderer>();
                if (rend != null)
                {
                    Bounds b = rend.bounds;
                    Vector3 clampedPos = newPos;
                    clampedPos.x = Mathf.Clamp(newPos.x, b.min.x, b.max.x);
                    clampedPos.y = Mathf.Clamp(newPos.y, b.min.y, b.max.y);
                    clampedPos.z = initialPosition.z;
                    newPos = clampedPos;
                    wasClamped = true;
                }
                else
                {
                    var bc = moveArea.GetComponent<BoxCollider>();
                    if (bc != null)
                    {
                        Bounds b = new Bounds(moveArea.transform.position + bc.center, bc.size);
                        Vector3 clampedPos = newPos;
                        clampedPos.x = Mathf.Clamp(newPos.x, b.min.x, b.max.x);
                        clampedPos.y = Mathf.Clamp(newPos.y, b.min.y, b.max.y);
                        clampedPos.z = initialPosition.z;
                        newPos = clampedPos;
                        wasClamped = true;
                    }
                }
            }

            if (!wasClamped)
            {
                if (paperObject != null)
                {
                    Renderer rend = paperObject.GetComponent<Renderer>();
                    if (rend != null)
                    {
                        Bounds b = rend.bounds;
                        Vector3 clamped = newPos;
                        clamped.x = Mathf.Clamp(newPos.x, b.min.x, b.max.x);
                        clamped.y = Mathf.Clamp(newPos.y, b.min.y, b.max.y);
                        clamped.z = initialPosition.z;
                        newPos = clamped;
                    }
                    else if (clampToViewport && Camera.main != null)
                    {
                        Vector3 vp = Camera.main.WorldToViewportPoint(newPos);
                        vp.x = Mathf.Clamp01(vp.x); vp.y = Mathf.Clamp01(vp.y);
                        float zDepth = Camera.main.WorldToScreenPoint(initialPosition).z;
                        newPos = Camera.main.ViewportToWorldPoint(new Vector3(vp.x, vp.y, zDepth));
                        newPos.z = initialPosition.z;
                    }
                }
                else if (clampToViewport && Camera.main != null)
                {
                    Vector3 vp = Camera.main.WorldToViewportPoint(newPos);
                    vp.x = Mathf.Clamp01(vp.x); vp.y = Mathf.Clamp01(vp.y);
                    float zDepth = Camera.main.WorldToScreenPoint(initialPosition).z;
                    newPos = Camera.main.ViewportToWorldPoint(new Vector3(vp.x, vp.y, zDepth));
                    newPos.z = initialPosition.z;
                }
            }

            movableObject.transform.position = newPos;
        }

        // Wiiの前回状態を更新
        prevWmB = curWmB;
        prevWmA = curWmA;
        prevWm3B = curWm3B;
        prevWm4B = curWm4B;
    }

    // ===== RecvBB 接続ユーティリティ =====
    void SetupOrReconnectBBIfNeeded()
    {
        if (GlobalData.recv_bb != null)
        {
            recv_bb = GlobalData.recv_bb;
            if (recv_bb.Connected())
            {
                try { recv_bb.Clear(); } catch { }
            }
            else
            {
                try { recv_bb.Disconnect(); } catch { }
                try { recv_bb.Connect(); } catch { }
            }
        }
        else
        {
            recv_bb = new RecvBB();
            try { recv_bb.Connect(); } catch { }
            GlobalData.recv_bb = recv_bb;
        }

        AttachWMHandler();
    }

    void ForceReconnectBB()
    {
        DetachWMHandler();
        try { recv_bb?.Disconnect(); } catch { }
        recv_bb = new RecvBB();
        try { recv_bb.Connect(); } catch { }
        GlobalData.recv_bb = recv_bb;
        bbNoDataTimer = 0f;
        AttachWMHandler();
    }

    void AttachWMHandler()
    {
        if (!enableWiiRemote) return;
        if (recv_bb == null) return;
        if (wmSubscribed) return;
        recv_bb.OnWM += OnWMInput;
        wmSubscribed = true;
    }

    void DetachWMHandler()
    {
        if (recv_bb == null) return;
        if (!wmSubscribed) return;
        try { recv_bb.OnWM -= OnWMInput; } catch { }
        wmSubscribed = false;
    }

    void OnDestroy()
    {
        DetachWMHandler();
    }

    // Wiiリモコン入力イベント（別スレッドから来る）
    void OnWMInput(RecvBB.WMButtons b)
    {
        if (!enableWiiRemote) return;

        if (dualRemoteMode)
        {
            // WM-1 の B で描画（A/Bどちらでも描画扱い）
            if (!string.IsNullOrEmpty(drawWMName) &&
                string.Equals(b.name, drawWMName, StringComparison.OrdinalIgnoreCase))
            {
                curDrawB = b.A || b.B;
            }

            // WM-2 の B で保存（※名称はAだが実体はB、A/Bどちらでも保存扱い）
            if (!string.IsNullOrEmpty(saveWMName) &&
                string.Equals(b.name, saveWMName, StringComparison.OrdinalIgnoreCase))
            {
                curSaveA = b.A || b.B;
            }

            // WM-3：B=描画のみ
            if (enableRemote3 &&
                !string.IsNullOrEmpty(remote3Name) &&
                string.Equals(b.name, remote3Name, StringComparison.OrdinalIgnoreCase))
            {
                curWm3B = b.B;
            }

            // WM-4：B=保存のみ
            if (enableRemote4 &&
                !string.IsNullOrEmpty(remote4Name) &&
                string.Equals(b.name, remote4Name, StringComparison.OrdinalIgnoreCase))
            {
                curWm4B = b.B;
            }

            return; // デュアル時はここで終了
        }

        // === 単独WMモード ===
        // WM-3 / WM-4 は単独モードでも併用可にする
        if (enableRemote3 &&
            !string.IsNullOrEmpty(remote3Name) &&
            string.Equals(b.name, remote3Name, StringComparison.OrdinalIgnoreCase))
        {
            curWm3B = b.B;   // 描画のみ
        }

        if (enableRemote4 &&
            !string.IsNullOrEmpty(remote4Name) &&
            string.Equals(b.name, remote4Name, StringComparison.OrdinalIgnoreCase))
        {
            curWm4B = b.B;   // 保存のみ
        }

        if (!string.IsNullOrEmpty(targetWMName))
        {
            if (!string.Equals(b.name, targetWMName, StringComparison.OrdinalIgnoreCase)) return;
        }

        if (string.IsNullOrEmpty(wmActiveName)) wmActiveName = b.name;

        if (!string.IsNullOrEmpty(wmActiveName) &&
            !string.Equals(wmActiveName, b.name, StringComparison.OrdinalIgnoreCase))
            return;

        curWmB = b.B;
        curWmA = b.A;
    }

    bool HasAnimatorParameter(Animator animator, string name, AnimatorControllerParameterType type)
    {
        foreach (var p in animator.parameters)
        {
            if (p.type == type && p.name == name) return true;
        }
        return false;
    }

    void FreezeAnimatorAtStart(Animator anim)
    {
        if (anim == null) return;

        foreach (var p in anim.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Trigger)
                anim.ResetTrigger(p.name);
        }
        anim.Update(0f);
        anim.enabled = false;
    }

    // ==== “sample text” 表示 ====
    void EnsureGhost()
    {
        if (ghostGO != null && ghostTMP != null) return;

        if (sampleTextPrefab == null)
        {
            Debug.LogWarning("[Game2] sampleTextPrefab が未割り当てです。Inspector で設定してください。");
            return;
        }

        ghostGO = Instantiate(sampleTextPrefab);
        ghostGO.name = "SampleTextActive";

        ghostTMP = ghostGO.GetComponentInChildren<TMP_Text>(true);
        ghostBaseScale = ghostGO.transform.localScale;
    }

    void ClearGhost()
    {
        if (ghostGO != null) Destroy(ghostGO);
        ghostGO = null;
        ghostTMP = null;
        ghostBaseScale = Vector3.one;
    }

    void ShowGhostForCurrentChar()
    {
        EnsureGhost();

        if (ghostTMP != null && !string.IsNullOrEmpty(currentPromptWord))
        {
            int idx = Mathf.Clamp(currentPromptIndex, 0, currentPromptWord.Length - 1);
            ghostTMP.text = currentPromptWord[idx].ToString();
        }
    }

    float GetGalleryStartWorldY()
    {
        if (galleryArea != null)
        {
            var r = galleryArea.GetComponent<Renderer>();
            if (r != null) return r.bounds.max.y;

            var bc = galleryArea.GetComponent<BoxCollider>();
            if (bc != null)
            {
                Bounds b = new Bounds(galleryArea.transform.position + bc.center, bc.size);
                return b.max.y;
            }
        }

        if (galleryParent != null)
        {
            Vector3 startWorld = galleryParent.TransformPoint(galleryStartLocalPos);
            return startWorld.y;
        }

        if (Camera.main != null)
        {
            Vector3 screen = new Vector3(Screen.width * 0.1f, Screen.height - 50f, 10f);
            return Camera.main.ScreenToWorldPoint(screen).y;
        }
        return 0f;
    }

    bool TryPopCurrentGlyphData(out GlyphData gd)
    {
        gd = null;

        if (trailDrawer == null)
        {
            ShowMessage("内部エラー: TrailDrawer が見つかりません。");
            return false;
        }

        trailDrawer.EndStroke();

        GameObject popped = trailDrawer.PopLastGlyph();
        if (popped == null) return false;

        LineRenderer[] lrs = popped.GetComponentsInChildren<LineRenderer>(true);
        int totalPoints = 0;
        foreach (var lr in lrs) if (lr != null) totalPoints += lr.positionCount;

        if (totalPoints <= 0)
        {
            Destroy(popped);
            trailDrawer.NextGlyph();
            return false;
        }

        gd = new GlyphData { name = popped.name };
        foreach (var lr in lrs)
        {
            if (lr == null || lr.positionCount <= 0) continue;

            int n = lr.positionCount;
            Vector3[] pts = new Vector3[n];
            for (int i = 0; i < n; i++) pts[i] = lr.GetPosition(i);
            gd.lrWorldPts.Add(pts);

            gd.lrWidths.Add(lr.startWidth);
            gd.lrEndWidths.Add(lr.endWidth);

            gd.lrWidthCurves.Add(lr.widthCurve != null ? new AnimationCurve(lr.widthCurve.keys) : new AnimationCurve(new Keyframe[] { new Keyframe(0, 1), new Keyframe(1, 1) }));
            gd.lrWidthMultipliers.Add(lr.widthMultiplier);

            Gradient grad = new Gradient();
            var srcGrad = lr.colorGradient;
            if (srcGrad != null)
                grad.SetKeys(srcGrad.colorKeys, srcGrad.alphaKeys);
            else
                grad.SetKeys(new GradientColorKey[] { new GradientColorKey(lr.startColor, 0), new GradientColorKey(lr.endColor, 1) },
                             new GradientAlphaKey[] { new GradientAlphaKey(lr.startColor.a, 0), new GradientAlphaKey(lr.endColor.a, 1) });
            gd.lrColorGradients.Add(grad);
            gd.lrStartColors.Add(lr.startColor);
            gd.lrEndColors.Add(lr.endColor);

            Material srcMat = lr.material != null ? lr.material : lr.sharedMaterial;
            gd.lrMaterials.Add(srcMat != null ? new Material(srcMat) : null);

            gd.lrTextureModes.Add(lr.textureMode);
            gd.lrAlignments.Add(lr.alignment);
            gd.lrNumCapVertices.Add(lr.numCapVertices);
            gd.lrNumCornerVertices.Add(lr.numCornerVertices);
            gd.lrUseWorldSpace.Add(lr.useWorldSpace);
            gd.lrSortingLayerIDs.Add(lr.sortingLayerID);
            gd.lrSortingOrders.Add(lr.sortingOrder);
        }

        Destroy(popped);
        trailDrawer.NextGlyph();
        return true;
    }

    void SaveGlyphToGallery(GlyphData glyph)
    {
        Vector3 targetWorldPos;
        bool hasGalleryBounds = false;
        Bounds galleryBounds = new Bounds();
        if (galleryArea != null)
        {
            var rendG = galleryArea.GetComponent<Renderer>();
            if (rendG != null) { galleryBounds = rendG.bounds; hasGalleryBounds = true; }
            else
            {
                var bc = galleryArea.GetComponent<BoxCollider>();
                if (bc != null) { galleryBounds = new Bounds(galleryArea.transform.position + bc.center, bc.size); hasGalleryBounds = true; }
            }
        }

        if (hasGalleryBounds)
        {
            float x = galleryBounds.center.x;
            float yTop = galleryBounds.max.y;
            float z = galleryBounds.center.z;
            targetWorldPos = new Vector3(x, yTop - (savedCount * gallerySpacingY), z);
        }
        else if (galleryParent != null)
        {
            Vector3 worldStart = galleryParent.TransformPoint(galleryStartLocalPos);
            targetWorldPos = worldStart + Vector3.down * (savedCount * gallerySpacingY);
        }
        else
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 screenPos = new Vector3(50, cam.pixelHeight - 50 - savedCount * (gallerySpacingY * 100f), 10f);
                targetWorldPos = cam.ScreenToWorldPoint(screenPos);
            }
            else targetWorldPos = Vector3.left * 3f + Vector3.up * (2f - savedCount * gallerySpacingY);
        }

        GameObject savedGroup = new GameObject("SavedGroup_" + savedCount);
        savedGroup.transform.position = targetWorldPos;
        savedGroup.transform.rotation = Quaternion.identity;
        savedGroup.isStatic = false;
        if (galleryParent != null) savedGroup.transform.SetParent(galleryParent, true);

        Vector3 moveCenterWorld = Vector3.zero;
        Quaternion moveRotation = Quaternion.identity;
        if (moveArea != null)
        {
            var moveR = moveArea.GetComponent<Renderer>();
            if (moveR != null) moveCenterWorld = moveR.bounds.center;
            else
            {
                var bc = moveArea.GetComponent<BoxCollider>();
                if (bc != null) moveCenterWorld = moveArea.transform.position + bc.center;
                else moveCenterWorld = moveArea.transform.position;
            }
            moveRotation = moveArea.transform.rotation;
        }

        if (moveArea != null)
        {
            GameObject moveClone = Instantiate(moveArea);
            moveClone.name = "Saved_MoveArea_" + savedCount;
            moveClone.transform.SetParent(savedGroup.transform, false);
            moveClone.transform.position = targetWorldPos;
            moveClone.transform.rotation = moveArea.transform.rotation;
            moveClone.transform.localScale = moveArea.transform.localScale * gallerySavedScale;

            foreach (var tr in moveClone.GetComponentsInChildren<Transform>(true))
                tr.gameObject.isStatic = false;

            foreach (var mb in moveClone.GetComponentsInChildren<MonoBehaviour>(true)) Destroy(mb);
            foreach (var col in moveClone.GetComponentsInChildren<Collider>(true)) Destroy(col);
            var rb = moveClone.GetComponentInChildren<Rigidbody>(true); if (rb) Destroy(rb);
        }

        Vector3 nudge = Vector3.forward * galleryStrokeZOffset;
        if (Camera.main != null) nudge = -Camera.main.transform.forward.normalized * galleryStrokeZOffset;

        for (int li = 0; li < glyph.lrWorldPts.Count; li++)
        {
            Vector3[] pts = glyph.lrWorldPts[li];
            if (pts == null || pts.Length == 0) continue;

            GameObject strokeObj = new GameObject($"Stroke_saved_{savedCount}_0_{li}");
            strokeObj.transform.SetParent(savedGroup.transform, true);
            strokeObj.transform.SetPositionAndRotation(targetWorldPos + nudge, moveRotation);
            strokeObj.transform.localScale = Vector3.one;
            strokeObj.isStatic = false;

            LineRenderer newLR = strokeObj.AddComponent<LineRenderer>();

            Material srcMat = null;
            if (li < glyph.lrMaterials.Count) srcMat = glyph.lrMaterials[li];
            if (srcMat != null) newLR.material = new Material(srcMat);
            else if (trailDrawer != null && trailDrawer.lineMaterial != null) newLR.material = new Material(trailDrawer.lineMaterial);
            else newLR.material = new Material(Shader.Find("Sprites/Default"));

            if (newLR.material != null && newLR.material.renderQueue < 3100) newLR.material.renderQueue = 3100;

            newLR.numCapVertices = (li < glyph.lrNumCapVertices.Count) ? glyph.lrNumCapVertices[li] : 8;
            newLR.numCornerVertices = (li < glyph.lrNumCornerVertices.Count) ? glyph.lrNumCornerVertices[li] : 8;
            newLR.textureMode = (li < glyph.lrTextureModes.Count) ? glyph.lrTextureModes[li] : LineTextureMode.Stretch;
            newLR.alignment = (li < glyph.lrAlignments.Count) ? glyph.lrAlignments[li] : LineAlignment.View;

            newLR.sortingLayerID = (li < glyph.lrSortingLayerIDs.Count) ? glyph.lrSortingLayerIDs[li] : 0;
            int baseOrder = (li < glyph.lrSortingOrders.Count) ? glyph.lrSortingOrders[li] : 0;
            newLR.sortingOrder = baseOrder + gallerySortingOrderBoost;

            var rend = newLR.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.receiveShadows = false;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rend.allowOcclusionWhenDynamic = false;
            }

            newLR.useWorldSpace = false;
            newLR.loop = false;
            newLR.positionCount = pts.Length;

            if (li < glyph.lrColorGradients.Count && glyph.lrColorGradients[li] != null)
            {
                Gradient g = new Gradient();
                g.SetKeys(glyph.lrColorGradients[li].colorKeys, glyph.lrColorGradients[li].alphaKeys);
                newLR.colorGradient = g;
            }
            else
            {
                Color sc = (li < glyph.lrStartColors.Count) ? glyph.lrStartColors[li] : Color.black;
                Color ec = (li < glyph.lrEndColors.Count) ? glyph.lrEndColors[li] : sc;
                newLR.startColor = sc;
                newLR.endColor = ec;
            }

            if (li < glyph.lrWidthCurves.Count && glyph.lrWidthCurves[li] != null)
            {
                newLR.widthCurve = new AnimationCurve(glyph.lrWidthCurves[li].keys);
                float mul = (li < glyph.lrWidthMultipliers.Count) ? glyph.lrWidthMultipliers[li] : 1f;
                newLR.widthMultiplier = mul * gallerySavedScale;
            }
            else
            {
                float sw = (li < glyph.lrWidths.Count) ? glyph.lrWidths[li] : 0.05f;
                float ew = (li < glyph.lrEndWidths.Count) ? glyph.lrEndWidths[li] : sw;
                newLR.startWidth = sw * gallerySavedScale;
                newLR.endWidth = ew * gallerySavedScale;
            }

            for (int i = 0; i < pts.Length; i++)
            {
                Vector3 offset = pts[i] - moveCenterWorld;
                Vector3 local = offset * gallerySavedScale;
                newLR.SetPosition(i, local);
            }

            newLR.Simplify(0f);
        }

        savedGroups.Add(savedGroup);
        savedCount++;
    }

    void SelectGalleryIndex(int idx)
    {
        if (savedGroups == null || savedGroups.Count == 0)
        {
            gallerySelectedIndex = -1;
            return;
        }

        gallerySelectedIndex = Mathf.Clamp(idx, 0, savedGroups.Count - 1);

        for (int i = 0; i < savedGroups.Count; i++)
        {
            if (savedGroups[i] == null) continue;
            savedGroups[i].transform.localScale = Vector3.one;
        }
    }

    // ★★★ ここで「上下の加重」＋ バイアスを計算 ★★★
    void ProcessRecvBBQueue(ref bool gotDataThisFrame)
    {
        if (recv_bb == null) return;

        while (recv_bb.Count() > 0)
        {
            RecvBB.BBDatum d = recv_bb.Dequeue();
            if (d != null)
            {
                bbdata.Add(d);
                float weightKg = TryGetWeightKgFromDatum(d);
                lastMeasuredWeightKg = weightKg;

                float[] cog = null;
                try
                {
                    cog = d.getCoG(offsets);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"getCoG 呼び出しに失敗しました: {ex.Message}");
                }

                if (cogline != null) cogline.appendPosition(d, offsets);

                float cogX = 0f;
                if (cog != null && cog.Length >= 1) cogX = cog[0];

                // 下半分( bl+br ) と上半分( tl+tr ) の差から -1〜+1 程度の縦バイアスを求める
                float bias;
                bool hasBias = TryGetVerticalBias01(d, out bias);

                float rawY;
                if (hasBias)
                {
                    // 正の値: 下半分の方が重い。
                    // cogForwardOffset を足して、多少上半分が重くても下に動きやすくする。
                    float effectiveBias = bias + cogForwardOffset;
                    rawY = Mathf.Clamp(effectiveBias, -1f, 1f);
                }
                else if (cog != null && cog.Length >= 2)
                {
                    // フォールバック：従来通り CoG の Y を使用
                    rawY = cog[1];
                }
                else
                {
                    rawY = 0f;
                }

                // newCog.y が正 → 下方向に動く（dir = (x, -y) のため）
                Vector2 newCog = new Vector2(cogX, rawY);

                if (!emaInitialized)
                {
                    emaCog = newCog;
                    emaInitialized = true;
                }
                else
                {
                    emaCog = Vector2.Lerp(emaCog, newCog, emaAlpha);
                }
                gotDataThisFrame = true;
            }
        }

        if (!gotDataThisFrame && emaInitialized)
        {
            emaCog = Vector2.Lerp(emaCog, Vector2.zero, emaAlpha);
            if (emaCog.magnitude < deadZone * 0.5f)
            {
                emaCog = Vector2.zero;
                emaInitialized = false;
            }
        }
    }

    void ShowMessage(string msg)
    {
        if (text == null) return;
        text.text = msg;
        if (messageCoroutine != null) StopCoroutine(messageCoroutine);
        messageCoroutine = StartCoroutine(ClearMessageAfterSeconds(3f));
    }

    void ShowStickyMessage(string msg)
    {
        if (text == null) return;
        text.text = msg;
        if (messageCoroutine != null)
        {
            StopCoroutine(messageCoroutine);
            messageCoroutine = null;
        }
    }

    IEnumerator ClearMessageAfterSeconds(float sec)
    {
        yield return new WaitForSeconds(sec);
        if (text != null) text.text = "";
        messageCoroutine = null;
    }

    // ========= 任意文字数のお題を許容 =========
    void SetupRandomPrompt()
    {
        ClearPromptSlots();

        if (sampleWords == null || sampleWords.Count == 0)
        {
            ShowMessage("お題が未登録です。Inspector の sampleWords を設定してください。");
            currentPromptWord = null;
            ClearGhost();
            return;
        }

        List<string> candidates = new List<string>();
        foreach (var s in sampleWords)
        {
            if (!string.IsNullOrEmpty(s)) candidates.Add(s);
        }

        string selected = candidates.Count > 0
            ? candidates[URandom.Range(0, candidates.Count)]
            : sampleWords[URandom.Range(0, sampleWords.Count)];

        currentPromptWord = selected;
        GlobalData.ResultPromptWord = currentPromptWord;
        currentPromptIndex = 0;

        CreateRightSlotsForWord(selected);
        SetActiveSlot(0);
        ShowGhostForCurrentChar();
    }

    // ========= 任意文字数入力をそのまま使用 =========
    void PrepareSingleRandomFruit()
    {
        if (!string.IsNullOrEmpty(GlobalData.PromptWord))
        {
            sampleWords = new List<string>() { GlobalData.PromptWord.Trim() };
            return;
        }

        string chosen = FRUIT_POOL[URandom.Range(0, FRUIT_POOL.Length)];
        sampleWords = new List<string>() { chosen };
    }

    bool IsAllHiragana(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (char c in s)
        {
            if (c < '\u3041' || c > '\u3096') return false;
        }
        return true;
    }

    void ClearPromptSlots()
    {
        foreach (var go in rightSlots) if (go != null) Destroy(go);
        rightSlots.Clear();
        activeSlotIndex = 0;
    }

    // 右側お手本（縦書きで上から下へ）
    void CreateRightSlotsForWord(string word)
    {
        if (backgroundPrefab == null || rightPanelParent == null)
        {
            ShowMessage("backgroundPrefab / rightPanelParent が未設定です。");
            return;
        }

        ClearPromptSlots();

        float galleryStartY = GetGalleryStartWorldY();

        Vector3 baseWorldPos = new Vector3(
            rightPanelParent.position.x,
            galleryStartY,
            rightPanelParent.position.z
        );

        Vector3 moveSize = Vector3.one;
        if (moveArea != null)
        {
            var mr = moveArea.GetComponent<Renderer>();
            if (mr != null) moveSize = mr.bounds.size;
            else
            {
                var bc = moveArea.GetComponent<BoxCollider>();
                if (bc != null) moveSize = bc.size;
            }
        }

        for (int i = 0; i < word.Length; i++)
        {
            GameObject slot = Instantiate(backgroundPrefab, rightPanelParent, false);
            slot.name = $"RightSlot_{i}_{word[i]}";

            // 縦並び
            slot.transform.position = baseWorldPos + Vector3.down * (i * rightSlotSpacing);
            slot.transform.rotation = rightPanelParent.rotation;

            var sr = slot.GetComponent<Renderer>();
            if (sr != null && moveSize.x > 0.0001f && moveSize.y > 0.0001f)
            {
                Vector3 slotSize = sr.bounds.size;
                if (slotSize.x > 0.0001f && slotSize.y > 0.0001f)
                {
                    Vector3 scaleMul = new Vector3(moveSize.x / slotSize.x, moveSize.y / slotSize.y, 1f);
                    scaleMul *= 0.35f;
                    slot.transform.localScale = Vector3.Scale(slot.transform.localScale, scaleMul);
                }
            }

            TMP_Text tmp = slot.GetComponentInChildren<TMP_Text>();
            if (tmp != null)
            {
                tmp.text = word[i].ToString();
                if (brushFont != null) tmp.font = brushFont;
                tmp.enableWordWrapping = false;
                tmp.alignment = TextAlignmentOptions.Center;
                Color c = tmp.color;
                tmp.color = new Color(c.r, c.g, c.b, 0.5f);
            }

            rightSlots.Add(slot);
        }

        SetActiveSlot(0);
    }

    void SetActiveSlot(int idx)
    {
        if (rightSlots.Count == 0) return;
        activeSlotIndex = Mathf.Clamp(idx, 0, rightSlots.Count - 1);

        for (int i = 0; i < rightSlots.Count; i++)
        {
            var r = rightSlots[i].GetComponent<Renderer>();
            if (r != null)
            {
                r.material = new Material(r.material);
                r.material.color = (i == activeSlotIndex) ? new Color(1f, 1f, 0.65f, 1f) : Color.white;
            }
        }
    }

    bool CurrentGlyphHasAnyPoints()
    {
        if (trailDrawer == null) return false;
        var g = trailDrawer.PeekLastGlyph();
        if (g == null) return false;

        var lrs = g.GetComponentsInChildren<LineRenderer>(true);
        foreach (var lr in lrs)
        {
            if (lr != null && lr.positionCount > 0) return true;
        }
        return false;
    }

    float TryGetWeightKgFromDatum(RecvBB.BBDatum d)
    {
        if (d == null) return -1f;
        System.Type t = d.GetType();

        string[] preferNames = new string[] {
            "totalWeight", "total_weight", "TotalWeight", "weightKg", "weight", "Weight", "sumWeight", "sum", "total"
        };

        foreach (var name in preferNames)
        {
            var prop = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null && (prop.PropertyType == typeof(float) || prop.PropertyType == typeof(double) || prop.PropertyType == typeof(int)))
            {
                object val = prop.GetValue(d, null);
                return ConvertToFloatKg(val);
            }
            var field = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && (field.FieldType == typeof(float) || field.FieldType == typeof(double) || field.FieldType == typeof(int)))
            {
                object val = field.GetValue(d);
                return ConvertToFloatKg(val);
            }
        }

        // ★ 4隅の重さが取れる場合はそれを合計
        float tl, tr, bl, br;
        if (TryGetCornerWeights(d, out tl, out tr, out bl, out br))
        {
            return tl + tr + bl + br;
        }

        string[] methodNames = new string[] { "GetTotalWeight", "getTotalWeight", "GetWeight", "getWeight", "TotalWeight", "Total" };
        foreach (var mname in methodNames)
        {
            var mi = t.GetMethod(mname, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new System.Type[0], null);
            if (mi != null && (mi.ReturnType == typeof(float) || mi.ReturnType == typeof(double) || mi.ReturnType == typeof(int)))
            {
                object val = mi.Invoke(d, null);
                return ConvertToFloatKg(val);
            }
        }

        var numericMembers = new List<object>();
        foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (f.FieldType == typeof(float) || f.FieldType == typeof(double) || f.FieldType == typeof(int))
            {
                numericMembers.Add(f.GetValue(d));
            }
        }
        foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (p.PropertyType == typeof(float) || p.PropertyType == typeof(double) || p.PropertyType == typeof(int))
            {
                if (p.GetIndexParameters().Length == 0)
                {
                    numericMembers.Add(p.GetValue(d, null));
                }
            }
        }

        if (numericMembers.Count >= 4)
        {
            float sum = 0f;
            int count = 0;
            foreach (var obj in numericMembers)
            {
                if (count >= 4) break;
                sum += ConvertToFloatKg(obj);
                count++;
            }
            return sum;
        }

        if (!warnedNoWeightInfo)
        {
            Debug.LogWarning("[Game2] BBDatum から体重情報を取得できませんでした。");
            warnedNoWeightInfo = true;
        }
        return -1f;
    }

    float ConvertToFloatKg(object val)
    {
        if (val == null) return 0f;
        if (val is float f) return f;
        if (val is double d) return (float)d;
        if (val is int i) return (float)i;
        try { return System.Convert.ToSingle(val); }
        catch { return 0f; }
    }

    // ==== 4隅の重さを取得するヘルパー ====
    bool TryGetCornerWeights(RecvBB.BBDatum d, out float tl, out float tr, out float bl, out float br)
    {
        tl = tr = bl = br = 0f;
        if (d == null) return false;
        System.Type t = d.GetType();

        string[][] cornerCandidates = new string[][]
        {
            new string[] { "tl", "tr", "bl", "br" },
            new string[] { "TL", "TR", "BL", "BR" },
            new string[] { "topLeft", "topRight", "bottomLeft", "bottomRight" },
            new string[] { "TopLeft", "TopRight", "BottomLeft", "BottomRight" },
            new string[] { "w1", "w2", "w3", "w4" },
            new string[] { "weight1", "weight2", "weight3", "weight4" }
        };

        foreach (var names in cornerCandidates)
        {
            if (names.Length < 4) continue;

            float v0, v1, v2, v3;
            if (TryGetFieldOrPropertyAsFloat(t, d, names[0], out v0) &&
                TryGetFieldOrPropertyAsFloat(t, d, names[1], out v1) &&
                TryGetFieldOrPropertyAsFloat(t, d, names[2], out v2) &&
                TryGetFieldOrPropertyAsFloat(t, d, names[3], out v3))
            {
                tl = v0; tr = v1; bl = v2; br = v3;
                return true;
            }
        }

        return false;
    }

    bool TryGetFieldOrPropertyAsFloat(System.Type t, object obj, string name, out float value)
    {
        var prop = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop != null &&
            (prop.PropertyType == typeof(float) || prop.PropertyType == typeof(double) || prop.PropertyType == typeof(int)))
        {
            object val = prop.GetValue(obj, null);
            value = ConvertToFloatKg(val);
            return true;
        }

        var field = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null &&
            (field.FieldType == typeof(float) || field.FieldType == typeof(double) || field.FieldType == typeof(int)))
        {
            object val = field.GetValue(obj);
            value = ConvertToFloatKg(val);
            return true;
        }

        value = 0f;
        return false;
    }

    /// <summary>
    /// 下半分( bl+br ) と 上半分( tl+tr ) の差から -1〜+1 程度のバイアス値を返す。
    /// 正の値: 下半分の方が重い（＝カーソルを下に動かす方向）。
    /// </summary>
    bool TryGetVerticalBias01(RecvBB.BBDatum d, out float bias)
    {
        bias = 0f;
        float tl, tr, bl, br;
        if (!TryGetCornerWeights(d, out tl, out tr, out bl, out br)) return false;

        float top = tl + tr;
        float bottom = bl + br;
        float total = top + bottom;
        if (total <= 0.0001f) return false;

        bias = (bottom - top) / total; // -1〜+1 程度
        return true;
    }

    // =================== 新方式：オブジェクト保存 =================

    bool GlyphObjectHasAnyPoints(GameObject root)
    {
        if (root == null) return false;
        var lrs = root.GetComponentsInChildren<LineRenderer>(true);
        foreach (var lr in lrs) if (lr != null && lr.positionCount > 0) return true;
        return false;
    }

    void CloneMoveAreaVisual(Transform parent)
    {
        if (moveArea == null || parent == null) return;

        GameObject moveClone = Instantiate(moveArea);
        moveClone.name = "Saved_MoveArea_" + savedCount;
        moveClone.transform.SetParent(parent, true);
        moveClone.isStatic = false;

        foreach (var tr in moveClone.GetComponentsInChildren<Transform>(true))
            tr.gameObject.isStatic = false;

        foreach (var mb in moveClone.GetComponentsInChildren<MonoBehaviour>(true)) Destroy(mb);
        foreach (var col in moveClone.GetComponentsInChildren<Collider>(true)) Destroy(col);
        var rb = moveClone.GetComponentInChildren<Rigidbody>(true); if (rb) Destroy(rb);
    }

    void ConvertLineRenderersToLocalAndScale(GameObject root, float widthScale, bool applyWidthScale)
    {
        if (root == null) return;
        var lrs = root.GetComponentsInChildren<LineRenderer>(true);
        foreach (var lr in lrs)
        {
            if (lr == null || lr.positionCount <= 0) continue;

            if (lr.useWorldSpace)
            {
                int n = lr.positionCount;
                Vector3[] worldPts = new Vector3[n];
                lr.GetPositions(worldPts);
                Vector3[] localPts = new Vector3[n];
                for (int i = 0; i < n; i++)
                {
                    localPts[i] = lr.transform.InverseTransformPoint(worldPts[i]);
                }
                lr.useWorldSpace = false;
                lr.positionCount = n;
                lr.SetPositions(localPts);
            }

            if (applyWidthScale)
            {
                if (lr.widthCurve != null && lr.widthCurve.keys != null && lr.widthCurve.keys.Length > 0)
                    lr.widthMultiplier *= widthScale;
                else
                {
                    lr.startWidth *= widthScale;
                    lr.endWidth *= widthScale;
                }
            }

            StabilizeLineRenderer(lr);
        }
    }

    Vector3 GetWorldBoundsCenter(GameObject root)
    {
        if (root == null) return Vector3.zero;
        var rends = root.GetComponentsInChildren<Renderer>(true);
        if (rends != null && rends.Length > 0)
        {
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return b.center;
        }
        return root.transform.position;
    }

    void StabilizeLineRenderer(LineRenderer lr)
    {
        if (lr == null) return;

        if (lr.material != null && lr.material.renderQueue < 3100) lr.material.renderQueue = 3100;
        if (lr.sharedMaterial != null && lr.sharedMaterial.renderQueue < 3100) lr.sharedMaterial.renderQueue = 3100;

        lr.sortingOrder = lr.sortingOrder + gallerySortingOrderBoost;

        var rend = lr.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.receiveShadows = false;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.allowOcclusionWhenDynamic = false;
        }

        lr.Simplify(0f);
    }

    void SaveGlyphObjectToGallery(GameObject glyphRoot)
    {
        if (glyphRoot == null) return;

        Vector3 targetWorldPos;
        bool hasGalleryBounds = false;
        Bounds galleryBounds = new Bounds();

        if (galleryArea != null)
        {
            var rendG = galleryArea.GetComponent<Renderer>();
            if (rendG != null) { galleryBounds = rendG.bounds; hasGalleryBounds = true; }
            else
            {
                var bc = galleryArea.GetComponent<BoxCollider>();
                if (bc != null) { galleryBounds = new Bounds(galleryArea.transform.position + bc.center, bc.size); hasGalleryBounds = true; }
            }
        }

        if (hasGalleryBounds)
        {
            float x = galleryBounds.center.x;
            float yTop = galleryBounds.max.y;
            float z = galleryBounds.center.z;
            targetWorldPos = new Vector3(x, yTop - (savedCount * gallerySpacingY), z);
        }
        else if (galleryParent != null)
        {
            Vector3 worldStart = galleryParent.TransformPoint(galleryStartLocalPos);
            targetWorldPos = worldStart + Vector3.down * (savedCount * gallerySpacingY);
        }
        else
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 screenPos = new Vector3(50, cam.pixelHeight - 50 - savedCount * (gallerySpacingY * 100f), 10f);
                targetWorldPos = cam.ScreenToWorldPoint(screenPos);
            }
            else targetWorldPos = Vector3.left * 3f + Vector3.up * (2f - savedCount * gallerySpacingY);
        }

        Vector3 anchorOriginal;
        Quaternion anchorRotation;
        if (moveArea != null)
        {
            var moveR = moveArea.GetComponent<Renderer>();
            if (moveR != null) anchorOriginal = moveR.bounds.center;
            else
            {
                var bc = moveArea.GetComponent<BoxCollider>();
                anchorOriginal = (bc != null) ? (moveArea.transform.position + bc.center) : moveArea.transform.position;
            }
            anchorRotation = moveArea.transform.rotation;
        }
        else
        {
            anchorOriginal = GetWorldBoundsCenter(glyphRoot);
            anchorRotation = Quaternion.identity;
        }

        GameObject savedGroup = new GameObject("SavedGroup_" + savedCount);
        savedGroup.isStatic = false;
        if (galleryParent != null) savedGroup.transform.SetParent(galleryParent, true);

        Vector3 nudge = Camera.main ? -Camera.main.transform.forward.normalized * galleryStrokeZOffset
                                    : Vector3.forward * galleryStrokeZOffset;

        savedGroup.transform.SetPositionAndRotation(anchorOriginal + nudge, anchorRotation);
        savedGroup.transform.localScale = Vector3.one;

        GameObject contentRoot = new GameObject("ContentRoot");
        contentRoot.transform.SetParent(savedGroup.transform, false);
        contentRoot.transform.localPosition = Vector3.zero;
        contentRoot.transform.localRotation = Quaternion.identity;
        contentRoot.transform.localScale = Vector3.one;

        CloneMoveAreaVisual(contentRoot.transform);

        GameObject glyphClone = Instantiate(glyphRoot);
        glyphClone.name = $"SavedGlyph_{savedCount}";
        glyphClone.isStatic = false;

        Destroy(glyphRoot);
        glyphClone.transform.SetParent(contentRoot.transform, true);

        ConvertLineRenderersToLocalAndScale(glyphClone, gallerySavedScale, scaleLineWidthInGallery);

        contentRoot.transform.localScale = Vector3.one * gallerySavedScale;

        savedGroup.transform.position = targetWorldPos + nudge;

        savedGroups.Add(savedGroup);
        savedCount++;
        SelectGalleryIndex(savedCount - 1);
    }

    // ★ 追加：白紙（何も書かれていない）をギャラリーに1行として保存
    void SaveBlankToGallery()
    {
        Vector3 targetWorldPos;
        bool hasGalleryBounds = false;
        Bounds galleryBounds = new Bounds();

        if (galleryArea != null)
        {
            var rendG = galleryArea.GetComponent<Renderer>();
            if (rendG != null) { galleryBounds = rendG.bounds; hasGalleryBounds = true; }
            else
            {
                var bc = galleryArea.GetComponent<BoxCollider>();
                if (bc != null) { galleryBounds = new Bounds(galleryArea.transform.position + bc.center, bc.size); hasGalleryBounds = true; }
            }
        }

        if (hasGalleryBounds)
        {
            float x = galleryBounds.center.x;
            float yTop = galleryBounds.max.y;
            float z = galleryBounds.center.z;
            targetWorldPos = new Vector3(x, yTop - (savedCount * gallerySpacingY), z);
        }
        else if (galleryParent != null)
        {
            Vector3 worldStart = galleryParent.TransformPoint(galleryStartLocalPos);
            targetWorldPos = worldStart + Vector3.down * (savedCount * gallerySpacingY);
        }
        else
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 screenPos = new Vector3(50, cam.pixelHeight - 50 - savedCount * (gallerySpacingY * 100f), 10f);
                targetWorldPos = cam.ScreenToWorldPoint(screenPos);
            }
            else targetWorldPos = Vector3.left * 3f + Vector3.up * (2f - savedCount * gallerySpacingY);
        }

        Vector3 anchorOriginal;
        Quaternion anchorRotation;
        if (moveArea != null)
        {
            var moveR = moveArea.GetComponent<Renderer>();
            if (moveR != null) anchorOriginal = moveR.bounds.center;
            else
            {
                var bc = moveArea.GetComponent<BoxCollider>();
                anchorOriginal = (bc != null) ? (moveArea.transform.position + bc.center) : moveArea.transform.position;
            }
            anchorRotation = moveArea.transform.rotation;
        }
        else
        {
            anchorOriginal = targetWorldPos;
            anchorRotation = Quaternion.identity;
        }

        GameObject savedGroup = new GameObject("SavedGroup_" + savedCount);
        savedGroup.isStatic = false;
        if (galleryParent != null) savedGroup.transform.SetParent(galleryParent, true);

        Vector3 nudge = Camera.main ? -Camera.main.transform.forward.normalized * galleryStrokeZOffset
                                    : Vector3.forward * galleryStrokeZOffset;

        savedGroup.transform.SetPositionAndRotation(anchorOriginal + nudge, anchorRotation);
        savedGroup.transform.localScale = Vector3.one;

        GameObject contentRoot = new GameObject("ContentRoot");
        contentRoot.transform.SetParent(savedGroup.transform, false);
        contentRoot.transform.localPosition = Vector3.zero;
        contentRoot.transform.localRotation = Quaternion.identity;
        contentRoot.transform.localScale = Vector3.one;

        // MoveArea の見た目だけクローン（白紙）
        CloneMoveAreaVisual(contentRoot.transform);

        // ギャラリー用スケール
        contentRoot.transform.localScale = Vector3.one * gallerySavedScale;

        // 行の位置に移動
        savedGroup.transform.position = targetWorldPos + nudge;

        savedGroups.Add(savedGroup);
        savedCount++;
        SelectGalleryIndex(savedCount - 1);
    }

    void TriggerCompletionAnimations()
    {
        _waitAnims.Clear();
        _waitStateHash.Clear();

        TryPlayAnimator(galleryAnimator, galleryAnimTrigger, galleryAnimStateName);
        TryPlayAnimator(rightPanelAnimator, rightPanelAnimTrigger, rightPanelAnimStateName);
        TryPlayAnimator(moveAreaAnimator, moveAreaAnimTrigger, moveAreaAnimStateName);
    }

    void TryPlayAnimator(Animator anim, string triggerName, string stateName)
    {
        if (anim == null) return;

        if (!anim.enabled) anim.enabled = true;
        anim.Update(0f);

        if (!string.IsNullOrEmpty(stateName))
        {
            anim.Play(stateName, 0, 0f);
            _waitAnims.Add(anim);
            _waitStateHash[anim] = Animator.StringToHash(stateName);
        }
        else if (!string.IsNullOrEmpty(triggerName) && HasAnimatorParameter(anim, triggerName, AnimatorControllerParameterType.Trigger))
        {
            anim.ResetTrigger(triggerName);
            anim.SetTrigger(triggerName);
            _waitAnims.Add(anim);
            _waitStateHash[anim] = -1;
        }
        else if (anim.runtimeAnimatorController != null)
        {
            anim.Rebind();
            anim.Update(0f);
            _waitAnims.Add(anim);
            _waitStateHash[anim] = -1;
        }
    }

    IEnumerator WaitAnimationsThenAwaitResultKey()
    {
        if (_waitAnims.Count == 0) yield return null;
        yield return null;

        float start = Time.time;
        var entered = new Dictionary<Animator, bool>();
        var done = new Dictionary<Animator, bool>();
        foreach (var a in _waitAnims)
        {
            if (a == null) continue;
            entered[a] = (_waitStateHash[a] == -1);
            done[a] = false;
        }

        while (Time.time - start < 8f)
        {
            bool allDone = true;

            foreach (var a in _waitAnims)
            {
                if (a == null) continue;
                if (done[a]) continue;
                if (!a.enabled || a.runtimeAnimatorController == null) { done[a] = true; continue; }

                int layer = 0;
                if (a.IsInTransition(layer)) { allDone = false; continue; }

                var info = a.GetCurrentAnimatorStateInfo(layer);
                int expect = _waitStateHash[a];

                if (expect != -1)
                {
                    if (!entered[a] && info.shortNameHash == expect) entered[a] = true;

                    if (entered[a])
                    {
                        if (!info.loop && info.shortNameHash == expect && info.normalizedTime >= 1f) done[a] = true;
                        else if (info.shortNameHash != expect) done[a] = true;
                        else allDone = false;
                    }
                    else allDone = false;
                }
                else
                {
                    if (!info.loop && info.normalizedTime >= 1f) done[a] = true;
                    else allDone = false;
                }
            }

            if (allDone) break;
            yield return null;
        }

        // 最終ポーズで固定＆アニメ停止
        foreach (var a in _waitAnims) { if (a != null) { a.Update(0f); a.enabled = false; } }

        _readyForResult = true;
        yield break;
    }

    void OnResultSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (GlobalData.SceneTransferRoot != null)
            GlobalData.SceneTransferRoot.SetActive(true);

        SceneManager.sceneLoaded -= OnResultSceneLoaded;
    }

    void LoadResultScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return;
        BuildResultTransferPayload();
        SceneManager.sceneLoaded += OnResultSceneLoaded;
        SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
    }

    void BuildResultTransferPayload()
    {
        if (GlobalData.SceneTransferRoot != null)
        {
            UObject.Destroy(GlobalData.SceneTransferRoot);
            GlobalData.SceneTransferRoot = null;
        }

        var root = new GameObject("ResultTransferRoot");
        GlobalData.SceneTransferRoot = root;
        UObject.DontDestroyOnLoad(root);

        GlobalData.ResultPromptWord = string.IsNullOrEmpty(currentPromptWord) ? GlobalData.PromptWord : currentPromptWord;

        foreach (var g in savedGroups)
        {
            if (g == null) continue;
            var copy = UObject.Instantiate(g);
            copy.name = g.name;
            copy.transform.SetParent(root.transform, true);
        }

        root.SetActive(false);
    }

    // ====== タイマー補助 ======
    void ResetCharTimer()
    {
        if (!enablePerCharTimeLimit)
        {
            charTimerSec = 0f;
            UpdateTimerUI();
            return;
        }
        // 前の文字の残り秒をボーナスとして加算
        charTimerSec = Mathf.Max(0.01f, perCharSeconds + Mathf.Max(0f, _nextBonusSeconds));
        _nextBonusSeconds = 0f;
        UpdateTimerUI();
    }

    void UpdateTimerUI()
    {
        if (timerText == null) return;
        if (status == Status.READY_TO_WRITE && enablePerCharTimeLimit)
        {
            float sec = Mathf.Max(0f, charTimerSec);
            timerText.text = $" {Mathf.CeilToInt(sec)} ";
        }
        else
        {
            timerText.text = "";
        }
    }

    void PlaceTimerToTopCenter()
    {
        if (timerText == null) return;
        var rt = timerText.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -Mathf.Abs(timerTopMargin));
        timerText.alignment = TextAlignmentOptions.Center;
        timerText.raycastTarget = false;
    }

    // ====== カウントダウン補助 ======
    void PlaceCountdownToCenter()
    {
        if (countdownText == null) return;
        var rt = countdownText.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = countdownOffset;
        countdownText.alignment = TextAlignmentOptions.Center;
        countdownText.raycastTarget = false;
    }

    IEnumerator CoCountdownThenStart()
    {
        // 念のためタイマー UI を消しておく
        UpdateTimerUI();

        if (countdownText != null)
        {
            if (autoPlaceCountdownAtCenter) PlaceCountdownToCenter();
            countdownText.gameObject.SetActive(true);
        }

        int start = Mathf.Max(1, countdownFrom);

        for (int n = start; n >= 1; n--)
        {
            if (countdownText != null) countdownText.text = n.ToString();

            if (sfxSource != null)
            {
                if (n == 3 && sfxThree != null) sfxSource.PlayOneShot(sfxThree);
                else if (n == 2 && sfxTwo != null) sfxSource.PlayOneShot(sfxTwo);
                else if (n == 1 && sfxOne != null) sfxSource.PlayOneShot(sfxOne);
            }

            yield return new WaitForSeconds(1f);
        }

        // 「始め！」表示＆SFX
        if (countdownText != null) countdownText.text = "始め！";
        float hold = Mathf.Max(0.1f, startWordHoldSeconds);
        float startSfxLen = 0f;

        if (sfxSource != null && sfxStart != null)
        {
            sfxSource.PlayOneShot(sfxStart);
            startSfxLen = sfxStart.length;
        }

        // 「始め！」の表示は、SFXの長さと hold の長い方に合わせる
        yield return new WaitForSeconds(Mathf.Max(hold, startSfxLen));

        if (countdownText != null) countdownText.text = "";

        if (bgmSource != null)
        {
            bgmSource.Play();
        }

        status = Status.READY_TO_WRITE;
        _nextBonusSeconds = 0f;     // 1文字目なのでボーナスは無し
        ResetCharTimer();
        ShowMessage("");
    }
}
