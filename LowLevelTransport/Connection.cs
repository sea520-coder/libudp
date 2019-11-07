using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using LowLevelTransport.Utils;
using System.Collections.Generic;
using LowLevelTransport.Udp;
#if DOTNET_CORE
using System.Threading.Channels;
#endif

namespace LowLevelTransport
{
    public class Connection
    {
#if DOTNET_CORE
        private readonly Channel<byte[]> recvQueue = Channel.CreateBounded<byte[]>(512);
        public CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
#else
        private Queue<byte[]> recvQueue = new Queue<byte[]>();
#endif
        protected EndPoint remoteEndPoint;
        private AutomaticRepeatRequest arq = null;
        private byte[] peekReceiveBuffer = new byte[ushort.MaxValue];
        private object arqLock = new object();
        private Timer tickTimer;
        private volatile bool isClosed;
        public bool IsClosed
        {
            get
            {
                lock (recvQueue)
                {
                    return isClosed;
                }
            }
        }
        private volatile ConnectionState state;
        protected ConnectionState State
        {
            get => state;
            set => state = value;
        }
        protected object stateLock = new object();

        public Action TryReconnectCallback;
        public Action<bool> ReconnectFinishCallback;

        protected virtual int SendBufferSize() { throw new NotImplementedException(); }
        protected virtual int ReceiveBufferSize() { throw new NotImplementedException();  }
#if DOTNET_CORE
        public byte[] Receive()
        { 
            lock (recvQueue)
            {
                if(isClosed)
                {
                    throw new LowLevelTransportException("Receive From a Not Connected Connection");
                }
                byte[] p;  
                return recvQueue.Reader.TryRead(out p) ? p : null;
            }
        }
        public Task<byte[]> ReceiveAsync(CancellationToken cToken, TimeSpan t)
        {
            var task = recvQueue.Reader.ReadAsync(cToken);
            return task.AsTask().TimeoutAfter<byte[]>((int)t.TotalMilliseconds);
        }
#else
        public byte[] Receive()
        {
            lock (recvQueue)
            {
                if(isClosed)
                {
                    throw new LowLevelTransportException("Receive From a Not Connected Connection");
                }
                if (recvQueue.Count != 0)
                    return recvQueue.Dequeue();

                return null;
            }
        }
#endif
        internal void NoReliableReceive(byte[] data, int index, int length)
        {
            byte[] dst = new byte[length];
            Buffer.BlockCopy(data, index, dst, 0, length);
            
#if DOTNET_CORE
            if(!recvQueue.Writer.TryWrite(dst))
            {
                throw new LowLevelTransportException("receive queue overload");
            }
#else
            lock (recvQueue)
            {
                recvQueue.Enqueue(dst);
            }
#endif
        }
        public void Close()
        {
            lock (recvQueue)
            {
                if(isClosed)
                {
                    return;
                }
                isClosed = true;

                Dispose();
                StopTimer();
            } 
        }
        protected void InvokeDisconnected(Exception e = null)
        {
            Log.Error("Disconnect IP:{0} Exception:{1}", remoteEndPoint.ToString(), e?.Message);
            Close();
        }
        protected virtual void Dispose()
        {
            throw new NotImplementedException();
        }
        public void Tick()
        {

        }
        public void Flush()
        {

        }
        private void StartTick()
        {
            tickTimer = new Timer((object o) => Update(), null, Interval(), Timeout.Infinite);
        }
        internal void StopTimer() => tickTimer.Change(Timeout.Infinite, Timeout.Infinite);
        internal void ARQInit(uint convID_)
        {
            lock (arqLock)
            {
                arq = new AutomaticRepeatRequest(convID_, EncapReliableSend);
                arq.WindowSize((int)ARQOption.SendWindow, (int)ARQOption.RecieveWindow); 
                arq.NoDelay((int)ARQOption.NoDelay, (int)ARQOption.Interval, (int)ARQOption.Resend, (int)ARQOption.NC);
                arq.SetMTU((int)ARQOption.MTU); 
            }
            StartTick();
        }
        private void Update()
        {
            lock (arqLock)
            {
                arq.Update();
                tickTimer.Change(arq.Check(), Timeout.Infinite);
            }
        }
        private int Interval()
        {
            lock (arqLock)
            {
                return arq.Interval();
            }
        }
        public void SendBytes(byte[] buff, SendOption sendOption = SendOption.None)
        {
            if (State != ConnectionState.Connected)
            {
                throw new LowLevelTransportException("Could not send data as this Connection is not connected");
            }

            if (buff.Length > SendBufferSize() && (sendOption == SendOption.None))
            {
                throw new LowLevelTransportException($"Send byte size:{buff.Length} too large");
            }

            if (sendOption == SendOption.FragmentedReliable)
            {
                ARQSend(buff);
            }
            else
            {
                EncapUnReliableSend(buff, buff.Length);
            }
        }
        protected void EncapUnReliableSend(byte[] buff, int length)
        {
            byte[] data = new byte[length + 1];
            data[0] = (byte)UdpSendOption.UnReliableData;
            Buffer.BlockCopy(buff, 0, data, 1, length);
            UnReliableSend(data, data.Length);
            data = null;
        }
        void EncapReliableSend(byte[] buff, int length)
        {
            byte[] data = new byte[length + 1];
            data[0] = (byte)UdpSendOption.ReliableData;
            Buffer.BlockCopy(buff, 0, data, 1, length);
            UnReliableSend(data, data.Length);
            data = null;
        }
        protected virtual void UnReliableSend(byte[] data, int length)
        {
            throw new NotImplementedException();
        }
        protected int ARQSend(byte[] buff)
        {
            int n = 0;
            lock (arqLock)
            {
                if(arq.WaitSend >= 2 * arq.SendWindow) //发送缓存积累
                {
                    Log.Error("ARQSend {0} {1}", arq.WaitSend, arq.SendWindow);
                    return 0;
                    //是否断开此连接，避免内存耗尽;影响其它连接
                }

                n = arq.Send(buff);
            }
            return n;
        }
        internal int ARQReceive(byte[] data, int index, int length)
        {
            int ret = -1;
            lock (arqLock)
            {
                ret = arq.Input(data, index, length, true); //扔数据给arq
                if(ret < 0)
                {
                    return ret;
                }

                while(true)
                {
                    var size = arq.PeekSize();
                    if(size <= 0)
                    {
                        break;
                    }

                    var n = arq.Receive(peekReceiveBuffer, size); //从arq中取数据
                    if(n > 0) //数据包
                    {

                        NoReliableReceive(peekReceiveBuffer, 0, n);

                    }
                    else //确认包 or 错误包 or 部分包
                    {
                    }
                }
            }
            return ret;
        }
    }
}