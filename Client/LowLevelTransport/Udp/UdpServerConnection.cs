using System;
using System.Collections.Generic;
using System.Text;
using System.Net;

namespace LowLevelTransport.Udp
{
    public class UdpServerConnection : Connection
    {
        private readonly byte[] dataBuffer = new byte[ushort.MaxValue];
        private readonly EndPoint remoteEndPoint;
        public UdpConnectionListener Listener { get; private set; }
        public Queue<byte[]> recvQueue = new Queue<byte[]>();

        public override int SendBufferSize() => Listener.SendBufferSize();
        public override int ReceiveBufferSize() => Listener.ReceiveBufferSize();

        internal UdpServerConnection(UdpConnectionListener listener, EndPoint endPoint)
        {
            this.Listener = listener;
            this.remoteEndPoint = endPoint;

            ARQInit();
        }
        public override void RawSend(byte[] data, int length)
        {
            Listener.SendBytes(data, length, remoteEndPoint);
        }
        public override void SendBytes(byte[] buff, SendOption sendOption = SendOption.None)
        {
            if(buff.Length > SendBufferSize() && (sendOption == SendOption.None))
            {
                throw new LowLevelTransportException($"Send byte size:{buff.Length} too large");
            }
            if(sendOption == SendOption.FragmentedReliable)
            {
                aRQ.Send(buff);
            }
            else
            {
                Listener.SendBytes(buff, remoteEndPoint);
            }
        }
        public override byte[] Receive()
        {
            if (recvQueue.Count != 0)
                return recvQueue.Dequeue();
            return null;
        }
        public override void Dispose()
        {
            Listener.RemoveConnectionTo(remoteEndPoint);
            StopTimer();
        }
    }
}
