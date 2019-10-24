using System;

namespace LowLevelTransport
{
    public class LowLevelTransportException : Exception
    {
        internal LowLevelTransportException(string msg) : base(msg) { }
        internal LowLevelTransportException(string msg, Exception e) : base(msg, e) { }
    }
}
