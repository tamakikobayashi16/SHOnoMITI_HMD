using System.Collections.Generic;
using UnityEngine;
using TMPro;

public static class OverlayIoUScorer
{
    // ========= 公開API =========
    // savedGroups: PreGame で保存している「書いた文字」(SavedGroup_*)
    // rightSlots : 右側お手本スロット（背景Prefabを並べたもの）
    // 返り値     : 各インデックスのスコア(0-100)。min(左,右)件ぶん
    public static List<int> ScoreAll(
        List<GameObject> savedGroups,
        List<GameObject> rightSlots,
        int resolution = 512,
        float alphaThreshold = 0.05f,
        float paddingRatio = 0.10f)
    {
        var scores = new List<int>();
        if (savedGroups == null || rightSlots == null) return scores;

        int count = Mathf.Min(savedGroups.Count, rightSlots.Count);
        if (count <= 0) return scores;

        // 一時カメラ
        var camGO = new GameObject("ScoringCamera_Temp");
        var cam = camGO.AddComponent<Camera>();
        cam.enabled = false;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.orthographic = true;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 100f;
        cam.depth = 1000;

        // レンダーテクスチャと読み出し用テクスチャ
        var rt = new RenderTexture(resolution, resolution, 16, RenderTextureFormat.ARGB32);
        var tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);

        // 一時レイヤー（30番）に退避して他の描画物を遮断
        int tempLayer = 30;
        int layerMask = 1 << tempLayer;
        cam.cullingMask = layerMask;

        for (int i = 0; i < count; i++)
        {
            var writtenRoot = savedGroups[i];
            var slotRoot = rightSlots[i];
            if (writtenRoot == null || slotRoot == null)
            {
                scores.Add(0);
                continue;
            }

            // 関連Renderer取得
            var writtenStrokeRenderers = GetStrokeRenderers(writtenRoot);   // LineRenderer だけ
            var slotTextRenderers = GetTMPTextRenderers(slotRoot);     // TMP_Text だけ

            if (writtenStrokeRenderers.Count == 0 || slotTextRenderers.Count == 0)
            {
                // どちらか無ければ 0 点
                scores.Add(0);
                continue;
            }

            // レイヤー退避＆セット
            var writtenLayerBackup = PushLayerRecursively(writtenRoot, tempLayer);
            var slotLayerBackup = PushLayerRecursively(slotRoot, tempLayer);

            // 対象外Rendererは一時的に無効化
            var disableWritten = CollectNonTargets(writtenRoot, writtenStrokeRenderers);
            var disableSlot = CollectNonTargets(slotRoot, slotTextRenderers);

            // 2パス描画：書いた文字→お手本
            // ビューフラストラム（直交）を2者の結合Boundsから決定
            Bounds b = CalcCombinedBounds(writtenStrokeRenderers, slotTextRenderers);
            if (b.size == Vector3.zero) { scores.Add(0); RestoreAll(); continue; }

            float pad = Mathf.Max(Mathf.Max(b.size.x, b.size.y) * paddingRatio, 0.001f);
            float orthoSize = (Mathf.Max(b.size.x, b.size.y) * 0.5f) + pad;

            cam.orthographicSize = orthoSize;
            cam.transform.position = new Vector3(b.center.x, b.center.y, b.center.z - 10f);
            cam.transform.rotation = Quaternion.identity;
            cam.targetTexture = rt;

            // --- パス1：書いた文字のみ可視化 ---
            SetEnabled(writtenStrokeRenderers, true);
            SetEnabled(slotTextRenderers, false);
            RenderToMask(cam, rt, tex, alphaThreshold, out var maskWritten);

            // --- パス2：お手本のみ可視化 ---
            SetEnabled(writtenStrokeRenderers, false);
            SetEnabled(slotTextRenderers, true);
            RenderToMask(cam, rt, tex, alphaThreshold, out var maskRef);

            // IoU
            int overlap = 0, union = 0;
            int len = maskWritten.Length;
            for (int p = 0; p < len; p++)
            {
                bool a = maskWritten[p];
                bool b2 = maskRef[p];
                if (a || b2) union++;
                if (a && b2) overlap++;
            }
            int score = (union > 0) ? Mathf.RoundToInt(100f * (overlap / (float)union)) : 0;
            scores.Add(Mathf.Clamp(score, 0, 100));

            // 復帰
            RestoreAll();

            // ローカル関数：復帰処理
            void RestoreAll()
            {
                // レンダラenable復帰
                foreach (var r in disableWritten) if (r != null) r.enabled = true;
                foreach (var r in disableSlot) if (r != null) r.enabled = true;

                // 主対象は一応有効化しておく
                SetEnabled(writtenStrokeRenderers, true);
                SetEnabled(slotTextRenderers, true);

                // レイヤー復帰
                PopLayerRecursively(writtenRoot, writtenLayerBackup);
                PopLayerRecursively(slotRoot, slotLayerBackup);
            }
        }

