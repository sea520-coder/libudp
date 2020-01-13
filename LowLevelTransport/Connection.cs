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
        private readonly Channel<byte[]> recvQueue = Channel.CreateUnbounded<byte[]>();
        public CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
#else
        private Queue<byte[]> recvQueue = new Queue<byte[]>();
#endif
        protected EndPoint remoteEndPoint;
        private AutomaticRepeatRequest arq = null;
        private byte[] peekReceiveBuffer = new byte[MaxSize];
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
#if UNITY
        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        internal long UnixTimeStamp() => (long)(DateTime.UtcNow - UnixEpoch).TotalMilliseconds;
#else
        internal long UnixTimeStamp() => (long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds;
#endif
        public static readonly int MaxSize = 1 << 20; //ushort.MaxValue
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
        public async Task<byte[]> ReceiveAsync(CancellationToken cToken)
        {
            return await recvQueue.Reader.ReadAsync(cToken);
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
        public void SendBytes(byte[] buff, SendOption sendOption = SendOption.None)
        {
            //Console.WriteLine("buf send length {0}", buff.Length);
            if (State != ConnectionState.Connected)
            {
                throw new LowLevelTransportException("Could not send data as this Connection is not connected");
            }

            if (buff.Length >= SendBufferSize() && (sendOption == SendOption.None))
            {
                throw new LowLevelTransportException($"Send byte size:{buff.Length} too large");
            }

            if(buff.Length >= MaxSize && (sendOption == SendOption.FragmentedReliable))
            {
                UInt16 msgType = (UInt16)(( (UInt16)buff[1] << 8 ) | ( (UInt16)buff[0] ));
                Log.Error($"arq type{msgType} Send buff size:{buff.Length} too large");
                throw new LowLevelTransportException($"arq type{msgType} Send buff size:{buff.Length} too large");
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
        public void Tick()
        {
        }
        public void Flush()
        {
        }
        internal void NoReliableReceive(byte[] data, int index, int length)
        {
            byte[] dst = new byte[length];
            Buffer.BlockCopy(data, index, dst, 0, length);
#if DOTNET_CORE
            if(!recvQueue.Writer.TryWrite(dst))
            {
                UInt16 msgType = length > 1 ? (UInt16)(( (UInt16)dst[1] << 8 ) | ( (UInt16)dst[0] )) : (UInt16)0;
                Log.Error($"receive queue overload & last type{msgType}");
            }
#else
            lock (recvQueue)
            {
                recvQueue.Enqueue(dst);
            }
#endif
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
        protected void EncapUnReliableSend(byte[] buff, int length)
        {
            byte[] data = MemoryPool.Malloc(length + 1);
            data[0] = (byte)UdpSendOption.UnReliableData;
            Buffer.BlockCopy(buff, 0, data, 1, length);
            UnReliableSend(data, length + 1);
            MemoryPool.Free(data);
        }
        void EncapReliableSend(byte[] buff, int length)
        {
            byte[] data = MemoryPool.Malloc(length + 1);
            data[0] = (byte)UdpSendOption.ReliableData;
            Buffer.BlockCopy(buff, 0, data, 1, length);
            UnReliableSend(data, length + 1);
            MemoryPool.Free(data);
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
                /*
                if(arq.WaitSend >= 8 * arq.SendWindow)
                {
                    UInt16 msg = buff.Length > 1 ? (UInt16)(( (UInt16)buff[1] << 8 ) | ( (UInt16)buff[0] )) : (UInt16)0;
                    Log.Error("Send fast {0} {1} MsgType{2}", arq.WaitSend, arq.SendWindow, msg);
                    return 0;
                    //是否断开此连接，避免内存耗尽;影响其它连接
                }
                */
                n = arq.Send(buff);
            }
            return n;
        }
        internal int ARQReceive(byte[] data, int index, int length)
        {
            int ret = -1;
            lock (arqLock)
            {
                ret = arq.Input(data, index, length, true);
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

                    var n = arq.Receive(peekReceiveBuffer, peekReceiveBuffer.Length);
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