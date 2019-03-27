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
    public string IpAddress = "127.0.0.1";
    public int Port = 1990;

    public Material PointCloudMat;

    private byte[] pointData;
    private byte[] pointDataSwapper;
    private byte[] depthTableData;
    private byte[] depthTableDataSwapper;

    private const int DepthTextureWidth = 512;
    private const int DepthTextureHeight = 424;
    private const int FramePointsCount = DepthTextureWidth * DepthTextureHeight;

    public const int MaxFrames = 64;
    private int currentPageIndex = 0;

    private ComputeBuffer depthTableBuffer;
    private const int DepthTableStride = sizeof(float) * 2;
    private const int PointDataStride = sizeof(short) + sizeof(byte) * 3;

    private const int DepthTableSize = FramePointsCount * DepthTableStride;
    private const int PointDataSize = FramePointsCount * PointDataStride;

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
        pointsArray = new BufferPoint[FramePointsCount];
        pointsBuffer = new ComputeBuffer(FramePointsCount * MaxFrames, PointsBufferStride);

        depthTableData = new byte[DepthTableSize];
        depthTableDataSwapper = new byte[DepthTableSize];

        depthTableBuffer = new ComputeBuffer(FramePointsCount, DepthTableStride);

        pointData = new byte[PointDataSize];
        pointDataSwapper = new byte[PointDataSize];

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
        SetSourceData();
        PointCloudMat.SetBuffer("_PointsBuffer", pointsBuffer);
        PointCloudMat.SetMatrix("_MasterTransform", transform.localToWorldMatrix);
        PointCloudMat.SetBuffer("_DepthTable", depthTableBuffer);
        PointCloudMat.SetInt("_FramePointsCount", FramePointsCount);
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
        PointCloudMat.SetPass(0);
        Graphics.DrawProcedural(MeshTopology.Points, 1, FramePointsCount * MaxFrames);
    }

    private void SetSourceData()
    {
        for (int i = 0; i < FramePointsCount; i++)
        {
            short depthVal = BitConverter.ToInt16(pointData, i * PointDataStride);
            byte r = pointData[i * PointDataStride + 2];
            byte g = pointData[i * PointDataStride + 3];
            byte b = pointData[i * PointDataStride + 4];
            pointsArray[i] = new BufferPoint() { DepthVal = depthVal, R = r, G = g, B = b };
        }
        currentPageIndex = (currentPageIndex + 1) % MaxFrames;
        //pointsBuffer.SetData(pointsArray);
        pointsBuffer.SetData(pointsArray, 0, FramePointsCount * currentPageIndex, FramePointsCount);
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
                        while (offset < PointDataSize)
                        {
                            offset += stream.Read(pointData, offset, pointData.Length - offset);
                        }
                        
                        lock (pointDataSwapper)
                        {
                            pointDataSwapper = pointData;
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