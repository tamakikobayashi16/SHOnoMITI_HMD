using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System;

/// <summary>
/// HMD(顔)データ(Port:50000)と右手コントローラーデータ(Port:50001)を
/// それぞれUDPで受信し、最新の状態を保持するクラスです。
/// </summary>
public class HMDReceiver : MonoBehaviour
{
    [Header("Port Settings")]
    public int facePort = 50000;      // 顔（HMD回転）用
    public int handPort = 50001;      // 右手（座標・角度・筆圧）用

    [Header("Latest Face Data (50000)")]
    public Vector3 faceRotation;

    [Header("Latest Hand Data (50001)")]
    public Vector3 handPosition;
    public Vector3 handRotation;
    public float handPressure;
    public bool isWriting;

    private Thread faceThread;
    private Thread handThread;
    private bool isRunning = false;

    // スレッドセーフなデータ保持用
    private Vector3 nextFaceRot;
    private Vector3 nextHandPos;
    private Vector3 nextHandRot;
    private float nextHandPressure;
    private bool nextIsWriting;
    private bool hasNewFaceData = false;
    private bool hasNewHandData = false;
    private readonly object faceLock = new object();
    private readonly object handLock = new object();

    void Start()
    {
        isRunning = true;
        
        // 顔データ受信スレッド開始
        faceThread = new Thread(() => ReceiveLoop(facePort, ParseFaceData));
        faceThread.IsBackground = true;
        faceThread.Start();

        // 右手データ受信スレッド開始
        handThread = new Thread(() => ReceiveLoop(handPort, ParseHandData));
        handThread.IsBackground = true;
        handThread.Start();

        Debug.Log($"[HMDReceiver] Listening on ports: Face={facePort}, Hand={handPort}");
    }

    void Update()
    {
        // メインスレッドでデータを同期
        if (hasNewFaceData)
        {
            lock (faceLock)
            {
                faceRotation = nextFaceRot;
                hasNewFaceData = false;
            }
        }

        if (hasNewHandData)
        {
            lock (handLock)
            {
                handPosition = nextHandPos;
                handRotation = nextHandRot;
                handPressure = nextHandPressure;
                isWriting = nextIsWriting;
                hasNewHandData = false;
            }
        }
    }

    private void ReceiveLoop(int port, Action<string> parseAction)
    {
        UdpClient client = null;
        try
        {
            client = new UdpClient(port);
            client.Client.ReceiveTimeout = 1000;
            IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

            while (isRunning)
            {
                try
                {
                    byte[] data = client.Receive(ref remoteEP);
                    string msg = Encoding.UTF8.GetString(data);
                    parseAction(msg);
                }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode != SocketError.TimedOut && isRunning) 
                        Debug.LogWarning($"[HMDReceiver:{port}] Socket Error: {ex.Message}");
                }
                catch (Exception e)
                {
                    if (isRunning) Debug.LogWarning($"[HMDReceiver:{port}] Error: {e.Message}");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[HMDReceiver:{port}] Setup Error: {e.Message}");
        }
        finally
        {
            if (client != null)
            {
                client.Close();
            }
        }
    }

    private void ParseFaceData(string msg)
    {
        try
        {
            string[] parts = msg.Split(',');
            if (parts.Length >= 3)
            {
                lock (faceLock)
                {
                    nextFaceRot = new Vector3(
                        float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture)
                    );
                    hasNewFaceData = true;
                }
            }
        }
        catch { /* Parse Error */ }
    }

    private void ParseHandData(string msg)
    {
        try
        {
            string[] parts = msg.Split(',');
            if (parts.Length >= 9 && parts[0] == "POS")
            {
                lock (handLock)
                {
                    nextHandPos = new Vector3(
                        float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture)
                    );
                    nextHandRot = new Vector3(
                        float.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(parts[5], System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(parts[6], System.Globalization.CultureInfo.InvariantCulture)
                    );
                    nextHandPressure = float.Parse(parts[7], System.Globalization.CultureInfo.InvariantCulture);
                    nextIsWriting = parts[8] == "1";
                    hasNewHandData = true;
                }
            }
        }
        catch { /* Parse Error */ }
    }

    void OnDisable()
    {
        isRunning = false;
    }

    void OnDestroy()
    {
        isRunning = false;
        // スレッドの終了を待つ必要があればここで行う
    }
}
