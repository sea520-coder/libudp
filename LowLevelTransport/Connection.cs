using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using LowLevelTransport.Utils;
using System.Collections.Generic;
using LowLevelTransport.Udp;

namespace LowLevelTransport
{
    public class Connection
    {
        private Queue<byte[]> recvQueue = new Queue<byte[]>();
        protected EndPoint remoteEndPoint;
        private AutomaticRepeatRequest arq = null;
        private readonly object arqLock = new object();
        private UInt32 mNextUpdateTime = 0;
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

        private Action TryReconnectCallback;
        private Action<bool> ReconnectFinishCallback;
        
        protected virtual int SendBufferSize() { throw new NotImplementedException(); }
        protected virtual int ReceiveBufferSize() { throw new NotImplementedException();  }

        public virtual void SendBytes(byte[] buff, SendOption sendOption = SendOption.None)
        {
            throw new NotImplementedException();
        }
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
        internal void NoReliableReceive(byte[] dataBuffer, int length)
        {
            byte[] dst = new byte[length];
            Buffer.BlockCopy(dataBuffer, 0, dst, 0, length);
            recvQueue.Enqueue(dst);
        }
        public void Close()
        {
            if(isClosed)
            {
                return;
            }
            isClosed = true;

            Dispose();
            StopTimer();
        }

        protected virtual void RawSend(byte[] data, int length)
        {
            throw new NotImplementedException();
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
        protected void SendDisconnect()
        {
            var data = new byte[] { (byte)UdpSendOption.Disconnect };
            SendBytes(data);
        }
        internal void ARQInit(uint convID_) //是否分开
        {
            lock (arqLock)
            {
                arq = new AutomaticRepeatRequest(convID_, RawSend);
                arq.WindowSize(128, 128); //根据预计带宽来填[32, 256] 每秒钟要发多少包
                arq.NoDelay(1, 40, 0, 1);
                arq.Interval(10); //update 10ms
                arq.SetMTU(470); 
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
        protected int ARQSend(byte[] buff)
        {
            int n = 0;
            lock (arqLock)
            {
                if(arq.WaitSend >= arq.SendWindow) //发送缓存积累(发的太快，接收能力慢)
                {
                    Log.Error("ARQSend {0} {1}", arq.WaitSend, arq.SendWindow);
                    return 0;
                    //是否断开此连接，而不影响其它连接
                }

                n = arq.Send(buff);
            }
            return n;
        }
        internal int ARQReceive(byte[] dataBuffer, int length)
        {
            int ret = -1;
            lock (arqLock)
            {
                ret = arq.Input(dataBuffer, length);
                if(ret < 0)
                {
                    return ret;
                }
               
                int index = 0;
                while(true)
                {
                    var size = arq.PeekSize();
                    if(size <= 0)
                        break;
                    
                    var n = arq.Receive(dataBuffer, index, size);
                    if(n > 0) //数据包
                    {
                        byte[] dst = new byte[n];
                        Buffer.BlockCopy(dataBuffer, index, dst, 0, n);
                        recvQueue.Enqueue(dst);
                        index += n;
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