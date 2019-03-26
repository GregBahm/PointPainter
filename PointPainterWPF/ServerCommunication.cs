using Microsoft.Kinect;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Samples.Kinect.ColorBasics
{
    public class ServerCommunication
    {
        private TcpListener server;
        
        private readonly Func<Byte[]> pointDataGetter;
        private readonly Func<Byte[]> depthTableGetter;

        public ServerCommunication(Func<Byte[]> pointDataGetter, Func<Byte[]> depthTableGetter)
        {
            this.pointDataGetter = pointDataGetter;
            this.depthTableGetter = depthTableGetter;
        }

        public async void Start()
        {
            server = new TcpListener(IPAddress.Any, 1990);
            server.Start();

            while (true)
            {
                TcpClient client = await server.AcceptTcpClientAsync();
                client.ReceiveTimeout = 60 * 1000; // in milliseconds
                Task t = Task.Run(() => OnConnected(client));
            }
        }

        private void OnConnected(TcpClient client)
        {
            NetworkStream stream = client.GetStream();
            
            byte[] tableData = depthTableGetter();
            stream.Write(tableData, 0, tableData.Length);

            while (client.Connected)
            {
                byte[] pointDataBytes = pointDataGetter();
                stream.Write(pointDataBytes, 0, pointDataBytes.Length);

                while (!stream.DataAvailable && client.Connected)
                {
                    System.Threading.Thread.Sleep(1);
                }
                if (client.Connected)
                {
                    while (stream.DataAvailable)
                    {
                        stream.ReadByte();
                    }
                }
            }

            client.Close();
        }
    }
}