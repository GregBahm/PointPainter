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
    public ComputeShader ComputeShader;
    private int ComputeKernel;
    private const int ComputeBatchSize = 128;
    private int ComputeGroupsCount;

    private byte[] pointData;
    private byte[] pointDataSwapper;
    private byte[] depthTableData;
    private byte[] depthTableDataSwapper;

    private const int DepthTextureWidth = 512;
    private const int DepthTextureHeight = 424;
    private const int FramePointsCount = DepthTextureWidth * DepthTextureHeight;

    public const int MaxFrames = 32;
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
    private BufferPoint[] rawPointsArray;
    public ComputeBuffer rawPointsBuffer;
    private const int RawPointsBufferStride = sizeof(int) * 4;

    public ComputeBuffer processedPointsBuffer;
    public const int ProcessedPointsBuffer = sizeof(float) * 6;

    struct BufferPoint
    {
        public int DepthVal;
        public int R;
        public int G;
        public int B;
    }

    private void Start()
    {
        ComputeKernel = ComputeShader.FindKernel("CSMain");
        ComputeGroupsCount = Mathf.CeilToInt(FramePointsCount / ComputeBatchSize);

        rawPointsArray = new BufferPoint[FramePointsCount];
        rawPointsBuffer = new ComputeBuffer(FramePointsCount, RawPointsBufferStride);

        depthTableData = new byte[DepthTableSize];
        depthTableDataSwapper = new byte[DepthTableSize];

        depthTableBuffer = new ComputeBuffer(FramePointsCount, DepthTableStride);

        processedPointsBuffer = new ComputeBuffer(FramePointsCount * MaxFrames, ProcessedPointsBuffer);

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
        RunComputeShader();

        PointCloudMat.SetBuffer("_FullPointsBuffer", processedPointsBuffer);
        PointCloudMat.SetMatrix("_MasterTransform", transform.localToWorldMatrix);
    }

    private void RunComputeShader()
    {
        currentPageIndex = (currentPageIndex + 1) % MaxFrames;

        ComputeShader.SetBuffer(ComputeKernel,"_DepthTable", depthTableBuffer);
        ComputeShader.SetBuffer(ComputeKernel, "_IncomingPointsBuffer", rawPointsBuffer);
        ComputeShader.SetBuffer(ComputeKernel, "_FullPointsBuffer", processedPointsBuffer);
        ComputeShader.SetInt("_CurrentFrameIndex", currentPageIndex);
        ComputeShader.SetInt("_FramePointsCount", FramePointsCount);
        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(Camera.main);
        ComputeShader.SetVector("_CameraPlaneA", PackPlaneIntoVector(planes[0]));
        ComputeShader.SetVector("_CameraPlaneB", PackPlaneIntoVector(planes[1]));
        ComputeShader.SetVector("_CameraPlaneC", PackPlaneIntoVector(planes[2]));
        ComputeShader.SetVector("_CameraPlaneD", PackPlaneIntoVector(planes[3]));

        ComputeShader.Dispatch(ComputeKernel, ComputeGroupsCount, 1, 1);
    }

    private Vector4 PackPlaneIntoVector(Plane plane)
    {
        return new Vector4(plane.normal.x, plane.normal.y, plane.normal.z, plane.distance);
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
            rawPointsArray[i] = new BufferPoint() { DepthVal = depthVal, R = r, G = g, B = b };
        }
       rawPointsBuffer.SetData(rawPointsArray);
    }
    
    private void OnDestroy()
    {
        depthTableBuffer.Release();
        rawPointsBuffer.Release();
        processedPointsBuffer.Release();
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