// RecvBB.cs
// 外部サーバー(127.0.0.1:9001)から
//  - "BB,name,ms,tl,tr,bl,br"
//  - "WM,name,ms,A,B,One,Two,Plus,Minus,Home,Up,Down,Left,Right"
// の行を同一ソケットで受信してパースする統合クライアント。
// PreGameControl 互換API（Connected/Connect/Disconnect/Clear/Count/Dequeue）も提供。

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public class RecvBB : IDisposable
{
    // ===== Balance Board datum =====
    public class BBDatum
    {
        public const float W = 43.0f;  // cm
        public const float H = 23.5f;  // cm

        public long time;      // ms
        public string name;    // "BB-1" など
        public float tl_kg;
        public float tr_kg;
        public float bl_kg;
        public float br_kg;

        public float total4(float[] offset)
        {
            if (offset == null || offset.Length != 4) offset = new float[4];
            return (tl_kg - offset[0]) + (tr_kg - offset[1]) + (bl_kg - offset[2]) + (br_kg - offset[3]);
        }

        /// <summary>重心(cm)。原点=中央、+X=右、+Y=前(top側)。</summary>
        public float[] getCoG(float[] offset)
        {
            if (offset == null || offset.Length != 4) offset = new float[4];
            float tl = tl_kg - offset[0];
            float tr = tr_kg - offset[1];
            float bl = bl_kg - offset[2];
            float br = br_kg - offset[3];
            float sum = tl + tr + bl + br;
            if (sum <= 1e-6f) return new float[] { 0f, 0f };
            float x = ((tr + br) - (tl + bl)) / sum * (W / 2f);
            float y = ((tl + tr) - (bl + br)) / sum * (H / 2f);
            return new float[] { x, y };
        }
    }

    // ===== Wiimote buttons =====
    public struct WMButtons
    {
        public string name; public long time;
        public bool A, B, One, Two, Plus, Minus, Home, Up, Down, Left, Right;
    }

    public event Action<WMButtons> OnWM;

    // 最新ボタン状態（デバイス名ごと）
    private readonly ConcurrentDictionary<string, WMButtons> latestWMByName =
        new ConcurrentDictionary<string, WMButtons>();
    public bool TryGetLatestWM(string name, out WMButtons b)
        => latestWMByName.TryGetValue(name, out b);

    // ===== Socket & queues =====
    private string host;
    private int port;
    private TcpClient client;
    private Thread thread;
    private volatile bool running;

    private readonly ConcurrentQueue<BBDatum> queue = new ConcurrentQueue<BBDatum>();
    private BBDatum latest = null;

    public RecvBB(string host = "127.0.0.1", int port = 9001)
    {
        this.host = host; this.port = port;
    }

    public void Start()
    {
        if (running) return;
        running = true;
        thread = new Thread(Loop) { IsBackground = true };
        thread.Start();
    }

    public void Stop()
    {
        running = false;
        try { client?.Close(); } catch { }
        client = null;
    }

    public void Dispose() => Stop();

    /// 最新1件（BB）取得
    public bool GetLatest(out BBDatum d) { d = latest; return d != null; }

    /// 受信キューをすべて取り出す
    public int DrainTo(List<BBDatum> dst)
    {
        int n = 0;
        while (queue.TryDequeue(out var d)) { dst.Add(d); n++; }
        return n;
    }

    private void Loop()
    {
        while (running)
        {
            try
            {
                if (client == null || !client.Connected)
                {
                    client = new TcpClient();
                    client.NoDelay = true;
                    client.Connect(host, port);
                }

                using (var ns = client.GetStream())
                using (var sr = new StreamReader(ns, Encoding.UTF8))
                {
                    while (running && client.Connected)
                    {
                        string line = sr.ReadLine();
                        if (line == null) break;

                        // --- BB 行 ---
                        if (line.StartsWith("BB,"))
                        {
                            var sp = line.Split(',');
                            if (sp.Length >= 7)
                            {
                                var d = new BBDatum
                                {
                                    name = sp[1].Trim(),
                                    time = SafeLong(sp[2]),
                                    tl_kg = SafeFloat(sp[3]),
                                    tr_kg = SafeFloat(sp[4]),
                                    bl_kg = SafeFloat(sp[5]),
                                    br_kg = SafeFloat(sp[6]),
                                };
                                latest = d;
                                queue.Enqueue(d);
                            }
                            continue;
                        }

                        // --- WM 行 ---
                        if (line.StartsWith("WM,"))
                        {
                            var sp = line.Split(',');
                            // 期待: WM,name,ms,A,B,One,Two,Plus,Minus,Home,Up,Down,Left,Right
                            if (sp.Length >= 14)
                            {
                                var b = new WMButtons
                                {
                                    name = sp[1].Trim(),
                                    time = SafeLong(sp[2]),
                                    A = sp[3] == "1",
                                    B = sp[4] == "1",
                                    One = sp[5] == "1",
                                    Two = sp[6] == "1",
                                    Plus = sp[7] == "1",
                                    Minus = sp[8] == "1",
                                    Home = sp[9] == "1",
                                    Up = sp[10] == "1",
                                    Down = sp[11] == "1",
                                    Left = sp[12] == "1",
                                    Right = sp[13] == "1",
                                };
                                latestWMByName[b.name] = b;
                                OnWM?.Invoke(b);
                            }
                            continue;
                        }

                        // それ以外の行は無視
                    }
                }
            }
            catch
            {
                Thread.Sleep(500); // 再接続リトライ
            }
        }
    }

    private static float SafeFloat(string s)
    {
        if (float.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v)) return v;
        if (float.TryParse(s, out v)) return v;
        return 0f;
    }
    private static long SafeLong(string s) => long.TryParse(s, out var v) ? v : 0L;

    // ===== PreGameControl 互換API =====
    public bool Connected()
    {
        try { return client != null && client.Connected; }
        catch { return false; }
    }
    public void Connect(string host = "127.0.0.1", int port = 9001)
    {
        this.host = host; this.port = port; Start();
    }
    public void Disconnect() => Stop();

    public void Clear()
    {
        while (queue.TryDequeue(out _)) { }
        latest = null;
        // WM側は最新を保持したままでOK（必要なら here で latestWMByName.Clear()）
    }
    public int Count() => queue.Count;
    public BBDatum Dequeue()
    {
        if (queue.TryDequeue(out var d)) { latest = d; return d; }
        return null;
    }
}
