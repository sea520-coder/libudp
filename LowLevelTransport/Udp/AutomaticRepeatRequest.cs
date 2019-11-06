using System;
using System.Collections.Generic;
using LowLevelTransport.Utils;

namespace LowLevelTransport.Udp
{
    public class AutomaticRepeatRequest
    {
        public const int RTO_NDL = 30;
        public const int RTO_MIN = 100;
        public const int RTO_DEF = 200;
        public const int RTO_MAX = 60000;
        public const int CMD_PUSH = 81;
        public const int CMD_ACK = 82;
        public const int CMD_WASK = 83;
        public const int CMD_WINS = 84;
        public const int ASK_SEND = 1;
        public const int ASK_TELL = 2;
        public const int WND_SND = 32;
        public const int WND_RCV = 128;
        public const int MTU_DEF = 1400;
        public const int ACK_FAST = 3;
        public const int INTERVAL = 100;
        public const int OVERHEAD = 24;
        public const int DEADLINK = 20;
        public const int THRESH_INIT = 2;
        public const int THRESH_MIN = 2;
        public const int PROBE_INIT = 7000;
        public const int PROBE_LIMIT = 120000;

        public static int encode8u(byte[] p, int offset, byte c)
        {
            p[0 + offset] = c;
            return 1;
        }
        public static int decode8u(byte[] p, int offset, ref byte c)
        {
            c = p[0 + offset];
            return 1;
        }
        public static int encode16u(byte[] p, int offset, ushort w)
        {
            p[0 + offset] = (byte)( w >> 0 );
            p[1 + offset] = (byte)( w >> 8 );
            return 2;
        }
        public static int decode16u(byte[] p, int offset, ref ushort w)
        {
            ushort result = 0;
            result |= (ushort)p[0 + offset];
            result |= (ushort)( p[1 + offset] << 8 );
            w = result;
            return 2;
        }
        public static int encode32u(byte[] p, int offset, uint l)
        {
            p[0 + offset] = (byte)( l >> 0 );
            p[1 + offset] = (byte)( l >> 8 );
            p[2 + offset] = (byte)( l >> 16 );
            p[3 + offset] = (byte)( l >> 24 );
            return 4;
        }
        public static int decode32u(byte[] p, int offset, ref uint l)
        {
            uint result = 0;
            result |= (uint)p[0 + offset];
            result |= (uint)( p[1 + offset] << 8 );
            result |= (uint)( p[2 + offset] << 16 );
            result |= (uint)( p[3 + offset] << 24 );
            l = result;
            return 4;
        }
        static uint _ibound_(uint lower, uint middle, uint upper)
        {
            return Math.Min(Math.Max(lower, middle), upper);
        }
        static int _itimediff(uint later, uint eariler)
        {
            return (int)( later - eariler );
        }

        private static DateTime refTime = DateTime.Now;
        private static UInt32 currentMS()
        {
            var ts = DateTime.Now.Subtract(refTime);
            return (UInt32)ts.TotalMilliseconds;
        }

        internal class Segment
        {
            internal uint conv = 0;
            internal uint cmd = 0;
            internal uint frg = 0;
            internal uint wnd = 0;
            internal uint ts = 0;
            internal uint sn = 0;
            internal uint una = 0;
            internal uint resendts = 0;
            internal uint rto = 0;
            internal uint fastack = 0;
            internal uint xmit = 0;
            internal int acked = 0;
            internal uint len = 0;
            internal byte[] data;

            internal int encode(byte[] ptr, int offset)
            {
                var prev = offset;
                offset += encode32u(ptr, offset, conv);
                offset += encode8u(ptr, offset, (byte)cmd);
                offset += encode8u(ptr, offset, (byte)frg);
                offset += encode16u(ptr, offset, (ushort)wnd);
                offset += encode32u(ptr, offset, ts);
                offset += encode32u(ptr, offset, sn);
                offset += encode32u(ptr, offset, una);
                offset += encode32u(ptr, offset, len);
                
                return offset - prev;
            }
            private static Stack<Segment> msSegmentPool = new Stack<Segment>(256);
            public static Segment Get(int size)
            {
                if(msSegmentPool.Count > 0)
                {
                    var seg = msSegmentPool.Pop();
                    seg.data = new byte[size];
                    if(seg.data == null)
                    {
                        Log.Error("segmentPool data is null");
                    }
                    return seg;
                }
                return new Segment(size);
            }
            public Segment()
            {
                reset();
            }
            public static void Put(Segment seg)
            {
                seg.reset();
                msSegmentPool.Push(seg);
            }
            private Segment(int size)
            {
                data = new byte[size];
                if(data == null)
                {
                    Log.Error("Segment data is null");
                }
            }
            internal void reset()
            {
                conv = 0;
                cmd = 0;
                frg = 0;
                wnd = 0;
                ts = 0;
                sn = 0;
                una = 0;
                rto = 0;
                xmit = 0;
                resendts = 0;
                fastack = 0;
                acked = 0;
                len = 0;
                data = null;
            }
        }
        internal class ackItem
        {
            internal uint sn;
            internal uint ts;
        }

