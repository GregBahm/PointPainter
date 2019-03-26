//------------------------------------------------------------------------------
// <copyright file="MainWindow.xaml.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------
namespace Microsoft.Samples.Kinect.ColorBasics
{
    using System;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Windows;
    using System.Windows.Media;
    using System.Windows.Media.Imaging;
    using Microsoft.Kinect;
    using System.Net;
    using System.Collections.Generic;
    using System.Linq;

    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public string MyIP { get; set; }

        public ImageSource ImageSource { get { return this.colorBitmap; } }
        public ImageSource DepthImageSource { get { return this.depthBitmap; } }

        public string StatusText
        {
            get
            {
                return this.statusText;
            }

            set
            {
                if (this.statusText != value)
                {
                    this.statusText = value;

                    // notify any bound elements that the text has changed
                    if (this.PropertyChanged != null)
                    {
                        this.PropertyChanged(this, new PropertyChangedEventArgs("StatusText"));
                    }
                }
            }
        }

        private const int MapDepthToByte = 8000 / 256;

        private KinectSensor kinectSensor;

        private ColorFrameReader colorFrameReader;
        private DepthFrameReader depthFrameReader;

        private WriteableBitmap colorBitmap;
        private WriteableBitmap depthBitmap;

        private FrameDescription depthFrameDescription;
        private FrameDescription colorFrameDescription;
        
        private byte[] allColorPixels;
        private byte[] pointData;

        private byte[] depthPixels;

        private int[] depthIndexToColorIndex;

        private ColorSpacePoint[] colorSpacePoints;

        private string statusText;

        private readonly ServerCommunication communicationServer;

        public MainWindow()
        {
            this.kinectSensor = KinectSensor.GetDefault();

            this.colorFrameReader = this.kinectSensor.ColorFrameSource.OpenReader();
            this.depthFrameReader = this.kinectSensor.DepthFrameSource.OpenReader();

            this.colorFrameReader.FrameArrived += this.Reader_ColorFrameArrived;
            this.depthFrameReader.FrameArrived += this.Reader_DepthFrameArrived;

            this.depthFrameDescription = this.kinectSensor.DepthFrameSource.FrameDescription;
            this.colorFrameDescription = this.kinectSensor.ColorFrameSource.CreateFrameDescription(ColorImageFormat.Bgra);

            this.depthPixels = new byte[this.depthFrameDescription.Width * this.depthFrameDescription.Height];

            this.colorSpacePoints = new ColorSpacePoint[this.depthFrameDescription.Width * this.depthFrameDescription.Height];
            this.allColorPixels = new byte[this.colorFrameDescription.Width * this.colorFrameDescription.Height * 4];
            this.pointData = new byte[this.depthFrameDescription.Width * this.depthFrameDescription.Height * 5];

            this.depthBitmap = new WriteableBitmap(this.depthFrameDescription.Width, this.depthFrameDescription.Height, 96.0, 96.0, PixelFormats.Gray8, null);
            this.colorBitmap = new WriteableBitmap(colorFrameDescription.Width, colorFrameDescription.Height, 96.0, 96.0, PixelFormats.Bgr32, null);

            this.kinectSensor.IsAvailableChanged += this.Sensor_IsAvailableChanged;

            this.kinectSensor.Open();

            this.StatusText = this.kinectSensor.IsAvailable ? Properties.Resources.RunningStatusText
                                                            : Properties.Resources.NoSensorStatusText;
            this.depthIndexToColorIndex = new int[this.depthFrameDescription.Width * this.depthFrameDescription.Height];
            this.DataContext = this;

            this.communicationServer = new ServerCommunication(GetPointDataForNetwork, GetDepthTableForNetwork);
            this.communicationServer.Start();

            MyIP = Dns.GetHostEntry(Dns.GetHostName()).AddressList[3].ToString();

            this.InitializeComponent();
        }
        
        public event PropertyChangedEventHandler PropertyChanged;

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (this.colorFrameReader != null)
            {
                // ColorFrameReder is IDisposable
                this.colorFrameReader.Dispose();
                this.colorFrameReader = null;
            }

