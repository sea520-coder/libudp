namespace LowLevelTransport
{
    public enum SendOption : byte
    {
        None = 0,
        FragmentedReliable = 1
    }

    public enum UdpSendOption : byte
    {
        //No Reliable data
        CreateConnection = 0,
        CreateConnectionResponse = 1,
        Heartbeat = 2,
        HeartbeatResponse = 3,
        Disconnect = 4,
        UnReliableData = 5,
        // Reliable data
        ReliableData = 6,
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
        SendWindow = 256, // unit package //根据预计带宽来填值[32, 256]
        RecieveWindow = 256, // unit package
        MTU = 1400, //
        Interval = 40, //Update's interval
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
        SendSize = 60000, //Byte
        ReceiveSize = 1000000, //Byte
    }
    public enum ConnectOption
    {
        Timeout = 3000, //second
    }
}