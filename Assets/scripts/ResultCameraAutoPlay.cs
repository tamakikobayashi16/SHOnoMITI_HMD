// ResultCameraAutoPlay.cs
using UnityEngine;

[DefaultExecutionOrder(100)] // 大抵の初期化の後に実行
public class ResultCameraAutoPlay : MonoBehaviour
{
    [Header("Camera / First Animator")]
    [Tooltip("再生に使う Animator（未指定ならこのGameObjectから自動取得）")]
    public Animator targetAnimator;

    [Header("How to Play (1st)")]
    [Tooltip("優先的に使う Trigger 名（空なら未使用）")]
    public string triggerName = "Play";

    [Tooltip("Trigger が無い/未設定の時に直接再生する Animator State 名（空なら未使用）")]
    public string stateName = "";

    [Tooltip("Start で Animator を有効化して即再生できる状態にします")]
    public bool enableAnimatorOnStart = true;

    [Tooltip("Trigger/State が使えなかった時のフォールバック: Rebind で先頭へ戻す")]
    public bool fallbackRebind = true;

    [Header("Detect End of Camera Animation")]
    [Tooltip("終了判定に使う監視対象ステート名（空なら“現在のステート”で判定）")]
    public string watchStateName = "";
    [Tooltip("監視するレイヤー index")]
    public int watchLayer = 0;
    [Tooltip("normalizedTime がこの値以上で終了とみなす（非ループ前提）")]
    [Range(0.5f, 1.5f)] public float endNormalizedTime = 1.0f;
    [Tooltip("ステートが Loop の場合は終了とみなさない（通常はONのまま）")]
    public bool requireNonLoopState = true;
    [Tooltip("終了時に1つ目のAnimatorを停止する")]
    public bool disableFirstAnimatorOnEnd = true;

    [Header("Chain / Next Animation")]
    [Tooltip("次に再生したい Animator（“表示したいオブジェクト”側の Animator）")]
    public Animator nextAnimator;
    [Tooltip("次のAnimatorの再生に使う Trigger（空なら未使用）")]
    public string nextTriggerName = "Show";
    [Tooltip("次のAnimatorの直接再生する State 名（Triggerが無い場合に使用）")]
    public string nextStateName = "";
    [Tooltip("次を再生するまでの待ち時間（秒）")]
    public float nextStartDelay = 0.0f;

    [Header("Reveal Object")]
    [Tooltip("表示したいオブジェクト（任意）")]
    public GameObject revealObject;
    [Tooltip("開始時にオブジェクトを自動で非表示にする")]
    public bool hideRevealObjectOnStart = true;
    [Tooltip("次のAnimatorを再生する直前にオブジェクトを表示する")]
    public bool activateObjectWhenNextStarts = true;

    // ==== 追加：カメラアニメ終了後に流すBGM ====
    [Header("BGM After Camera Animation")]
    [Tooltip("カメラのアニメーション終了後に再生するBGM用 AudioSource（PlayOnAwakeはOFF推奨）")]
    public AudioSource bgmAfterCameraSource;
    [Tooltip("終了検知からBGMを流すまでのディレイ（秒）")]
    public float bgmStartDelay = 0f;

    // --- internal ---
    Animator _anim;
    int _watchHash = -1;
    bool _enteredWatched = false;
    bool _firedNext = false;
    bool _startedBgm = false; // BGM二重再生防止

    void Reset()
    {
        targetAnimator = GetComponent<Animator>();
    }

