using System;
using System.Collections.Generic;
using System.Net;
using LowLevelTransport.Utils;

namespace LowLevelTransport.Udp
{
    public class UdpServerConnection : Connection
    {
        internal UdpConnectionListener Listener { get; private set; }
        protected override int SendBufferSize() => Listener.SendBufferSize();
        protected override int ReceiveBufferSize() => Listener.ReceiveBufferSize();

        private long keepAliveCurTimestamp = 0;
        internal long KeepAliveCurTimestamp
        {
            get => keepAliveCurTimestamp;
            set => keepAliveCurTimestamp = UnixTimeStamp();
        }

        internal UdpServerConnection(UdpConnectionListener listener, EndPoint endPoint, uint convID_)
        {
            this.Listener = listener;
            this.remoteEndPoint = endPoint;
            ARQInit(convID_);
            lock (stateLock)
            {
                State = ConnectionState.Connected;
            }
        }
        protected override void UnReliableSend(byte[] data, int length)
        {
            lock (stateLock)
            {
                if(State != ConnectionState.Connected)
                {
                    return;
                }
            }
            Listener.SendBytes(data, length, remoteEndPoint);
        }
        protected override void Dispose()
        {
            Listener.RemoveConnectionTo(remoteEndPoint);
            lock(stateLock)
            {
                State = ConnectionState.NotConnected;
            }
        }
    }
}
