using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class Debugger : MonoBehaviour
{
    [SerializeField] private Sensor sensor;

    // caches
    private GameObject[] cubes;
    private long lastTimestamp;

    void Start()
    {
        ShowDebugDataOnConsole(10).Forget();
    }
    
    void Update()
    {
        // next:
        // * 今度は触った判定を撮るようにする。
        //    ref: https://cgworld.jp/regular/201901-codelight-unity05.html
        ShowDebugCubes();        
    }
    
    private async UniTask ShowDebugDataOnConsole(int showCount)
    {
        while (sensor.Distances.Count == 0)
        {
            Debug.Log("データが取得されていません。");
            await UniTask.Delay(1000);
        }
        
        try
        {
            long timeStamp = 0;
            for (var i = 0; i < showCount; ++i)
            {
                while (timeStamp == sensor.TimeStamp)
                {
                    await UniTask.Delay(50);
                }

                timeStamp = sensor.TimeStamp;
                Debug.Log("time stamp: " + sensor.TimeStamp + " distance[100] : " + sensor.Distances[100]);
            }
        }
        catch (Exception ex)
        {
            Debug.Log(ex.Message);
        }
    }

    /// <summary>
    /// 受け取ったデータを細長いcubeで表示する。
    /// ただしサイズ感をmagnification、最大距離をmaxDistanceで調整している。
    /// なおcubeで出しているので、中心がレーダーが届いているところになることに注意。
    /// </summary>
    private void ShowDebugCubes()
    {
        if (sensor.Distances.Count == 0)
        {
            Debug.Log("データが取得されていません。");
            return;
        }
        if (lastTimestamp == sensor.TimeStamp)
        {
            // データが更新されていないためcubesを更新しません
            return;   
        }

        // REVISIT: 固定値になるので毎回計算する必要ない
        var perDeg = 360f / sensor.AngularResolution;
        var angularRange = (sensor.MeasurableRangeMax - sensor.MeasurableRangeMin) * perDeg;
        var angularAdjust = Mathf.Max(angularRange / 2 - 90, 0);
        
        if (cubes == null || cubes.Length != sensor.Distances.Count)
        {
            cubes = new GameObject[sensor.Distances.Count];
            for (var i = 0; i < sensor.Distances.Count; i++)
            {
                cubes[i] = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cubes[i].transform.SetParent(transform);
                cubes[i].transform.localScale = new Vector3(0.02f, 0.002f, 0.002f);
                cubes[i].transform.localRotation = Quaternion.Euler(new Vector3(0, 0, perDeg * i));
                cubes[i].name = $"cube{i + sensor.MeasurableRangeMin:000}";
            }
        }
        
        try
        {
            var maxDistance = 0.5f;
            for(var i = 0; i < cubes.Length - 1; i++)
            {
                var dist = sensor.Distances[i];
                dist = Mathf.Min(dist, maxDistance);
                var rad = Mathf.Deg2Rad * (perDeg * i - angularAdjust); 
                var x = dist * Mathf.Cos(rad);
                var y = dist * Mathf.Sin(rad);
                cubes[i].transform.position = new Vector3(x, y, 0);
            }

            lastTimestamp = sensor.TimeStamp;
        }
        catch (Exception ex)
        {
            Debug.Log(ex.Message);
        }
    }
}
