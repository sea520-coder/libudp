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
    public partial class UdpConnectionListener
    {
        private Socket server;
        private readonly EndPoint endPoint;
        private readonly byte[] dataBuffer = new byte[ushort.MaxValue];
        private Thread receiveThread;
        private object end2conLock = new object();
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
        public UdpConnectionListener(string host, int port, int sendBufferSize = (int)ServerSocketBufferOption.SendSize, 
            int receiveBufferSize = (int)ServerSocketBufferOption.ReceiveSize)
        {
            server = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                SendBufferSize = sendBufferSize,
                ReceiveBufferSize = receiveBufferSize,
                SendTimeout = 500,
                ReceiveTimeout = 500,
            };
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
            receiveThread = new Thread(ReceiveMsg)
            {
                IsBackground = true
            };
            receiveThread.Start();
            InitCheckTimeoutTimer();
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
            StopCheckTimeoutTimer();
            receiveThread.Abort();
            server.Close();
        }
        internal void RemoveConnectionTo(EndPoint endPoint)
        {
            lock(end2conLock)
            {
                endPoint2Connection.Remove(endPoint);
            }
        }
        internal void SendBytes(byte[] buff, EndPoint remoteEndPoint)
        {
            try
            {
                server.SendTo(buff, 0, buff.Length, SocketFlags.None, remoteEndPoint);
            }
            catch(Exception e)
            {
                Log.Error($"[SocketSend|IP:{remoteEndPoint.ToString()}], Exception:{e}");
            }
        }
        internal void SendBytes(byte[] buff, int length, EndPoint remoteEndPoint)
        {
            try
            {
                server.SendTo(buff, 0, length, SocketFlags.None, remoteEndPoint);
            }
            catch(Exception e)
            {
                Log.Error($"[SocketSend|IP:{remoteEndPoint.ToString()}], Exception:{e}");
            }
        }
        private void ReceiveMsg()
        {
            while(true)
            {
                if(!server.Poll(1000000, SelectMode.SelectRead))
                {
                    continue;
                }

                EndPoint point = new IPEndPoint(IPAddress.Any, 0);
                int length = -1;
                try
                {
                    length = server.ReceiveFrom(dataBuffer, 0, dataBuffer.Length, SocketFlags.None, ref point);
                }
                catch (SocketException e)
                {
                    Log.Error($"receiveCallback exception: {e.Message}");
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                if(length <= 1)
                {
                    Log.Error($"receiveCallback: length");
                    continue;
                }

                //Log.Info("receive length={0} {1}", length, dataBuffer[0]);
                UdpServerConnection connection;
                uint convID_ = 0;
                bool beforeExistConnection = false;

                if (GetConnection(point, out connection)) //已建立连接
                {
                    beforeExistConnection = true;
                }
                else //新连接
                {
                    CreateConnection(point, ref connection, ref convID_);
                }

                byte option = dataBuffer[0];
                if(option == (byte)UdpSendOption.ReliableData)
                {
                    connection.ARQReceive(dataBuffer, 1, length - 1);
                }
                else if(option == (byte)UdpSendOption.UnReliableData)
                {
                    if(length == 2 && dataBuffer[1] == (byte)UdpSendOption.CreateConnection)
                    {
                            if(beforeExistConnection) //之前的连接没有被释放掉
                            {
                                connection.Close();
                                connection = null;
                                CreateConnection(point, ref connection, ref convID_);
                            }
                            
                            byte[] conv = BitConverter.GetBytes(convID_);
                            byte[] buff = new byte[5] {(byte)UdpSendOption.CreateConnectionResponse, 0, 0, 0, 0};
                            Buffer.BlockCopy(conv, 0, buff, 1, conv.Length);
                            connection.SendBytes(buff);
                            Log.Info("create an connection convID:{0}", convID_);
                    }
                    else if(length == 2 && dataBuffer[1] == (byte)UdpSendOption.Disconnect) //客户端主动释放连接
                    {
                        connection.Close();
                        connection = null;
                        Log.Info("close an connection");
                    }
                    else if(length == 2 && dataBuffer[1] == (byte)UdpSendOption.Heartbeat) //keepalive
                    {
                        //记录当前时间
                        connection.KeepAliveCurTimestamp = connection.UnixTimeStamp();

                        byte[] buff = new byte[1] {(byte)UdpSendOption.HeartbeatResponse};
                        connection.SendBytes(buff);
                    }
                    else //正数数据收
                    {
                        connection.NoReliableReceive(dataBuffer, 1, length - 1);
                    }
                }
                else
                {
                    Log.Error("listen receive data is error {0}", option);
                }
            }
        }
        private void CreateConnection(EndPoint point, ref UdpServerConnection connection, ref uint convID_)
        {
            convID_ = GenerateConvID();
            connection = new UdpServerConnection(this, point, convID_);

            lock(end2conLock)
            {
                endPoint2Connection.Add(point, connection);
            }
#if DOTNET_CORE
            newConnQueue.Post(connection);
#else
            newConnQueue.Enqueue(connection);
#endif
        }
        private bool GetConnection(EndPoint point, out UdpServerConnection connection)
        {
            bool ret = false;
            lock(end2conLock)
            {
                ret = endPoint2Connection.TryGetValue(point, out connection);
            }
            return ret;
        }
    }
}