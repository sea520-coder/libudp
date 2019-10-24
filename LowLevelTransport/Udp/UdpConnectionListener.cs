using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
#if DOTNET_CORE
using System.Threading.Tasks.Dataflow;
#endif

namespace LowLevelTransport.Udp
{
    public class UdpConnectionListener
    {
        public Socket server;
        private readonly EndPoint endPoint;
        private readonly byte[] dataBuffer = new byte[ushort.MaxValue];
        private readonly Dictionary<EndPoint, UdpServerConnection> endPoint2Connection = new Dictionary<EndPoint, UdpServerConnection>();
#if DOTNET_CORE
        private readonly BufferBlock<Connection> newConnQueue = new BufferBlock<Connection>();
#else
        private readonly Queue<Connection> newConnQueue = new Queue<Connection>();
#endif
        public int SendBufferSize() => server.SendBufferSize;
        public int ReceiveBufferSize() => server.ReceiveBufferSize;

        public UdpConnectionListener(string host, int port, int sendBufferSize = 20480, int receiveBufferSize = 20480)
        {
            server = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            server.SendBufferSize = sendBufferSize;
            server.ReceiveBufferSize = receiveBufferSize;
            endPoint = new IPEndPoint(IPAddress.Parse(host), port);
        }
        public void Start()
        {
            server.Bind(endPoint);
            receiveMsg();
        }
#if DOTNET_CORE
        public async Task<Connection> AcceptAsync(CancellationToken token)
        {
            return await newConnQueue.ReceiveAsync(cancellationToken: token);
        }
#else
        public Connection Accept()
        {
            if (newConnQueue.Count != 0)
                return newConnQueue.Dequeue();
            return null;
        }
#endif
        public void RemoveConnectionTo(EndPoint endPoint)
        {
            endPoint2Connection.Remove(endPoint);
        }
        public void Close()
        {
            server.Close();
        }
        public void SendBytes(byte[] buff, EndPoint remoteEndPoint)
        {
            server.BeginSendTo(buff, 0, buff.Length, 0, remoteEndPoint, sendCallback, server);
        }
        public void SendBytes(byte[] buff, int length, EndPoint remoteEndPoint)
        {
             server.BeginSendTo(buff, 0, length, 0, remoteEndPoint, sendCallback, server);
        }
        private void sendCallback(IAsyncResult result)
        {
            server.EndSendTo(result);
        }
        private void receiveMsg()
        {
            EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
            server.BeginReceiveFrom(dataBuffer, 0, dataBuffer.Length, 0, ref remoteEP, receiveCallback, server);
        }
        private void receiveCallback(IAsyncResult result)
        {
            EndPoint point = new IPEndPoint(IPAddress.Any, 0);
            int length = server.EndReceiveFrom(result, ref point);

            UdpServerConnection connection;
            if (endPoint2Connection.TryGetValue(point, out connection))
            {
            }
            else
            {
                connection = new UdpServerConnection(this, point);
                endPoint2Connection.Add(point, connection);
#if DOTNET_CORE
                newConnQueue.Post(connection);
#else
                newConnQueue.Enqueue(connection);
#endif
            }

            int ret = connection.aRQ.Input(dataBuffer, length);
            if(ret >= 0)
            {
                length = connection.aRQ.Receive(dataBuffer);
                if(length > 0) //数据包
                {
                    byte[] dst = new byte[length];
                    Buffer.BlockCopy(dataBuffer, 0, dst, 0, length);
                    connection.recvQueue.Enqueue(dst);
                }
                else //确认包 or 错误包 or 部分包
                {}
            }
            else
            {
                byte[] dst = new byte[length];
                Buffer.BlockCopy(dataBuffer, 0, dst, 0, length);
                connection.recvQueue.Enqueue(dst);
            }
            
            server.BeginReceiveFrom(dataBuffer, 0, dataBuffer.Length, 0, ref point, receiveCallback, server);
        }
    }
}

