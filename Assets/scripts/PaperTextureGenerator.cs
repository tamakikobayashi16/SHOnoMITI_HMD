using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public class PaperTextureGenerator : MonoBehaviour
{
    [Header("Paper")]
    [SerializeField, Tooltip("テクスチャサイズ（正方形）")] private int size = 1024;
    [SerializeField, Tooltip("ノイズ拡大率（小さいほど細かい）")] private float noiseScale = 3.5f;
    [SerializeField, Range(0f, 1f), Tooltip("陰影の強さ")] private float noiseIntensity = 0.10f;
    [SerializeField, Range(0f, 1f), Tooltip("繊維の強さ")] private float fibreIntensity = 0.07f;
    [SerializeField, Tooltip("ベース色（和紙の地色）")] private Color baseColor = new Color(0.97f, 0.965f, 0.94f);

    [Header("Runtime")]
    [SerializeField] private bool regenerateOnEnable = true;

    private Image targetImage;
    private Texture2D tex;

    private void Awake()
    {
        targetImage = GetComponent<Image>();
    }

    private void OnEnable()
    {
        if (regenerateOnEnable) Apply();
    }

    [ContextMenu("Generate & Apply")]
    public void Apply()
    {
        if (tex != null)
        {
            Destroy(tex);
            tex = null;
        }

        tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        float seed = Random.Range(0f, 10000f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x + 0.5f) / size;
                float ny = (y + 0.5f) / size;

                // ベースのPerlinノイズ（陰影）
                float n1 = Mathf.PerlinNoise((nx + seed) * noiseScale, (ny + seed) * noiseScale);

                // 斜め繊維（薄い線状のテクスチャ）
                float angle = 0.35f; // ランダムでもOK
                float rx = nx * Mathf.Cos(angle) - ny * Mathf.Sin(angle);
                float ry = nx * Mathf.Sin(angle) + ny * Mathf.Cos(angle);
                float fibre = Mathf.PerlinNoise((rx + seed * 0.37f) * noiseScale * 4f, (ry + seed * 0.51f) * noiseScale * 0.8f);

                float shade = 1f - noiseIntensity * (n1 - 0.5f) * 2f
                                 - fibreIntensity * (fibre - 0.5f) * 2f;

                Color c = baseColor * shade;
                c.a = 1f;
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply(false, false);

        // Image に適用
        if (targetImage != null)
        {
            var spr = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            targetImage.sprite = spr;
            targetImage.type = Image.Type.Simple;
            targetImage.preserveAspect = true;
        }
    }

    private void OnDestroy()
    {
        if (tex != null) Destroy(tex);
    }
}
