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
        public Action TryReconnectCallback;
        public Action<bool> ReconnectFinishCallback;

        public AutomaticRepeatRequest aRQ = null;
        private UInt32 mNextUpdateTime = 0;
        private Timer tickTimer;
        private volatile bool isClosed;
        public bool IsClosed
        {
            get
            {
                return isClosed;
            }
        }
        protected ConnectionState State
        {
            get => state;
            set => state = value;
        }
        private volatile ConnectionState state;
        public virtual int SendBufferSize() { throw new NotImplementedException(); }
        public virtual int ReceiveBufferSize() { throw new NotImplementedException();  }
        public virtual void SendBytes(byte[] buff, SendOption sendOption = SendOption.None)
        {
            throw new NotImplementedException();
        }
        public virtual void RawSend(byte[] data, int length)
        {
            throw new NotImplementedException();
        }
        public virtual byte[] Receive()
        {
            throw new NotImplementedException();
        }
        public void Close()
        {
            if(isClosed)
            {
                return;
            }
            isClosed = true;
            Dispose();
        }
        public virtual void Dispose()
        {
            throw new NotImplementedException();
        }
        public void Tick()
        {

        }
        public void Flush()
        {

        }
        public void ARQInit()
        {
            aRQ = new AutomaticRepeatRequest(1, RawSend);
            aRQ.WindowSize(128, 128);
            aRQ.NoDelay(1, 40, 0, 0);
            StartTick();
        }

        private void StartTick()
        {
            tickTimer = new Timer((object o) => { aRQ.Update(); tickTimer.Change(aRQ.Check(), Timeout.Infinite); }, null, aRQ.interval, Timeout.Infinite);
        }
        public void StopTimer() => tickTimer.Change(Timeout.Infinite, Timeout.Infinite);

        protected void SendDisconnect()
        {
            var data = new byte[] { (byte)UdpSendOption.Disconnect };
            SendBytes(data);
        }
    }
}