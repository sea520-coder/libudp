using System;

namespace LowLevelTransport
{
    public enum SendOption : byte
    {
        None = 0,
        FragmentedReliable = 1
    }

    public enum UdpSendOption : byte
    {
        Hello = 8,

        Disconnect = 9,

        Acknowledgement = 10,

        HandShake = 11,

        HandShakeDone = 12,
    }

    public enum ConnectionState
    {
        NotConnected,
        Connecting,
        Connected,
        Disconnecting,
    }
}
