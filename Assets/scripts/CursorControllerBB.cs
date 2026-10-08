using UnityEngine;

public class CursorControllerBB : MonoBehaviour
{
    public RecvBB recvBB; // Inspectorで設定
    public float scale = 0.1f; // バランスボード座標をUnity座標に変換
    public float zPos = 0f;    // Z座標固定

    void Update()
    {
        if (recvBB != null && recvBB.Connected() && recvBB.Count() > 0)
        {
            var datum = recvBB.Dequeue();
            float[] offset = new float[4]; // ゼロ補正
            float[] pos = datum.getCoG(offset);

            // pos[0] = X軸, pos[1] = Y軸（単位: cm）
            Vector3 worldPos = new Vector3(pos[0] * scale, pos[1] * scale, zPos);

            transform.position = worldPos;
        }
    }
}