            if (this.kinectSensor != null)
            {
                this.kinectSensor.Close();
                this.kinectSensor = null;
            }
        }

        private void Reader_ColorFrameArrived(object sender, ColorFrameArrivedEventArgs e)
        {
            // ColorFrame is IDisposable
            using (ColorFrame colorFrame = e.FrameReference.AcquireFrame())
            {
                if (colorFrame != null)
                {
                    FrameDescription colorFrameDescription = colorFrame.FrameDescription;

                    using (KinectBuffer colorBuffer = colorFrame.LockRawImageBuffer())
                    {
                        this.colorBitmap.Lock();
                        // verify data and write the new color frame data to the display bitmap
                        if ((colorFrameDescription.Width == this.colorBitmap.PixelWidth) && (colorFrameDescription.Height == this.colorBitmap.PixelHeight))
                        {
                            colorFrame.CopyConvertedFrameDataToIntPtr(
                                this.colorBitmap.BackBuffer,
                                (uint)(colorFrameDescription.Width * colorFrameDescription.Height * 4),
                                ColorImageFormat.Bgra);

                            this.colorBitmap.AddDirtyRect(new Int32Rect(0, 0, this.colorBitmap.PixelWidth, this.colorBitmap.PixelHeight));

                            ProcessColorFrame(colorFrame);
                        }
                        this.colorBitmap.Unlock();
                    }
                }
            }
        }

        private void ProcessColorFrame(ColorFrame colorFrame)
        {
            colorFrame.CopyConvertedFrameDataToArray(allColorPixels, ColorImageFormat.Rgba);

            for (int i = 0; i < colorSpacePoints.Length; i++)
            {
                int colorPixelIndex = depthIndexToColorIndex[i];
                pointData[i * 5 + 2] = allColorPixels[colorPixelIndex * 4];
                pointData[i * 5 + 3] = allColorPixels[colorPixelIndex * 4 + 1];
                pointData[i * 5 + 4] = allColorPixels[colorPixelIndex * 4 + 2];
            }
        }

        private void Reader_DepthFrameArrived(object sender, DepthFrameArrivedEventArgs e)
        {
            bool depthFrameProcessed = false;

            using (DepthFrame depthFrame = e.FrameReference.AcquireFrame())
            {
                if (depthFrame != null)
                {
                    // the fastest way to process the body index data is to directly access 
                    // the underlying buffer
                    using (Microsoft.Kinect.KinectBuffer depthBuffer = depthFrame.LockImageBuffer())
                    {
                        // verify data and write the color data to the display bitmap
                        if (((this.depthFrameDescription.Width * this.depthFrameDescription.Height) == (depthBuffer.Size / this.depthFrameDescription.BytesPerPixel)) &&
                            (this.depthFrameDescription.Width == this.depthBitmap.PixelWidth) && (this.depthFrameDescription.Height == this.depthBitmap.PixelHeight))
                        {
                            // Note: In order to see the full range of depth (including the less reliable far field depth)
                            // we are setting maxDepth to the extreme potential depth threshold
                            ushort maxDepth = ushort.MaxValue;

                            // If you wish to filter by reliable depth distance, uncomment the following line:
                            //// maxDepth = depthFrame.DepthMaxReliableDistance

                            ProcessDepthFrameData(depthBuffer.UnderlyingBuffer, depthBuffer.Size, depthFrame.DepthMinReliableDistance, maxDepth);
                            depthFrameProcessed = true;
                        }
                    }
                }
            }

            if (depthFrameProcessed)
            {
                this.RenderDepthPixels();
            }
        }

        private unsafe void ProcessDepthFrameData(IntPtr depthFrameData, uint depthFrameDataSize, ushort minDepth, ushort maxDepth)
        {
            // depth frame data is a 16 bit value
            ushort* frameData = (ushort*)depthFrameData;

            // convert depth to a visual representation
            for (int i = 0; i < (int)(depthFrameDataSize / this.depthFrameDescription.BytesPerPixel); ++i)
            {
                // Get the depth for this pixel
                ushort depth = frameData[i];

                // To convert to a byte, we're mapping the depth value to the byte range.
                // Values outside the reliable depth range are mapped to 0 (black).
                byte depthPixel = (byte)(depth >= minDepth && depth <= maxDepth ? (depth / MapDepthToByte) : 0);
                this.depthPixels[i] = depthPixel;
                Array.Copy(BitConverter.GetBytes(depth), 0, this.pointData, i * 5, 2);
            }


            this.kinectSensor.CoordinateMapper.MapDepthFrameToColorSpaceUsingIntPtr(depthFrameData, depthFrameDataSize, colorSpacePoints);
            for (int i = 0; i < colorSpacePoints.Length; i++)
            {
                ColorSpacePoint colorSpacePoint = colorSpacePoints[i];
                int colorArrayIndex = GetDepthArrayIndex(colorSpacePoint.X, colorSpacePoint.Y);
                depthIndexToColorIndex[i] = colorArrayIndex;
            }
        }

        private int GetDepthArrayIndex(float xRaw, float yRaw)
        {
            float xParam = float.IsInfinity(xRaw) ? 0 : xRaw;
            float yParam = float.IsInfinity(yRaw) ? 0 : yRaw;
            yParam += colorFrameDescription.Height;
            yParam %= colorFrameDescription.Height;
            int yVal = (int)(yParam) * colorFrameDescription.Width;
            int xVal = (int)(xParam);

            int ret = yVal + xVal;
            if(ret < 0 || ret > (colorFrameDescription.Width * colorFrameDescription.Height))
            {
                throw new Exception();
            }
            return ret;
        }

        private void RenderDepthPixels()
        {
            this.depthBitmap.WritePixels(
                new Int32Rect(0, 0, this.depthBitmap.PixelWidth, this.depthBitmap.PixelHeight),
                this.depthPixels,
                this.depthBitmap.PixelWidth,
                0);
        }

        private void Sensor_IsAvailableChanged(object sender, IsAvailableChangedEventArgs e)
        {
            // on failure, set the status text
            this.StatusText = this.kinectSensor.IsAvailable ? Properties.Resources.RunningStatusText
                                                            : Properties.Resources.SensorNotAvailableStatusText;
        }

        private byte[] GetDepthTableForNetwork()
        {
            PointF[] table = this.kinectSensor.CoordinateMapper.GetDepthFrameToCameraSpaceTable();
            int byteStride = sizeof(float) * 2;
            byte[] ret = new byte[table.Length * byteStride];
            for (int i = 0; i < table.Length; i++)
            {
                PointF tablePoint = table[i];
                int xIndex = i * byteStride;
                int yIndex = xIndex + sizeof(float);
                Array.Copy(BitConverter.GetBytes(tablePoint.X), 0, ret, xIndex, sizeof(float));
                Array.Copy(BitConverter.GetBytes(tablePoint.Y), 0, ret, yIndex, sizeof(float));
            }
            return ret;
        }
        
        private byte[] GetPointDataForNetwork()
        {
            lock (pointData)
            {
                return pointData;
            }
        }
    }
}
