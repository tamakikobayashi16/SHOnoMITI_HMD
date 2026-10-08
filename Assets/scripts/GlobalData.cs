using System.Collections.Generic;
using UnityEngine;

public static class GlobalData
{
    [System.Serializable]
    public class HeadId { public string id; public float[] rect; }

    [System.Serializable]
    public class PartRect { public string name; public float[] rect; }

    [System.Serializable]
    public class PartsId { public string id; public PartRect[] partsrects; }

    // 画像保存先（必要に応じて変更）
    public static string IMAGEFOLDER_PATH = null;

    // Wii バランスボードサーバー情報
    public static string BBSERVER_ADDR = "127.0.0.1";
    public static int BBSERVER_PORT = 9001;

    // API URL（ヘッドやパーツ画像）
    public static string HEADEXTRACT_URL = "http://133.55.226.112:9000/head";
    public static string HEADIMAGE_URLBASE = "http://133.55.226.112:9000/outfile1/";
    public static string PARTSEXTRACT_URL = "http://133.55.226.112:9000/parts";
    public static string PARTSIMAGE_URLBASE = "http://133.55.226.112:9000/outfile2/";

    // 画像IDやパーツ
    public static HeadId headimage_id;
    public static PartsId partsimage_id;
    public static Dictionary<string, byte[]> partspng;

    // Wii バランスボード接続オブジェクト
    public static RecvBB recv_bb;

    // キャリブレーション
    public static float[] CALIBRATION_OFFSETS = new float[4] { 0f, 0f, 0f, 0f };
    public static float[] bboffsets;

    // ★ 追加：名前入力（お題）とResult受け渡し
    // InputName で設定 → PreGame が参照
    public static string PromptWord = null;

    // PreGame で実際に出したお題（Result表示用に残す）
    public static string ResultPromptWord = null;

    // PreGame から Result へ渡す描画プレハブのコンテナ
    public static GameObject SceneTransferRoot = null;
}
