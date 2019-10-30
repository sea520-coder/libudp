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

        public override void SendBytes(byte[] buff, SendOption sendOption = SendOption.None)
        {
            if(buff.Length > SendBufferSize() && (sendOption == SendOption.None))
            {
                throw new LowLevelTransportException($"Send byte size:{buff.Length} too large");
            }
            if(sendOption == SendOption.FragmentedReliable)
            {
                ARQSend(buff);
            }
            else
            {
                Listener.SendBytes(buff, remoteEndPoint);
            }
        }
        internal UdpServerConnection(UdpConnectionListener listener, EndPoint endPoint, uint convID_)
        {
            this.Listener = listener;
            this.remoteEndPoint = endPoint;
            ARQInit(convID_);
        }
        protected override void RawSend(byte[] data, int length)
        {
            Listener.SendBytes(data, length, remoteEndPoint);
        }
        protected override void Dispose()
        {
            Listener.RemoveConnectionTo(remoteEndPoint);
            StopTimer();
        }
    }
}
