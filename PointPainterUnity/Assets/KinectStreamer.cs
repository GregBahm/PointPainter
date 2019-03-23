using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

public class KinectStreamer : MonoBehaviour
{
    public bool ShowStreamPoints = true;

    public string IpAddress = "127.0.0.1";
    public int Port = 1990;

    public Material PointCloudMat;

    private byte[] depthData;
    private byte[] depthDataSwapper;
    private byte[] colorData;
    private byte[] colorDataSwapper;
    private byte[] depthTableData;
    private byte[] depthTableDataSwapper;

    private const int DepthTextureWidth = 512;
    private const int DepthTextureHeight = 424;
    private const int DepthPointsCount = DepthTextureWidth * DepthTextureHeight;

    private ComputeBuffer depthTableBuffer;
    private const int DepthTableStride = sizeof(float) * 2;
    private const int DepthDataStride = sizeof(short);
    private const int ColorDataStride = sizeof(byte) * 3;

    private const int DepthTableSize = DepthPointsCount * DepthTableStride;
    private const int DepthDataSize = DepthPointsCount * DepthDataStride;
    private const int ColorDataSize = DepthPointsCount * ColorDataStride;

    private bool depthTableLoaded;
    private bool depthTableSet;

    private Thread thread;
    public float ThreadFPS;

    private static bool Run = false;
    
    struct BufferPoint
    {
        public int DepthVal;
        public int R;
        public int G;
        public int B;
    }
    private BufferPoint[] pointsArray;
    public ComputeBuffer pointsBuffer;
    private const int PointsBufferStride = sizeof(int) * 4;

    private void Start()
    {
        pointsArray = new BufferPoint[DepthPointsCount];
        pointsBuffer = new ComputeBuffer(DepthPointsCount, PointsBufferStride);

        depthTableData = new byte[DepthTableSize];
        depthTableDataSwapper = new byte[DepthTableSize];

        depthTableBuffer = new ComputeBuffer(DepthPointsCount, DepthTableStride);

        depthData = new byte[DepthDataSize];
        depthDataSwapper = new byte[DepthDataSize];

        colorData = new byte[ColorDataSize];
        colorDataSwapper = new byte[ColorDataSize];

        thread = new Thread(() => ReadNetworkData());
        thread.IsBackground = true;
        Run = true;
        thread.Start();
    }

    private void Update()
    {
        if(depthTableLoaded && !depthTableSet)
        {
            TryLoadDepthTable();
        }
        GetSourceData();
        PointCloudMat.SetBuffer("_PointsBuffer", pointsBuffer);
        PointCloudMat.SetMatrix("_MasterTransform", transform.localToWorldMatrix);
        PointCloudMat.SetBuffer("_DepthTable", depthTableBuffer);
    }

    private void TryLoadDepthTable()
    {
        lock (depthTableDataSwapper)
        {
            depthTableBuffer.SetData(depthTableDataSwapper);
        }
        depthTableSet = true;
    }

    private void OnRenderObject()
    {
        if(ShowStreamPoints)
        {
            PointCloudMat.SetPass(0);
            Graphics.DrawProcedural(MeshTopology.Points, 1, DepthPointsCount);
        }
    }

    private void GetSourceData()
    {
        for (int i = 0; i < DepthPointsCount; i++)
        {
            short depthVal = BitConverter.ToInt16(depthData, i * 2);
            byte r = colorData[i * 3];
            byte g = colorData[i * 3 + 1];
            byte b = colorData[i * 3 + 2];
            pointsArray[i] = new BufferPoint() { DepthVal = depthVal, R = r, G = g, B = b };
        }
        pointsBuffer.SetData(pointsArray);
    }
    
    private void OnDestroy()
    {
        depthTableBuffer.Release();
        pointsBuffer.Release();
        Run = false;
    }

    private void ReadNetworkData()
    {
        Stopwatch threadTimer = new Stopwatch();
        
        while (Run)
        {

            using (TcpClient client = new TcpClient())
            {
                client.Connect(IpAddress, Port);

                using (NetworkStream stream = client.GetStream())
                {


                    int offset = 0;
                    while (offset < DepthTableSize)
                    {
                        offset += stream.Read(depthTableData, offset, depthTableData.Length - offset);
                    }

                    lock (depthTableDataSwapper)
                    {
                        depthTableDataSwapper = depthTableData;
                    }

                    depthTableLoaded = true;

                    while (client.Connected)
                    {
                        threadTimer.Start();

                        offset = 0;
                        while (offset < DepthDataSize)
                        {
                            offset += stream.Read(depthData, offset, depthData.Length - offset);
                        }

                        offset = 0;
                        while (offset < ColorDataSize)
                        {
                            offset += stream.Read(colorData, offset, colorData.Length - offset);
                        }


                        lock (depthDataSwapper)
                        {
                            depthDataSwapper = depthData;
                        }

                        lock (colorDataSwapper)
                        {
                            colorDataSwapper = colorData;
                        }

                        ThreadFPS = 1.0f / (float)threadTimer.Elapsed.TotalSeconds;
                        threadTimer.Reset();

                        stream.WriteByte(0);
                        Thread.Sleep(1);

                    } // END client.connected
                } // END using stream
            } // END using client            
        }// END while true
    }
}