using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using LowLevelTransport.Utils;

namespace LowLevelTransport.Udp
{
    public class UdpClientConnection : Connection
    {
        private readonly Socket client;

        private readonly byte[] dataBuffer = new byte[ushort.MaxValue];

        private readonly Queue<byte[]> recvQueue = new Queue<byte[]>();

        private readonly EndPoint endPoint;
        private readonly EndPoint remoteEndPoint;

        public override int SendBufferSize() => client.SendBufferSize;
        public override int ReceiveBufferSize() => client.ReceiveBufferSize;

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
            return Task.Run(() =>
            {
                Connect();
                return true;
            });
        }
        public void Connect()
        {
            client.Bind(endPoint);
            receiveMsg();
            
            ARQInit();
        }
        public override void SendBytes(byte[] buff, SendOption sendOption = SendOption.None)
        {
            if(buff.Length > SendBufferSize() && (sendOption == SendOption.None))
            {
                throw new LowLevelTransportException($"Send byte size:{buff.Length} too large");
            }
           
            if(sendOption == SendOption.FragmentedReliable)
            {
                aRQ.Send(buff);
            }
            else
            {
                RawSend(buff, buff.Length);
            }
        }
        public override void RawSend(byte[] data, int length)
        {
            client.BeginSendTo(data, 0, length, 0, remoteEndPoint, sendCallback, client);
        }
        public override byte[] Receive()
        {
            if (recvQueue.Count != 0)
                return recvQueue.Dequeue();
            return null;
        }
        public override void Dispose()
        {
            bool connected;
            connected = State == ConnectionState.Connected;

            if(connected)
            {
                SendDisconnect();
            }

            State = ConnectionState.NotConnected;
            
            StopTimer();
            client.Close();
        }
        public void ReconnectTest()
        {

        }
        private void sendCallback(IAsyncResult result)
        {
            client.EndSendTo(result);
        }
        private void receiveMsg()
        {
            EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
            client.BeginReceiveFrom(dataBuffer, 0, dataBuffer.Length, 0, ref remoteEP, receiveCallback, client);
        }
        private void receiveCallback(IAsyncResult result)
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

            int ret = aRQ.Input(dataBuffer, length);
            if(ret >= 0) //是可靠包
            {
                length = aRQ.Receive(dataBuffer);
                if(length > 0)
                {
                    byte[] dst = new byte[length];
                    Buffer.BlockCopy(dataBuffer, 0, dst, 0, length);
                    recvQueue.Enqueue(dst);
                }
                else //是确认包 or 错误包
                {}
            }
            else
            {
                byte[] dst = new byte[length];
                Buffer.BlockCopy(dataBuffer, 0, dst, 0, length);
                recvQueue.Enqueue(dst);
            }
            
            client.BeginReceiveFrom(dataBuffer, 0, dataBuffer.Length, 0, ref point, receiveCallback, client);
        }
        private UInt32 currentMS()
        {
            var ts = DateTime.Now.Subtract(DateTime.Now);
            return (UInt32)ts.TotalMilliseconds;
        }
    }
}

