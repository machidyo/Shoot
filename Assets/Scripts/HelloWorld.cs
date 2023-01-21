using System;
using System.Collections.Generic;
using System.IO.Ports;
using SCIP_library;
using UnityEngine;

public class HelloWorld : MonoBehaviour
{
    void Start()
    {
        Main();
    }

    static void Main()
    {
        const int GET_NUM = 10;
        const int start_step = 0;
        const int end_step = 760;
        
        try
        {
            var port_name = "COM3";
            var baudrate = 115200;
            Debug.Log("Connect setting = Port name : " + port_name + " Baudrate : " + baudrate);

            var urg = new SerialPort(port_name, baudrate);
            urg.NewLine = "\n\n";

            urg.Open();

            urg.Write(SCIP_Writer.SCIP2());
            urg.ReadLine(); // ignore echo back
            urg.Write(SCIP_Writer.MD(start_step, end_step));
            urg.ReadLine(); // ignore echo back

            var distances = new List<long>();
            long time_stamp = 0;
            for (var i = 0; i < GET_NUM; ++i)
            {
                var receive_data = urg.ReadLine();
                if (!SCIP_Reader.MD(receive_data, ref time_stamp, ref distances))
                {
                    Debug.Log(receive_data);
                    break;
                }

                if (distances.Count == 0)
                {
                    Debug.Log(receive_data);
                    continue;
                }

                // show distance data
                Debug.Log("time stamp: " + time_stamp.ToString() + " distance[100] : " + distances[100].ToString());
            }

            urg.Write(SCIP_Writer.QT()); // stop measurement mode
            urg.ReadLine(); // ignore echo back
            urg.Close();
        }
        catch (Exception ex)
        {
            Debug.Log(ex.Message);
        }
    }
}