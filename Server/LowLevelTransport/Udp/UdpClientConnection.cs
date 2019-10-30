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
            uint SIO_UDP_CONNRESET = 2550136844; //errorcode = 10054
            client.IOControl((int)SIO_UDP_CONNRESET, new byte[1], null);
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
            ReceiveMsg();

            byte[] data = { (byte)UdpSendOption.CreateConnection };
            UnReliableSend(data, data.Length);
            var timer = new Timer((object obj) => { tcs.TrySetResult(false); }, null, timeout, Timeout.Infinite);
            return tcs.Task;
        }
        public override void SendBytes(byte[] buff, SendOption sendOption = SendOption.None)
        {
            if(buff.Length > SendBufferSize() && (sendOption == SendOption.None))
            {
                throw new LowLevelTransportException($"Send byte size:{buff.Length} too large");
            }
           
            if(sendOption == SendOption.FragmentedReliable)
            {
                ARQSend(buff);
            }
            else
            {
                System.Diagnostics.Debug.Assert(buff.Length > 1);
                UnReliableSend(buff, buff.Length);
            }
        }
        public override void UnReliableSend(byte[] data, int length)
        {
            client.BeginSendTo(data, 0, length, 0, remoteEndPoint, SendCallback, client);
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
        private void SendCallback(IAsyncResult result)
        {
            client.EndSendTo(result);
        }
        private void ReceiveMsg()
        {
            EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
            client.BeginReceiveFrom(dataBuffer, 0, dataBuffer.Length, 0, ref remoteEP, FirstReceiveCallback, client);
        }
        private void FirstReceiveCallback(IAsyncResult result)
        {
            EndPoint point = new IPEndPoint(IPAddress.Any, 0);
            int length;
            try
            {
                length = client.EndReceiveFrom(result, ref point);
            }
            catch (Exception e)
            {
                Log.Error($"FirstReceiveCallback: {e.Message}");
                return;
            }
            if (length <= 0)
            {
                Log.Error($"FirstReceiveCallback: length");
                return;
            }
            if(dataBuffer[0] != (byte)UdpSendOption.CreateConnectionResponse)
            {
                Log.Error($"FirstReceiveCallback: UdpSendOption.HandShakeDone");
                return;
            }
            Log.Info("FirstReceiveCallback {0}", length);
            uint convID_ = BitConverter.ToUInt32(dataBuffer, 1);
            ARQInit(convID_);
            InitKeepAliveTimer();
            tcs.TrySetResult(true);

            lock (stateLock)
            {
                State = ConnectionState.Connected;
            }

            client.BeginReceiveFrom(dataBuffer, 0, dataBuffer.Length, 0, ref point, ReceiveCallback, client);
        }
        private void ReceiveCallback(IAsyncResult result)
        {
            EndPoint point = new IPEndPoint(IPAddress.Any, 0);
            int length = 0;
            try
            {
                length = client.EndReceiveFrom(result, ref point);
            }
            catch (Exception e)
            {
                Log.Error($"receiveCallback exception: {e.Message}");
                return;
            }
            if (length <= 0)
            {
                Log.Error($"receiveCallback: length");
                return;
            }
            Log.Info("receiveCallback {0}", length);
            //24 = ack packages length
            int ret = ARQReceive(dataBuffer, length);
            if(ret < 0)
            {
                if(length == 1)
                {
                    if(dataBuffer[0] == (byte)UdpSendOption.HeartbeatResponse)
                    {
                        HandleHeartbeat();
                    }
                }
                else
                {
                    NoReliableReceive(dataBuffer, length);
                }
            }
            
            client.BeginReceiveFrom(dataBuffer, 0, dataBuffer.Length, 0, ref point, ReceiveCallback, client);
        }
        private UInt32 currentMS()
        {
            var ts = DateTime.Now.Subtract(DateTime.Now);
            return (UInt32)ts.TotalMilliseconds;
        }
        private void SendDisconnect()
        {
            var data = new byte[1] { (byte)UdpSendOption.Disconnect };
            UnReliableSend(data, data.Length);
        }
    }
}
