using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class HitChecker : MonoBehaviour
{
    [SerializeField] private Sensor sensor;
    [SerializeField] private ParticleSystem demoParticle;

    private CancellationTokenSource checking;

    private void OnDestroy()
    {
        StopCheckRepeatedly();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.S))
        {
            var initialized = Init();
            if (initialized)
            {
                StartCheckRepeatedly().Forget();
                Debug.Log("Hit checker is started.");
            }
        }
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            StopCheckRepeatedly();
        }
    }

    private bool Init()
    {
        if (!sensor.IsAvailable)
        {
            Debug.Log("まだセンサーのデータアップデータが開始していません。");
            return false;
        }
        else
        {
            Debug.Log("Hit checker is initialized.");
            return true;
        }
    }

    private async UniTask StartCheckRepeatedly()
    {
        checking = new CancellationTokenSource();
        while (!checking.IsCancellationRequested)
        {
            StartCheck();
            // UBG-04LX-F01の応答速度は28msで、そこから少しだけ短くしている
            await UniTask.Delay(25, cancellationToken: checking.Token);
        }
    }
    
    private void StartCheck()
    {
        var isDetecting = false;
        var p = 0;
        var pCount = 0;
        var hit = -1;
        var maxDistance = 0.5f;
        for (var i = 0; i < sensor.Distances.Count - 1; i++)
        {
            if (hit >= 0) break;

            // センサーの仕様としてある距離以上までものがないと、5cm未満？ぐらいで返ってくることがあるので、その場合はmaxDistanceを設定
            var filterD0 = sensor.Distances[i] < 0.05f ? maxDistance : sensor.Distances[i];
            var d0 = Mathf.Min(maxDistance, filterD0);
            var filterD1 = sensor.Distances[i + 1] < 0.05f ? maxDistance : sensor.Distances[i + 1];
            var d1 = Mathf.Min(maxDistance, filterD1);
            var gap = d0 - d1;

            // 検出開始
            if (gap > 0.1f)
            {
                isDetecting = true;
                p = i + 1;
                pCount = 1;
                // Debug.Log($"[{i}] detecting started {d0}, {d1}, {gap}, {p}, {pCount}");
            }

            if (isDetecting)
            {
                p += i + 1;
                pCount++;
                // Debug.Log($"[{i}] detecting ....... {d0}, {d1}, {gap}, {p}, {pCount}");
            }

            // 検出終了
            if (gap < -0.1f)
            {
                // Debug.Log($"[{i}] detecting end.... {d0}, {d1}, {gap}, {p}, {pCount}");
                // hit
                if (pCount >= 3)
                {
                    hit = p / pCount;
                    // Debug.Log($"[{i}] {hit} = {p} / {pCount}");
                }
                
                // reset
                isDetecting = false;
                p = 0;
                pCount = 0;
            }
        }

        if (hit != -1)
        {
            Debug.Log($"Detected hitting, position is {hit}");
            
            var perDeg = 360f / sensor.AngularResolution;
            var angularRange = (sensor.MeasurableRangeMax - sensor.MeasurableRangeMin) * perDeg;
            var angularAdjust = Mathf.Max(angularRange / 2 - 90, 0);

            var dist = sensor.Distances[hit];
            dist = Mathf.Min(dist, maxDistance);
            var rad = Mathf.Deg2Rad * (perDeg * hit - angularAdjust); 
            var x = dist * Mathf.Cos(rad);
            var y = dist * Mathf.Sin(rad);
            var pos = new Vector3(x, y, 0);
            var particle = Instantiate(demoParticle, pos, Quaternion.identity);
        }
    }

    private void StopCheckRepeatedly()
    {
        if (checking != null)
        {
            checking.Cancel();
            checking.Dispose();
            Debug.Log("Hit checker is finished.");
        }
    }
}
