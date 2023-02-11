using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using SCIP_library;
using UnityEngine;

public class Sensor : MonoBehaviour
{
    private const float TO_METER = 0.001f;

    [SerializeField] private string portName = "COM3";
    [SerializeField] private int baudrate = 115200;

    public int AngularResolution { get; private set; }
    public int MeasurableRangeMin { get; private set; }
    public int MeasurableRangeMax { get; private set; }

    public bool IsAvailable { get; private set; } = false;
    public List<float> Distances => distances;
    private List<float> distances = new ();
    public long TimeStamp => timeStamp;
    private long timeStamp = 0;

    private bool isOpen = false;
    private SerialPort urg;

    private object lookObj = new ();
    private CancellationTokenSource updateDataCanceler;

    void Start()
    {
        Open();

        updateDataCanceler = new CancellationTokenSource();
        UpdateDataOnThread().Forget();
    }

    private void OnDestroy()
    {
        updateDataCanceler.Cancel();
        Close();
    }

    void Update()
    {
        // if (!isOpen) return;
        //
        // UpdateData();

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            updateDataCanceler.Cancel();
        }
    }

    private void Open()
    {
        try
        {
            Debug.Log("Connect setting = Port name : " + portName + " Baudrate : " + baudrate);

            urg ??= new SerialPort(portName, baudrate);
            urg.NewLine = "\n\n";

            urg.Open();

            urg.Write(SCIP_Writer.SCIP2());
            Debug.Log($"SCIP2 echo back: {urg.ReadLine()}"); // ignore echo back
            
            (AngularResolution, MeasurableRangeMin, MeasurableRangeMax) = GetMeasurementParameters();

            urg.Write(SCIP_Writer.MD(MeasurableRangeMin, MeasurableRangeMax));
            Debug.Log($"MD(START, END) echo back: {urg.ReadLine()}"); // ignore echo back

            isOpen = true;
        }
        catch (Exception ex)
        {
            Debug.Log(ex.Message);
        }
    }

    private void Close()
    {
        if (urg == null) return;

        try
        {
            isOpen = false;
            urg.Write(SCIP_Writer.QT()); // stop measurement mode
            urg.ReadLine(); // ignore echo back
            urg.Close();
        }
        catch (Exception ex)
        {
            Debug.Log(ex.Message);
        }
    }
    
    /// <remarks>
    /// PPの仕様 : https://sourceforge.net/p/urgnetwork/wiki/scip_status_jp/
    /// </remarks>
    private Tuple<int, int, int> GetMeasurementParameters()
    {
        urg.Write(SCIP_Writer.PP());
        
        var pp = urg.ReadLine();
        var lines = pp.Split('\n');
        var data = lines
            .Select(line => line.Split(":")) // keyとvalueに分割
            .Where(a => a.Length == 2) // keyとvalueでない行は不要なので削除
            .ToDictionary(x => x[0], x => x[1].Split(";")[0]); // valueの;の後ろはチェックサムなので削除
        
        var info = $"センサ型式情報: {data["MODL"]}" + Environment.NewLine
                   + $"最小計測可能距離 (mm): {data["DMIN"]}" + Environment.NewLine
                   + $"最大計測可能距離 (mm): {data["DMAX"]}" + Environment.NewLine
                   + $"角度分解能(360度の分割数): {data["ARES"]}" + Environment.NewLine
                   + $"最小計測可能方向値: {data["AMIN"]}" + Environment.NewLine
                   + $"最大計測可能方向値: {data["AMAX"]}" + Environment.NewLine
                   + $"正面方向値: {data["AFRT"]}" + Environment.NewLine
                   + $"標準操作角速度: {data["SCAN"]}";
        Debug.Log(info);
        // ex: UBG-04LX-F01
        // センサ型式情報: UBG-04LX-F01[Rapid-URG](Hokuyo Automatic Co., Ltd.)
        // 最小計測可能距離 (mm): 20
        // 最大計測可能距離 (mm): 5600
        // 角度分解能(360度の分割数): 1024
        // 最小計測可能方向値: 44
        // 最大計測可能方向値: 725
        // 正面方向値: 384
        // 標準操作角速度: 2400
        //
        // 補足
        // ARES = 1024
        //   360 / 1024 = 0.3515625、およそ0.35度毎にレーザーを飛ばしていることを意味する。
        // AMIN、AMAX、AFRT = 44, 725, 384
        //   0度のindexを0として、計測可能な最小index44で、最大が725
        //   角度が15.4度（44 * 360/1024）から254.9度（725 * 360/1024）で、およそ240度の範囲が測定可能
        //   index384の135度が中央ということになる

        return new Tuple<int, int, int>(int.Parse(data["ARES"]), int.Parse(data["AMIN"]), int.Parse(data["AMAX"]));
    }

    private async UniTask UpdateDataOnThread()
    {
        await UniTask.WaitUntil(() => isOpen);

        Debug.Log($"Start to update sensor data, and data count is {MeasurableRangeMax - MeasurableRangeMin}.");
        var dataCount = MeasurableRangeMax - MeasurableRangeMin + 1;
        filtered = new float[dataCount];
        
        // ループ内でMainThreadとThreadPoolとのスイッチングをしないと、Unityの実行を止めたときに必ずUnityが固まってしまう。
        // おそらくUnityの停止処理からのキャンセルと、ThreadPool内の処理の連携がうまくいっていないと推測し、
        // 性能的に特に影響がなかったので、ループ内で都度Threadingを切り替えることで回避するようにした。
        while (!updateDataCanceler.IsCancellationRequested)
        {
            await UniTask.SwitchToThreadPool();
            UpdateData();
            if (!IsAvailable && distances is { Count: > 0 })
            {
                IsAvailable = true;
            }
            await UniTask.SwitchToMainThread();
        }
        
        Debug.Log("Finished the thread that update sensor data.");
    }

    private long rowTimeStamp;
    private List<long> rowData = new ();
    private float[] filtered;
    private const int FILTER_RANGE = 3;
    private const int MIDDLE = FILTER_RANGE / 2;
    private long[] sorted = new long[FILTER_RANGE];
    private void UpdateData()
    {
        if (urg == null)
        {
            Debug.Log("Open()してからでないと利用できません。");
            return;
        }

        try
        {
            var data = urg.ReadLine();
            if (!SCIP_Reader.MD(data, ref rowTimeStamp, ref rowData))
            {
                Debug.Log(data);
                return;
            }
            if (rowData.Count == 0)
            {
                Debug.Log(data);
                return;
            }

            // median filter
            for (var i = 0; i < rowData.Count; i++)
            {
                if (i < MIDDLE || i > rowData.Count - MIDDLE - 1)
                {
                    filtered[i] = rowData[i];
                }
                else
                {
                    for (var j = 0; j < FILTER_RANGE; j++)
                    {
                        sorted[j] = rowData[i - MIDDLE + j];
                    }
                    Array.Sort(sorted);
                    filtered[i] = sorted[MIDDLE];
                }
            }
            
            lock (lookObj)
            {
                timeStamp = rowTimeStamp;
                distances = filtered.Select(x => x * TO_METER).ToList();
            }
        }
        catch (Exception ex)
        {
            Debug.Log(ex.Message);
        }
    }
}