using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using LowLevelTransport.Utils;
#if DOTNET_CORE
using System.Threading.Tasks.Dataflow;
#endif

namespace LowLevelTransport.Udp
{
    public class UdpConnectionListener
    {
        private Socket server;
        private readonly EndPoint endPoint;
        private readonly byte[] dataBuffer = new byte[ushort.MaxValue];
        private readonly Dictionary<EndPoint, UdpServerConnection> endPoint2Connection = new Dictionary<EndPoint, UdpServerConnection>();
#if DOTNET_CORE
        private readonly BufferBlock<Connection> newConnQueue = new BufferBlock<Connection>();
#else
        private readonly Queue<Connection> newConnQueue = new Queue<Connection>();
#endif
        private uint convId = 0;
        private uint GenerateConvID()
        {
            return convId >= (uint)ConvIDOption.Max ? 1 : ++convId;
        }
        internal int SendBufferSize() => server.SendBufferSize;
        internal int ReceiveBufferSize() => server.ReceiveBufferSize;
        public UdpConnectionListener(string host, int port, int sendBufferSize = (int)SocketBufferOption.SendSize, 
            int receiveBufferSize = (int)SocketBufferOption.ReceiveSize)
        {
            server = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            server.SendBufferSize = sendBufferSize;
            server.ReceiveBufferSize = receiveBufferSize;
            endPoint = new IPEndPoint(IPAddress.Parse(host), port);
#if WIN 
            //解决对端关闭了，但server端还调用SendBytes()给对端发送数据
            uint SIO_UDP_CONNRESET = 2550136844; //errorcode = 10054
            server.IOControl((int)SIO_UDP_CONNRESET, new byte[1], null);
#endif
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
        public void Close()
        {
            server.Close();
        }
        internal void RemoveConnectionTo(EndPoint endPoint)
        {
            endPoint2Connection.Remove(endPoint);
        }
        internal void SendBytes(byte[] buff, EndPoint remoteEndPoint)
        {
            server.BeginSendTo(buff, 0, buff.Length, 0, remoteEndPoint, sendCallback, server);
        }
        internal void SendBytes(byte[] buff, int length, EndPoint remoteEndPoint)
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
        private void CreateConnection(EndPoint point, ref UdpServerConnection connection, ref uint convID_)
        {
            convID_ = GenerateConvID();
            connection = new UdpServerConnection(this, point, convID_);
            endPoint2Connection.Add(point, connection);
#if DOTNET_CORE
            newConnQueue.Post(connection);
#else
            newConnQueue.Enqueue(connection);
#endif
        }
        private void receiveCallback(IAsyncResult result)
        {
            EndPoint point = new IPEndPoint(IPAddress.Any, 0);
            int length = server.EndReceiveFrom(result, ref point);

            UdpServerConnection connection;
            uint convID_ = 0;
            bool beforeExistConnection = false;
            if (endPoint2Connection.TryGetValue(point, out connection)) //已建立连接
            {
                beforeExistConnection = true;
            }
            else //新连接
            {
                CreateConnection(point, ref connection, ref convID_);
            }

            int ret = connection.ARQReceive(dataBuffer, length);
            if(ret < 0)
            {
                if(length == 1)
                {
                    if(dataBuffer[0] == (byte)UdpSendOption.CreateConnection) //建立连接
                    {
                        if(beforeExistConnection)
                        {
                            connection.Close();
                            CreateConnection(point, ref connection, ref convID_);
                        }
                        
                        byte[] conv = BitConverter.GetBytes(convID_);
                        byte[] data = new byte[5] {(byte)UdpSendOption.CreateConnectionResponse, 0, 0, 0, 0};
                        Buffer.BlockCopy(conv, 0, data, 1, conv.Length);
                        connection.UnReliableSend(data, data.Length);
                        Log.Info("create an connection convID:{0}", convID_);
                    }
                    else if(dataBuffer[0] == (byte)UdpSendOption.Disconnect) //客户端主动释放连接
                    {
                        connection.Close();
                        connection = null;
                        Log.Info("close an connection");
                    }
                    else if(dataBuffer[0] == (byte)UdpSendOption.Heartbeat) //keepalive
                    {
                        byte[] data = new byte[1] {(byte)UdpSendOption.HeartbeatResponse};
                        connection.UnReliableSend(data, data.Length);
                    }
                    else
                    {
                        Log.Error("receiveCallback option{0}", dataBuffer[0]);
                    }
                }
                else //正数数据收
                {
                    connection.NoReliableReceive(dataBuffer, length);
                }
            }
            
            server.BeginReceiveFrom(dataBuffer, 0, dataBuffer.Length, 0, ref point, receiveCallback, server);
        }
    }
}