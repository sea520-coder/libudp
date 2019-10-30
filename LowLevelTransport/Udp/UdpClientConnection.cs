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
    public class UdpClientConnection : Connection
    {
        private readonly Socket client;
        private readonly EndPoint endPoint;
        protected byte[] dataBuffer = new byte[ushort.MaxValue];
        protected TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>(null, TaskCreationOptions.RunContinuationsAsynchronously);
        protected override int SendBufferSize() => client.SendBufferSize;
        protected override int ReceiveBufferSize() => client.ReceiveBufferSize;

        public UdpClientConnection(string host, int port, string remoteHost, int remotePort, int sendBufferSize = 20480, int receiveBufferSize = 20480)
        {
            client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            client.SendBufferSize = sendBufferSize;
            client.ReceiveBufferSize = receiveBufferSize;
            endPoint = new IPEndPoint(IPAddress.Parse(host), port);
            remoteEndPoint = new IPEndPoint(IPAddress.Parse(remoteHost), remotePort);
        }
        public UdpClientConnection(EndPoint ep, int flushInterval = 10, int sendBufferSize = 20480, int receiveBufferSize = 20480)
        {
            endPoint = new IPEndPoint(IPAddress.Any, 0);
            remoteEndPoint = ep;
            client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            client.SendBufferSize = sendBufferSize;
            client.ReceiveBufferSize = receiveBufferSize;
        }
        public Task<bool> ConnectAsync(int timeout = 5000)
        {
            client.Bind(endPoint);
            ReceiveMsg();

            byte[] data = { (byte)UdpSendOption.HandShake };
            SendBytes(data);
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
                RawSend(buff, buff.Length);
            }
        }
       
        public void ReconnectTest()
        {
        }
        protected override void RawSend(byte[] data, int length)
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

            if(connected) //正常断开
            {
                SendDisconnect();
            }

            lock (stateLock)
            {
                State = ConnectionState.NotConnected;
            }
            
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
            if(dataBuffer[0] != (byte)UdpSendOption.HandShakeDone)
            {
                Log.Error($"FirstReceiveCallback: UdpSendOption.HandShakeDone");
                return;
            }
            Log.Info("FirstReceiveCallback {0}", length);
            uint convID_ = BitConverter.ToUInt32(dataBuffer, 1);
            ARQInit(convID_);
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
            int length;
            try
            {
                length = client.EndReceiveFrom(result, ref point);
            }
            catch (Exception e)
            {
                Log.Error($"receiveCallback: {e.Message}");
                return;
            }
            if (length <= 0)
            {
                Log.Error($"receiveCallback: length");
                return;
            }
            Log.Info("receiveCallback {0}", length);

            int ret = ARQReceive(dataBuffer, length);
            if(ret < 0)
            {
                NoReliableReceive(dataBuffer, length);
            }
            
            client.BeginReceiveFrom(dataBuffer, 0, dataBuffer.Length, 0, ref point, ReceiveCallback, client);
        }
        private UInt32 currentMS()
        {
            var ts = DateTime.Now.Subtract(DateTime.Now);
            return (UInt32)ts.TotalMilliseconds;
        }
    }
}

