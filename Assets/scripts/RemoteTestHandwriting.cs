using UnityEngine;

// Hand-authored centre-line samples, in the same XY space as the tablet sender.
public static class RemoteTestHandwriting
{
    public const string Word = "あいう";

    public static RemoteTemplateData Create()
    {
        var data = new RemoteTemplateData {
            promptWord = Word, isLocalTest = true,
            sourceHalfSize = new Vector2(1.92f, 1.08f), lineWidth = 0.03f
        };
        // あ: horizontal, vertical, then the crossing loop.
        Add(data, 0, 0, -.70f,.46f, -.28f,.47f, .20f,.53f, .60f,.60f);
        Add(data, 0, 1, -.08f,.86f, -.17f,.46f, -.18f,.03f, -.12f,-.42f, .02f,-.68f);
        Add(data, 0, 2, .40f,.36f, .27f,.02f, .02f,-.36f, -.38f,-.65f,
            -.66f,-.58f, -.72f,-.32f, -.48f,-.04f, -.10f,.10f,
            .34f,.06f, .64f,-.15f, .64f,-.48f, .38f,-.72f);
        // い: left stroke with a hook, then the shorter right stroke.
        Add(data, 1, 0, -.58f,.68f, -.64f,.29f, -.63f,-.14f, -.50f,-.53f, -.33f,-.58f, -.13f,-.30f);
        Add(data, 1, 1, .30f,.50f, .51f,.27f, .65f,-.03f, .69f,-.38f);
        // う: upper mark, then the curved main stroke.
        Add(data, 2, 0, -.25f,.85f, .02f,.76f, .32f,.69f);
        Add(data, 2, 1, -.65f,.22f, -.26f,.38f, .19f,.42f, .50f,.25f,
            .51f,-.08f, .36f,-.44f, .10f,-.69f, -.25f,-.85f);
        return data;
    }

    static void Add(RemoteTemplateData data, int character, int index, params float[] xy)
    {
        var stroke = new RemoteTemplateData.Stroke { character = character, stroke = index };
        int count = xy.Length / 2;
        for (int segment = 0; segment < count - 1; segment++)
        {
            Vector3 p0 = Point(xy, Mathf.Max(0, segment - 1));
            Vector3 p1 = Point(xy, segment);
            Vector3 p2 = Point(xy, segment + 1);
            Vector3 p3 = Point(xy, Mathf.Min(count - 1, segment + 2));
            for (int step = 0; step < 8; step++)
            {
                float t = step / 8f;
                stroke.points.Add(.5f * ((2f * p1) + (-p0 + p2) * t
                    + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t
                    + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t));
            }
        }
        stroke.points.Add(Point(xy, count - 1));
        data.strokes.Add(stroke);
    }

    static Vector3 Point(float[] xy, int index) => new Vector3(xy[index * 2], xy[index * 2 + 1], 0f);
}
