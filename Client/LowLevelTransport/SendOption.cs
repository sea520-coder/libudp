namespace LowLevelTransport
{
    public enum SendOption : byte
    {
        None = 0,
        FragmentedReliable = 1
    }

    public enum UdpSendOption : byte
    {
        CreateConnection = 0,
        CreateConnectionResponse = 1,
        Heartbeat = 2,
        HeartbeatResponse = 3,
        Disconnect = 4,
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
        SendWindow = 128, // unit package //根据预计带宽来填值[32, 256] 每秒钟要发多少包
        RecieveWindow = 128, // unit package
        MTU = 470, //
        Interval = 40, //Update's interval
        Resend = 0, //0 close; 2 2次ACK跨越将会直接重传
        NC = 1, // 0 open; 1 close 是否关闭流控
        NoDelay = 1, // 0不启用; 1 启用nodelay模式
    }
    public enum ConvIDOption
    {
        Max = 100000, //convid生成的最大值
    }
    public enum SocketBufferOption
    {
        SendSize = 20480, //Byte
        ReceiveSize = 20480, //Byte
    }
    public enum ConnectOption
    {
        Timeout = 3000, //second
    }
}
