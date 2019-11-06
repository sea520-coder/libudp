namespace LowLevelTransport
{
    public enum SendOption : byte
    {
        None = 0,
        FragmentedReliable = 1
    }

    public enum UdpSendOption : byte
    {
        UnReliableData = 0,
        // Reliable data
        ReliableData = 1,
        // No Reliable data
        CreateConnection = 2,
        CreateConnectionResponse = 3,
        Heartbeat = 4,
        HeartbeatResponse = 5,
        Disconnect = 6,
       
    }

    public enum ConnectionState
    {
        NotConnected = 0,
        Connecting = 1,
        Connected = 2,
        Disconnecting = 3,
    }

    public enum KeepAliveOption
    {
        ReconnectLimit = 10,
        KeepAliveInterval = 60000, // milliseconds
    }


    public enum ARQOption
    {
        SendWindow = 256, //(包)
        RecieveWindow = 256, //(包)
        MTU = 1400, //
        Interval = 40, //发送间隔，越小发的越快
        Resend = 0, //0 close; 2 2次ACK跨越将会直接重传
        NC = 0, // 0 open; 1 close 是否关闭流控
        NoDelay = 0, // 0不启用; 1 启用nodelay模式
    }
    public enum ConvIDOption
    {
        Max = 100000, //convid生成的最大值
    }
    public enum SocketBufferOption
    {
        SendSize = 1000000, //Byte
        ReceiveSize = 1000000, //Byte
    }
    public enum ConnectOption
    {
        Timeout = 3000, //millisecond
    }
}