    void Start()
    {
        _anim = targetAnimator != null ? targetAnimator : GetComponent<Animator>();
        if (_anim == null)
        {
            Debug.LogWarning("[ResultCameraAutoPlay] Animator が見つかりません。このコンポーネントを外すか、Animator を追加してください。");
            return;
        }

        if (hideRevealObjectOnStart && revealObject != null)
            revealObject.SetActive(false);

        if (enableAnimatorOnStart && !_anim.enabled) _anim.enabled = true;
        _anim.Update(0f); // 初期化を確実に反映

        bool played = false;

        // 1) Trigger 優先
        if (!string.IsNullOrEmpty(triggerName) && HasParam(_anim, triggerName, AnimatorControllerParameterType.Trigger))
        {
            _anim.ResetTrigger(triggerName);
            _anim.SetTrigger(triggerName);
            played = true;
        }

        // 2) State 直接再生
        if (!played && !string.IsNullOrEmpty(stateName))
        {
            _anim.Play(stateName, watchLayer, 0f);
            played = true;
        }

        // 3) フォールバック：先頭へ（Controller があれば）
        if (!played && fallbackRebind && _anim.runtimeAnimatorController != null)
        {
            _anim.Rebind();
            _anim.Update(0f);
        }

        // 監視対象ステートのハッシュを用意
        if (!string.IsNullOrEmpty(watchStateName))
        {
            _watchHash = Animator.StringToHash(watchStateName);
            _enteredWatched = false;
        }
    }

    void Update()
    {
        if (_anim == null || _firedNext) return;

        if (_anim.runtimeAnimatorController == null || !_anim.enabled)
        {
            // 何らかの理由で無効になっていれば“終わった”と見なす
            FireNext();
            return;
        }

        if (_anim.IsInTransition(watchLayer)) return; // 遷移中は判定しない

        var info = _anim.GetCurrentAnimatorStateInfo(watchLayer);

        // 監視対象ステートに入ったかのフラグ（指定がある場合のみ）
        if (_watchHash != -1)
        {
            if (!_enteredWatched && info.shortNameHash == _watchHash) _enteredWatched = true;
            if (!_enteredWatched) return; // まだ監視ステートに入っていない
        }

        // ループ状態は終了とみなさない（設定次第）
        if (requireNonLoopState && info.loop) return;

        // normalizedTime による終了判定
        if (info.normalizedTime >= endNormalizedTime)
        {
            FireNext();
        }
    }

    void FireNext()
    {
        if (_firedNext) return;
        _firedNext = true;

        if (disableFirstAnimatorOnEnd && _anim != null)
        {
            _anim.Update(0f); // 最終ポーズへ
            _anim.enabled = false;
        }

        // ★ 追加：カメラアニメ終了後にBGM再生
        if (!_startedBgm && bgmAfterCameraSource != null)
        {
            _startedBgm = true;
            StartCoroutine(PlayBgmAfterDelay());
        }

        if (nextAnimator == null && revealObject == null)
            return; // 何もすることが無い

        StartCoroutine(PlayNextAfterDelay());
    }

    System.Collections.IEnumerator PlayNextAfterDelay()
    {
        if (nextStartDelay > 0f) yield return new WaitForSeconds(nextStartDelay);

        if (activateObjectWhenNextStarts && revealObject != null)
            revealObject.SetActive(true);

        if (nextAnimator != null)
        {
            if (!nextAnimator.enabled) nextAnimator.enabled = true;
            nextAnimator.Update(0f);

            bool played = false;
            if (!string.IsNullOrEmpty(nextTriggerName) && HasParam(nextAnimator, nextTriggerName, AnimatorControllerParameterType.Trigger))
            {
                nextAnimator.ResetTrigger(nextTriggerName);
                nextAnimator.SetTrigger(nextTriggerName);
                played = true;
            }

            if (!played && !string.IsNullOrEmpty(nextStateName))
            {
                nextAnimator.Play(nextStateName, 0, 0f);
                played = true;
            }

            if (!played && nextAnimator.runtimeAnimatorController != null)
            {
                nextAnimator.Rebind();
                nextAnimator.Update(0f);
            }
        }
    }

    // ★ 追加：BGM再生コルーチン
    System.Collections.IEnumerator PlayBgmAfterDelay()
    {
        if (bgmStartDelay > 0f) yield return new WaitForSeconds(bgmStartDelay);
        if (bgmAfterCameraSource != null && !bgmAfterCameraSource.isPlaying)
        {
            bgmAfterCameraSource.Play();
        }
    }

    bool HasParam(Animator animator, string name, AnimatorControllerParameterType type)
    {
        foreach (var p in animator.parameters)
            if (p.name == name && p.type == type) return true;
        return false;
    }
}
