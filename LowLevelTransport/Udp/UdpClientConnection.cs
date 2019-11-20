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
        private Thread receiveThread;
        public UdpClientConnection(string host, int port, string remoteHost, int remotePort, 
            int sendBufferSize = (int)ClientSocketBufferOption.SendSize, int receiveBufferSize = (int)ClientSocketBufferOption.ReceiveSize)
        {
            client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                SendBufferSize = sendBufferSize,
                ReceiveBufferSize = receiveBufferSize,
                SendTimeout = 500,
                ReceiveTimeout = 500
            };
            endPoint = new IPEndPoint(IPAddress.Parse(host), port);
            remoteEndPoint = new IPEndPoint(IPAddress.Parse(remoteHost), remotePort);
#if WIN
            uint SIO_UDP_CONNRESET = 2550136844; //errorcode = 10054
            client.IOControl((int)SIO_UDP_CONNRESET, new byte[1], null);
#endif
        }
        public UdpClientConnection(EndPoint ep, int flushInterval = 10, 
            int sendBufferSize = (int)ClientSocketBufferOption.SendSize, int receiveBufferSize = (int)ClientSocketBufferOption.ReceiveSize)
        {
            endPoint = new IPEndPoint(IPAddress.Any, 0);
            remoteEndPoint = ep;
            client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                SendBufferSize = sendBufferSize,
                ReceiveBufferSize = receiveBufferSize,
                SendTimeout = 500,
                ReceiveTimeout = 500,
            };
        }
        public Task<bool> ConnectAsync(int timeout = (int)ConnectOption.Timeout)
        {
            Log.Info("try connect a server");
            lock (stateLock)
            {
                if(State != ConnectionState.NotConnected)
                {
                    throw new InvalidOperationException("Cannot connect as the Connection is already connected.");
                }
                State = ConnectionState.Connecting;
            }

            try
            {
                client.Bind(endPoint);
            }
            catch(SocketException e)
            {
                State = ConnectionState.NotConnected;
                throw new LowLevelTransportException("A socket exception occured while binding to the port.", e);
            }

            receiveThread = new Thread(ReceiveMsg)
            {
                IsBackground = true
            };
            receiveThread.Start();

            byte[] data = { (byte)UdpSendOption.CreateConnection };
            EncapUnReliableSend(data, data.Length);
            var timer = new Timer((object obj) => { tcs.TrySetResult(false); }, null, timeout, Timeout.Infinite);
            return tcs.Task;
        }
        protected override void UnReliableSend(byte[] data, int length)
        {
            try
            {
                client.SendTo(data, 0, length, SocketFlags.None, remoteEndPoint);
            }
            catch(ObjectDisposedException e)
            {
                Log.Info($"UnReliableSend ObjectDisposedException: {e.Message}");
                return;
            }
            catch(SocketException e)
            {
                Log.Info($"UnReliableSend SocketException: {e.Message}");
                return;
            }
            catch (Exception e)
            {
                HandleDisconnect(e);
            }
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
            
            receiveThread.Abort();
            StopKeepAliveTimer();
            client.Close();
        }
        private void ReceiveMsg()
        {
            while(true)
            {
                if(!client.Poll(1000000, SelectMode.SelectRead))
                {
                    continue;
                }

                EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                int length = -1;
                try
                {
                    length = client.ReceiveFrom(dataBuffer, 0, dataBuffer.Length, SocketFlags.None, ref remoteEP);
                }
                catch (SocketException e)
                {
                    HandleDisconnect(e);
                    return;
                }
                catch(ObjectDisposedException)
                {
                    return;
                }

                if (length <= 1)
                {
                    Log.Error($"ReceiveMsg: length{0}", length);
                    HandleDisconnect(new Exception("Received Zero Bytes in ReceiveFrom"));
                    continue;
                }
                
                Log.Info("receiveCallback {0} {1}", length, dataBuffer[0]);
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
                    else if(length == 6 && dataBuffer[1] == (byte)UdpSendOption.CreateConnectionResponse)
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
        private void HandleDisconnect(Exception e)
        {
            bool invoke = false;
            lock (stateLock)
            {
                if(State == ConnectionState.Connected)
                {
                    State = ConnectionState.Disconnecting;
                    invoke = true;
                }
            }
            if(invoke)
            {
                InvokeDisconnected(e);
            }
        }
    }
}