        // 破棄
        Object.Destroy(rt);
        Object.Destroy(tex);
        Object.Destroy(camGO);

        return scores;
    }

    // ========= 内部ユーティリティ =========
    static List<Renderer> GetStrokeRenderers(GameObject root)
    {
        var list = new List<Renderer>();
        if (root == null) return list;

        var lrs = root.GetComponentsInChildren<LineRenderer>(true);
        foreach (var lr in lrs)
        {
            if (lr != null && lr.positionCount > 0)
            {
                var r = lr.GetComponent<Renderer>();
                if (r != null) list.Add(r);
            }
        }
        return list;
    }

    static List<Renderer> GetTMPTextRenderers(GameObject root)
    {
        var list = new List<Renderer>();
        if (root == null) return list;

        var tmps = root.GetComponentsInChildren<TMP_Text>(true);
        foreach (var t in tmps)
        {
            if (t != null)
            {
                var r = t.GetComponent<Renderer>();
                if (r != null) list.Add(r);
            }
        }
        // フォールバック：TMP_Textがなければ何もしない（背景は除外したいので）
        return list;
    }

    static Bounds CalcCombinedBounds(List<Renderer> a, List<Renderer> b)
    {
        Bounds total = new Bounds(Vector3.zero, Vector3.zero);
        bool has = false;

        foreach (var r in a)
        {
            if (r == null) continue;
            if (!has) { total = r.bounds; has = true; }
            else total.Encapsulate(r.bounds);
        }
        foreach (var r in b)
        {
            if (r == null) continue;
            if (!has) { total = r.bounds; has = true; }
            else total.Encapsulate(r.bounds);
        }

        return has ? total : new Bounds(Vector3.zero, Vector3.zero);
    }

    static void RenderToMask(Camera cam, RenderTexture rt, Texture2D tex, float alphaThreshold, out bool[] mask)
    {
        RenderTexture.active = rt;
        cam.Render();
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
        tex.Apply(false, false);
        RenderTexture.active = null;

        var pixels = tex.GetPixels32();
        mask = new bool[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            // alpha か 明度 でしきい判定
            float a = pixels[i].a / 255f;
            if (a >= alphaThreshold)
            {
                mask[i] = true;
            }
            else
            {
                float luma = (0.299f * pixels[i].r + 0.587f * pixels[i].g + 0.114f * pixels[i].b) / 255f;
                mask[i] = (luma >= alphaThreshold);
            }
        }
    }

    static void SetEnabled(List<Renderer> renderers, bool enabled)
    {
        foreach (var r in renderers) if (r != null) r.enabled = enabled;
    }

    // root配下ですべてを tempLayer にし、元レイヤーを退避
    static Dictionary<Transform, int> PushLayerRecursively(GameObject root, int tempLayer)
    {
        var map = new Dictionary<Transform, int>();
        if (root == null) return map;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            map[t] = t.gameObject.layer;
            t.gameObject.layer = tempLayer;
        }
        return map;
    }
    static void PopLayerRecursively(GameObject root, Dictionary<Transform, int> backup)
    {
        if (root == null || backup == null) return;
        foreach (var kv in backup)
        {
            if (kv.Key != null) kv.Key.gameObject.layer = kv.Value;
        }
    }

    // 対象“以外”のRendererを列挙して一時的に不可視化（enabled=false）
    static List<Renderer> CollectNonTargets(GameObject root, List<Renderer> targets)
    {
        var list = new List<Renderer>();
        if (root == null) return list;

        var all = root.GetComponentsInChildren<Renderer>(true);
        var targetSet = new HashSet<Renderer>(targets);
        foreach (var r in all)
        {
            if (r == null) continue;
            if (!targetSet.Contains(r))
            {
                if (r.enabled)
                {
                    r.enabled = false;
                    list.Add(r);
                }
            }
        }
        return list;
    }
}
