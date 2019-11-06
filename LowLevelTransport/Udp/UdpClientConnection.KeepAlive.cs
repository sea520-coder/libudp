using System;
using System.Threading;
using LowLevelTransport;

namespace LowLevelTransport.Udp
{
    public partial class UdpClientConnection
    {
        private long reconnectTimes = 0;
        private bool init = false;
        private bool keepAliveTimerDisposed = false;
        private Timer keepAliveTimer;
#if UNITY
        protected static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        protected long UnixTimeStamp() => (long)(DateTime.UtcNow - UnixEpoch).TotalMilliseconds;
#else
        protected long UnixTimeStamp() => (long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds;
#endif
        protected void InitKeepAliveTimer()
        {
            init = true;
            keepAliveTimer = new Timer(KeepAlive, null, (int)KeepAliveOption.KeepAliveInterval, (int)KeepAliveOption.KeepAliveInterval);
        }
        private void KeepAlive(object obj)
        {
            if(Interlocked.Read(ref reconnectTimes) > (long)KeepAliveOption.ReconnectLimit)
            {
                HandleDisconnect(new Exception("Reconnect too many times"));
                return;
            }
            Interlocked.Add(ref reconnectTimes, 1);
            
            byte[] data = new byte[1] { (byte)UdpSendOption.Heartbeat };
            SendBytes(data);
        }
        internal void HandleHeartbeat()
        {
            Interlocked.Exchange(ref reconnectTimes, 0);
        }
        private void StopKeepAliveTimer()
        {
            if(!keepAliveTimerDisposed && init)
            {
                keepAliveTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
            keepAliveTimerDisposed = true;
        }
    }
}