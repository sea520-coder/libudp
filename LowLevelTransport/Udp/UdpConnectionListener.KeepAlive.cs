using System.Threading;
using System.Collections.Generic;
using System.Net;
using LowLevelTransport.Utils;
using System.Diagnostics;

namespace LowLevelTransport.Udp
{
    public partial class UdpConnectionListener
    {
        private bool init = false;
        private bool checkTimeoutTimerDisposed = false;
        private Timer checkTimeoutTimer;
        private void InitCheckTimeoutTimer()
        {
            Debug.Assert((int)ServerKeepAliveOption.CheckTimeoutInterval >= 
                (int)ClientKeepAliveOption.ReconnectLimit * (int)ClientKeepAliveOption.KeepAliveInterval);
            init = true;
            checkTimeoutTimer = new Timer(CheckTimeout, null, 
                (int)ServerKeepAliveOption.CheckTimeoutInterval, (int)ServerKeepAliveOption.CheckTimeoutInterval);
        }
        private void CheckTimeout(object obj)
        {
            var stack = new Stack<UdpServerConnection>();

            lock(end2conLock)
            {
                Dictionary<EndPoint, UdpServerConnection>.ValueCollection valueColl =
                endPoint2Connection.Values;
                foreach(var connection in valueColl)
                {
                    if((0L != connection.KeepAliveCurTimestamp) && (connection.KeepAliveCurTimestamp + (long)ServerKeepAliveOption.CheckTimeoutInterval <
                        connection.UnixTimeStamp()))
                    {
                        stack.Push(connection);
                    }
                }
            }
            
            while(stack.Count > 0)
            {
                var connection = stack.Pop();
                connection.Close();
                connection = null;
                Log.Info("check timeout close an connection");
            }
        }
        private void StopCheckTimeoutTimer()
        {
            if(!checkTimeoutTimerDisposed && init)
            {
                checkTimeoutTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
            checkTimeoutTimerDisposed = true;
        }
    }
}