        uint sendUna;
        uint sendNextNumber;
        uint receiveNextNumber;
        uint conv; 
        uint mtu; uint mss; 
        uint state;
        uint ts_recent; 
        uint ts_lastack; 
        uint ssthresh;
        uint rx_rttval; uint rx_srtt; uint rx_rto; uint rx_minrto;
        uint cwnd; 
        uint probe;
        uint current; 
        uint interval; 
        uint ts_flush; 
        uint xmit;
        uint nodelay; 
        uint updated;
        uint ts_probe; uint probe_wait;
        uint dead_link; 
        uint incr;
        uint sendWindow;
        uint receiveWindow;
        uint remoteWindow;
        byte[] buffer;
        Action<byte[], int> output;
        int fastresend;
        int fastlimit;
        int nocwnd;

        List<Segment> sendQueue = new List<Segment>(16);
        List<Segment> receiveQueue = new List<Segment>(16);
        List<Segment> sendBuffer = new List<Segment>(16); //存着远端已确认的数据acked=1等下次删除，和未确认的数据， 要发送的数据
        List<Segment> receiveBuffer = new List<Segment>(16);
        List<ackItem> ackList = new List<ackItem>(16);

        public uint SendWindow { get { return sendWindow; } } //单位(包)
        public uint RecieveWindow { get { return receiveWindow; } }
        public int WaitSend { get { return sendBuffer.Count + sendQueue.Count; } }

        public UInt32 CurrentMS { get { return currentMS(); } }

        public AutomaticRepeatRequest(uint conv_, Action<byte[], int> output_)
        {
            conv = conv_;
            output = output_;
            sendWindow = WND_SND;
            receiveWindow = WND_RCV;
            remoteWindow = WND_RCV;
            mtu = MTU_DEF;
            mss = mtu - OVERHEAD;
            buffer = new byte[( mtu + OVERHEAD ) * 3];
            rx_rto = RTO_DEF;
            rx_minrto = RTO_MIN;
            interval = INTERVAL;
            ts_flush = INTERVAL;
            ssthresh = THRESH_INIT;
            dead_link = DEADLINK;
        }
        public int PeekSize()
        {
            if (0 == receiveQueue.Count)
                return -1;
            var seg = receiveQueue[0];
            if (seg.frg == 0)
                return (int)seg.len;
            if (receiveQueue.Count < seg.frg + 1)
            {
                return -2;
            }

            int length = 0;
            foreach(var item in receiveQueue)
            {
                length += (int)item.len;
                if (0 == item.frg)
                    break;
            }
            return length;
        }
        public int Receive(byte[] buffer, int size)
        {
            if (0 == receiveQueue.Count)
                return -1;
            var peekSize = PeekSize();
            if (peekSize < 0)
                return -2;
            if (peekSize > size)
                return -3;

            var fastRecover = false;
            if (receiveQueue.Count >= receiveWindow)
                fastRecover = true;

            var count = 0;
            var length = 0;
            foreach(var seg in receiveQueue)
            {
                uint frg = seg.frg;
                Buffer.BlockCopy(seg.data, 0, buffer, length, (int)seg.len);
                length += (int)seg.len;
                count++;
                Segment.Put(seg);
                if (0 == frg)
                    break;
            }
            if(count > 0)
            {
                receiveQueue.RemoveRange(0, count);
            }

            //move data from receiveBuffer -> receiveQueue
            count = 0;
            foreach(var seg in receiveBuffer)
            {
                if(seg.sn == receiveNextNumber && receiveQueue.Count < receiveWindow)
                {
                    receiveQueue.Add(seg);
                    receiveNextNumber++;
                    count++;
                }
                else
                {
                    break;
                }
            }
            if(count > 0)
            {
                receiveBuffer.RemoveRange(0, count);
            }

            //fast recover
            if(receiveQueue.Count < receiveWindow && fastRecover)
            {
                probe |= ASK_TELL;
            }

            return length;
        }

