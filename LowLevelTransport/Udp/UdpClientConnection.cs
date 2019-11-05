using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using LowLevelTransport.Utils;
using System.Threading;

namespace LowLevelTransport.Udp
{
    public partial class UdpClientConnection : Connection
    {
        private readonly Socket client;
        private readonly EndPoint endPoint;
        protected byte[] dataBuffer = new byte[ushort.MaxValue];
        protected TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>(null, TaskCreationOptions.RunContinuationsAsynchronously);
        protected override int SendBufferSize() => client.SendBufferSize;
        protected override int ReceiveBufferSize() => client.ReceiveBufferSize;

        public UdpClientConnection(string host, int port, string remoteHost, int remotePort, 
            int sendBufferSize = (int)SocketBufferOption.SendSize, int receiveBufferSize = (int)SocketBufferOption.ReceiveSize)
        {
            client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            client.SendBufferSize = sendBufferSize;
            client.ReceiveBufferSize = receiveBufferSize;
            endPoint = new IPEndPoint(IPAddress.Parse(host), port);
            remoteEndPoint = new IPEndPoint(IPAddress.Parse(remoteHost), remotePort);
#if WIN
            uint SIO_UDP_CONNRESET = 2550136844; //errorcode = 10054
            client.IOControl((int)SIO_UDP_CONNRESET, new byte[1], null);
#endif
        }
        public UdpClientConnection(EndPoint ep, int flushInterval = 10, 
            int sendBufferSize = (int)SocketBufferOption.SendSize, int receiveBufferSize = (int)SocketBufferOption.ReceiveSize)
        {
            endPoint = new IPEndPoint(IPAddress.Any, 0);
            remoteEndPoint = ep;
            client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            client.SendBufferSize = sendBufferSize;
            client.ReceiveBufferSize = receiveBufferSize;
        }
        public Task<bool> ConnectAsync(int timeout = (int)ConnectOption.Timeout)
        {
            client.Bind(endPoint);
            Thread receiveThread = new Thread(ReceiveMsg);
            receiveThread.IsBackground = true;
            receiveThread.Start();

            byte[] data = { (byte)UdpSendOption.CreateConnection };
            SendBytes(data);
            var timer = new Timer((object obj) => { tcs.TrySetResult(false); }, null, timeout, Timeout.Infinite);
            return tcs.Task;
        }
        protected override void UnReliableSend(byte[] data, int length)
        {
            client.SendTo(data, 0, length, 0, remoteEndPoint);
        }
        protected override void Dispose()
        {
            bool connected;
            lock (stateLock)
            {
                connected = State == ConnectionState.Connected;
            }

            if(connected)
            {
                SendDisconnect();
            }

            lock (stateLock)
            {
                State = ConnectionState.NotConnected;
            }
            
            StopKeepAliveTimer();
            client.Close();
        }
        private void ReceiveMsg()
        {
            while(true)
            {
                EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

                if(!client.Poll(-1, SelectMode.SelectRead))
                {
                    return;
                }

                int length = -1;
                try
                {
                    length = client.ReceiveFrom(dataBuffer, 0, dataBuffer.Length, 0, ref remoteEP);
                }
                catch (Exception e)
                {
                    Log.Error($"receiveCallback exception: {e.Message}");
                    return;
                }
                if (length <= 1)
                {
                    Log.Error($"receiveCallback: length");
                    return;
                }
                Log.Info("receiveCallback {0}", length);
                //25 = ack packages length

                byte option = dataBuffer[0];
                if(option == (byte)UdpSendOption.ReliableData)
                {
                    ARQReceive(dataBuffer, 1, length - 1);
                }
                else if(option == (byte)UdpSendOption.UnReliableData)
                {
                    if(length == 2 && dataBuffer[1] == (byte)UdpSendOption.HeartbeatResponse)
                    {
                        HandleHeartbeat();
                    }
                    if(length == 6 && dataBuffer[1] == (byte)UdpSendOption.CreateConnectionResponse)
                    {
                        Log.Info("FirstReceiveCallback");
                        uint convID_ = BitConverter.ToUInt32(dataBuffer, 2);
                        ARQInit(convID_);
                        InitKeepAliveTimer();
                        tcs.TrySetResult(true);
                        lock (stateLock)
                        {
                            State = ConnectionState.Connected;
                        }
                    }
                    else
                    {
                        NoReliableReceive(dataBuffer, 1, length - 1);
                    }
                }
                else
                {
                    Log.Error("receive data is error {0}", option);
                }
            }
        }
        private UInt32 currentMS()
        {
            var ts = DateTime.Now.Subtract(DateTime.Now);
            return (UInt32)ts.TotalMilliseconds;
        }
        private void SendDisconnect()
        {
            var data = new byte[1] { (byte)UdpSendOption.Disconnect };
            SendBytes(data);
        }
        public void ReconnectTest()
        {
        }
    }
}