        public int Send(byte[] data)
        {
            if (data.Length <= 0)
                return -1;

            var count = 0;
            if(data.Length <= mss)
            {
                count = 1;
            }
            else
            {
                count = (int)(data.Length + (int)mss - 1) / (int)mss;
            }
            if (count > 255) return -2;
            if (count == 0)
                count = 1;

            var readIndex = 0;
            for(var i = 0; i < count; i++)
            {
                var size = Math.Min(data.Length - readIndex, (int)mss);
                var seg = Segment.Get(size);
                Buffer.BlockCopy(data, readIndex, seg.data, 0, size);
                readIndex += size;
                seg.len = (uint)size;
                seg.frg = (byte)(count - i - 1);
                sendQueue.Add(seg);
            }
            return readIndex;
        }
        void UpdateAck(int rtt)
        {
            if(rx_srtt == 0)
            {
                rx_srtt = (uint)rtt;
                rx_rttval = (uint)rtt >> 1;
            }
            else
            {
                int delta = (int)((uint)rtt - rx_srtt);
                if (delta < 0)
                    delta = -delta;
                rx_rttval = ((3 * rx_rttval) + (uint)delta) >> 2;
                rx_srtt = ((7 * rx_srtt) + (uint)rtt) >> 3;
                if (rx_srtt < 1)
                    rx_srtt = 1;
            }
            var rto = (int)(rx_srtt + Math.Max((int)interval, rx_rttval << 2));
            rx_rto = _ibound_(rx_minrto, (uint)rto, RTO_MAX);
        }
        void ShrinkBuf()
        {
            if (sendBuffer.Count > 0)
                sendUna = sendBuffer[0].sn;
            else
                sendUna = sendNextNumber;
        }
        void ParseAck(uint sn)
        {
            if (_itimediff(sn, sendUna) < 0 || _itimediff(sn, sendNextNumber) >= 0)
                return;
            foreach(var seg in sendBuffer) //确认过期的包没有立即从sendBuffer移除，直到收到una包后删除
            {
                if(sn == seg.sn)
                {
                    Segment.Put(seg);
                    seg.acked = 1;
                    break;
                }
                if (_itimediff(sn, seg.sn) < 0)
                    break;
            }
        }
        void ParseUna(uint una)
        {
            var count = 0;
            foreach(var seg in sendBuffer)
            {
                if (_itimediff(una, seg.sn) > 0)
                    count++;
                else
                    break;
            }
            if(count > 0)
            {
                sendBuffer.RemoveRange(0, count); //只有此处删除
            }
        }
        void ParseFastAck(uint sn, uint ts)
        {
            if(_itimediff(sn, sendUna) < 0 || _itimediff(sn, sendNextNumber) >= 0)
            {
                return;
            }
            foreach(var seg in sendBuffer)
            {
                if(_itimediff(sn, seg.sn) < 0)
                {
                    break;
                }
                else if(sn != seg.sn && _itimediff(ts, seg.ts) >= 0)
                {
                    seg.fastack++;
                }
            }
        }
        void AckPush(uint sn, uint ts)
        {
            ackList.Add(new ackItem { sn = sn, ts = ts });
        }
        void ParseData(Segment newseg)
        {
            var sn = newseg.sn;
            if (_itimediff(sn, receiveNextNumber + receiveWindow) >= 0 || _itimediff(sn, receiveNextNumber) < 0)
                return;

            var n = receiveBuffer.Count - 1;
            var insert_idx = 0;
            var repeat = false;
            for(var i = n; i >= 0; i--)
            {
                var seg = receiveBuffer[i];
                if(seg.sn == sn)
                {
                    repeat = true;
                    break;
                }
                if(_itimediff(sn, seg.sn) > 0)
                {
                    insert_idx = i + 1;
                    break;
                }
            }

            if(!repeat)
            {
                if (insert_idx == n + 1)
                    receiveBuffer.Add(newseg);
                else
                    receiveBuffer.Insert(insert_idx, newseg);
            }

            var count = 0;
            foreach(var seg in receiveBuffer)
            {
                if(seg.sn == receiveNextNumber && receiveQueue.Count < receiveWindow)
                {
                    receiveNextNumber++;
                    count++;
                }
                else
                {
                    break;
                }
            }

            for (var i = 0; i < count; i++)
                receiveQueue.Add(receiveBuffer[i]);
            if(count > 0)
                receiveBuffer.RemoveRange(0, count);
        }
        public int Input(byte[] data, int index, int size)
        {
			if (size < OVERHEAD) return -1;
            var prevUna = sendUna;
            int flag = 0;
            uint latest = 0;
            int offset = index;
			ulong inSegs = 0;

            while(true)
            {
                uint tmp_conv = 0;
                byte tmp_cmd = 0;
                byte tmp_frg = 0;
                ushort tmp_wnd = 0;
                uint tmp_ts = 0;
                uint tmp_sn = 0;
                uint tmp_una = 0;
                uint tmp_length = 0;

                if ((size - (offset - index)) < OVERHEAD)
                    break;

                offset += decode32u(data, offset, ref tmp_conv);
                if (conv != tmp_conv)
                    return -1;
                offset += decode8u(data, offset, ref tmp_cmd);
                offset += decode8u(data, offset, ref tmp_frg);
                offset += decode16u(data, offset, ref tmp_wnd);
                offset += decode32u(data, offset, ref tmp_ts);
                offset += decode32u(data, offset, ref tmp_sn);
                offset += decode32u(data, offset, ref tmp_una);
                offset += decode32u(data, offset, ref tmp_length);
                if ((size - (offset - index)) < tmp_length)
                    return -2;
                if (tmp_cmd != CMD_PUSH && tmp_cmd != CMD_ACK && tmp_cmd != CMD_WASK && tmp_cmd != CMD_WINS)
                    return -3;

                remoteWindow = tmp_wnd;
                ParseUna(tmp_una);
                ShrinkBuf();

                if(CMD_ACK == tmp_cmd)
                {
                    ParseAck(tmp_sn);
					ParseFastAck(tmp_sn, tmp_ts);
					flag |= 1;
					latest = tmp_ts;
                 }
                else if(CMD_PUSH == tmp_cmd)
                {
                    if(_itimediff(tmp_sn, receiveNextNumber + receiveWindow) < 0)
                    {
                        AckPush(tmp_sn, tmp_ts);
                        if (_itimediff(tmp_sn, receiveNextNumber) >= 0)
                        {
                            var seg = Segment.Get((int)tmp_length);
                            seg.conv = tmp_conv;
                            seg.cmd = (uint)tmp_cmd;
                            seg.frg = (uint)tmp_frg;
                            seg.wnd = (uint)tmp_wnd;
                            seg.ts = tmp_ts;
                            seg.sn = tmp_sn;
                            seg.una = tmp_una;
                            seg.len = tmp_length;
                            Buffer.BlockCopy(data, offset, seg.data, 0, (int)tmp_length);
                            Log.Info("sn={0}:len={1}", seg.sn, seg.len);
                            ParseData(seg);
                        }
                    }
                }
                else if(CMD_WASK == tmp_cmd)
                {
                    probe |= ASK_TELL;
                }
                else if(CMD_WINS == tmp_cmd)
                {
                }
                else
                {
                    return -3;
                }

				inSegs++;
                offset += (int)tmp_length;
            }

            if (flag != 0)
			{
                var current = currentMS();
				if(_itimediff(current, latest) >= 0)
				{
					UpdateAck(_itimediff(current, latest));
				}
			}
            if(_itimediff(sendUna, prevUna) > 0)
            {
                if(cwnd < remoteWindow)
                {
                    uint tmp_mss = mss;
                    if(cwnd < ssthresh)
                    {
                        cwnd++;
                        incr += tmp_mss;
                    }
                    else
                    {
                        if (incr < tmp_mss)
                            incr = tmp_mss;
                        incr += (tmp_mss * tmp_mss / incr) + ( tmp_mss / 16 );
                        if(_itimediff((cwnd + 1) * tmp_mss, incr) <= 0)
                            cwnd++;
                    }
                    if (cwnd > remoteWindow)
                    {
                        cwnd = remoteWindow;
                        incr = remoteWindow * tmp_mss;
                    }
                }
            }

            return 0;
        }
        ushort WindowUnUsed()
        {
            if (receiveQueue.Count < receiveWindow)
                return (ushort)( receiveWindow - receiveQueue.Count );
            return 0;
        }
        void Flush()
        {
            var seg = new Segment();
            seg.conv = conv;
            seg.cmd = CMD_ACK;
            seg.wnd = (uint)( WindowUnUsed() );
            seg.una = receiveNextNumber;

            var writeIndex = 0;

            Action<int> makeSpace = (space) =>
            {
                if(writeIndex + space > mtu)
                {
                    output(buffer, writeIndex);
                    writeIndex = 0;
                }
            };

            Action flushBuffer = () =>
            {
                if(writeIndex > 0)
                {
                    output(buffer, writeIndex);
                }
            };

            // flush acknowledges
            for(var i = 0; i < ackList.Count; i++) //模拟丢大量确认包?
            {
                makeSpace(OVERHEAD);
                var ack = ackList[i];
                if(ack.sn >= receiveNextNumber || ackList.Count - 1 == i)
                {
                    seg.sn = ack.sn;
                    seg.ts = ack.ts;
                    writeIndex += seg.encode(buffer, writeIndex);
                }
            }
            ackList.Clear();

            var current = currentMS();

            if(0 == remoteWindow)
            {
                if(0 == probe_wait)
                {
                    probe_wait = PROBE_INIT;
                    ts_probe = current + probe_wait;
                }
                else
                {
                    if(_itimediff(current, ts_probe) >= 0)
                    {
                        if (probe_wait < PROBE_INIT)
                            probe_wait = PROBE_INIT;
                        probe_wait += probe_wait / 2;
                        if (probe_wait > PROBE_LIMIT)
                            probe_wait = PROBE_LIMIT;
                        ts_probe = current + probe_wait;
                        probe |= ASK_SEND;
                    }
                }
            }
            else
            {
                ts_probe = 0;
                probe_wait = 0;
            }

            if((probe & ASK_SEND) != 0)
            {
                seg.cmd = CMD_WASK;
                makeSpace(OVERHEAD);
                writeIndex += seg.encode(buffer, writeIndex);
            }
            if((probe & ASK_TELL) != 0)
            {
                seg.cmd = CMD_WINS;
                makeSpace(OVERHEAD);
                writeIndex += seg.encode(buffer, writeIndex);
            }

            probe = 0;

            var cwnd_ = Math.Min(sendWindow, remoteWindow);
            if (0 == nocwnd) cwnd_ = Math.Min(cwnd, cwnd_);

            //sendQueue -> sendBuffer
            var newSegsCount = 0;
            for(var k = 0; k < sendQueue.Count; k++)
            {
                if (_itimediff(sendNextNumber, sendUna + cwnd_) >= 0)
                    break;

                var newseg = sendQueue[k];
                newseg.conv = conv;
                newseg.cmd = CMD_PUSH;
                newseg.wnd = seg.wnd;
                newseg.sn = sendNextNumber++;
                newseg.una = receiveNextNumber;
                newseg.resendts = current;
                newseg.rto = rx_rto;
                newseg.fastack = 0;
                newseg.xmit = 0;
                sendBuffer.Add(newseg);
                newSegsCount++;
            }
            if(newSegsCount > 0)
            {
                sendQueue.RemoveRange(0, newSegsCount);
            }

            var resent = (fastresend > 0) ? (uint)fastresend : 0xffffffff;

            current = currentMS();
            ulong change = 0;
            ulong lost = 0;
            ulong lostSegs = 0;
            ulong fastRetransSegs = 0;
            ulong earlyRetransSegs = 0;
            var minrto = (int)interval;

            for(var k = 0; k < sendBuffer.Count; k++)
            {
                var segment = sendBuffer[k];
                var needsend = false;
                if(segment.acked == 1)
                {
                    continue;
                }
                if(segment.xmit == 0)
                {
                    needsend = true;
                    segment.rto = rx_rto;
                    segment.resendts = current + segment.rto;
                }
                else if(_itimediff(current, segment.resendts) >= 0)
                {
                    needsend = true;
                    if(nodelay == 0)
                    {
                        segment.rto += rx_rto;
                    }
                    else
                    {
                        segment.rto += rx_rto / 2;
                    }
                    segment.resendts = current + segment.rto;
                    lost++;
                    lostSegs++;
                }
                else if(segment.fastack >= resent)
                {
                    needsend = true;
                    segment.fastack = 0;
                    segment.rto = rx_rto;
                    segment.resendts = current + segment.rto;
                    change++;
                    fastRetransSegs++;
                }
                else if(segment.fastack > 0 && newSegsCount == 0)
                {
                    needsend = true;
                    segment.fastack = 0;
                    segment.rto = rx_rto;
                    segment.resendts = current + segment.rto;
                    change++;
                    earlyRetransSegs++;
                }
                if(needsend)
                {
                    segment.xmit++;
                    segment.ts = current;
                    segment.wnd = seg.wnd;
                    segment.una = seg.una;

                    var need = OVERHEAD + segment.len;
                    makeSpace((int)need);
                    writeIndex += segment.encode(buffer, writeIndex);
                    if(segment.len > 0)
                    {
                        Buffer.BlockCopy(segment.data, 0, buffer, writeIndex, (int)segment.len);
                        writeIndex += (int)segment.len;
                    }
                    if(segment.xmit >= dead_link)
                    {
                        state = 0xFFFFFFFF;
                    }
                }
            }

            flushBuffer();

            if(change > 0)
            {
                uint inflight = sendNextNumber - sendUna;
                ssthresh = inflight / 2;
                if (ssthresh < THRESH_MIN)
                    ssthresh = THRESH_MIN;
                cwnd = ssthresh + resent;
                incr = cwnd * mss;
            }
            if(lost > 0)
            {
                ssthresh = cwnd / 2;
                if (ssthresh < THRESH_MIN)
                    ssthresh = THRESH_MIN;
                cwnd = 1;
                incr = mss;
            }
            if(cwnd < 1)
            {
                cwnd = 1;
                incr = mss;
            }
        }
        internal void Update()
        {
            var current = currentMS();

            if(updated == 0)
            {
                updated = 1;
                ts_flush = current;
            }
            var slap = _itimediff(current, ts_flush);
            if(slap >= 10000 || slap < -10000)
            {
                ts_flush = current;
                slap = 0;
            }
            if(slap >= 0)
            {
                ts_flush += interval;
                if(_itimediff(current, ts_flush) >= 0)
                    ts_flush = current + interval;
                
                Flush();
            }
        }
        internal uint Check()
        {
            var current = currentMS();

            var ts_flush_ = ts_flush;
            var tm_flush_ = 0x7fffffff;
            var tm_packet = 0x7fffffff;
            var minimal = 0;

            if (updated == 0)
                return 0;
				
            if(_itimediff(current, ts_flush_) >= 10000 ||
                _itimediff(current, ts_flush_) < -10000)
            {
                ts_flush_ = current;
            }
            if(_itimediff(current, ts_flush_) >= 0)
            {
                return 0;
            }
            tm_flush_ = _itimediff(ts_flush_, current);
            foreach(var seg in sendBuffer)
            {
                var diff = _itimediff(seg.resendts, current);
                if (diff <= 0)
                    return 0;
                if (diff < tm_packet)
                    tm_packet = (int)diff;
            }

            minimal = Math.Min(tm_packet, tm_flush_);
            minimal = Math.Min(minimal, (int)interval);

            return (uint)minimal;
        }
        internal int SetMTU(int mtu_)
        {
            if (mtu_ < 50 || mtu_ < (int)OVERHEAD)
                return -1;

            byte[] buffer_ = new byte[(mtu_ + OVERHEAD) * 3];
            if (null == buffer_)
                return -2;

            mtu = (uint)mtu_;
            mss = mtu - OVERHEAD;
            buffer = buffer_;
            return 0;
        }
        internal int Interval(int interval_)
        {
            if (interval_ > 5000)
                interval_ = 5000;
            else if (interval_ < 10)
                interval_ = 10;
            interval = (uint)interval_;
            return 0;
        }
        internal int Interval()
        {
            return (int)interval;
        }
        internal int NoDelay(int nodelay_, int interval_, int resend_, int nc_)
        {
            if(nodelay >= 0)
            {
                nodelay = (uint)nodelay_;
                if (nodelay_ != 0)
                    rx_minrto = RTO_NDL;
                else
                    rx_minrto = RTO_MIN;
            }
            if(interval_ >= 0)
            {
                Interval(interval_);
            }
            if (resend_ >= 0)
            {
                fastresend = resend_;
            }
            if (nc_ >= 0)
            {
                nocwnd = nc_;
            }
            return 0;
        }
        internal int WindowSize(int sendWindow_, int receiveWindow_)
        {
            if (sendWindow_ > 0)
                sendWindow = (uint)sendWindow_;
            if (receiveWindow_ > 0)
                receiveWindow = (uint)receiveWindow_;
            return 0;
        }
    }
}
