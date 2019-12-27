using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using LowLevelTransport;
using LowLevelTransport.Udp;

namespace Client
{
    class Program
    {
        static string remoteHost = "192.168.237.130";
        static string host = "192.168.237.130";
        //static string remoteHost = "172.26.187.156";
        //static string host = "172.26.187.156";

        public static async void ConnectHost(Object obj)
        {
            int port = (int)obj;
            UdpClientConnection conn = new UdpClientConnection(host, port, remoteHost, 1230);

            var connectOK = await conn.ConnectAsync();
            if(connectOK)
            {
                Console.WriteLine("Client");
                Thread.Sleep(1000);
                sendMsg(conn, port);
            }
            else
            {
                Console.WriteLine("client connect server failed");
            }
            conn.Close();
        }
        static void Main(string[] args)
        {
            for(int i = 0; i < 20; i++)
            {
                Thread t1 = new Thread(ConnectHost);
                t1.Start(1000+i);
            }
            Thread.Sleep(100000);
        }
        static void sendMsg(Connection conn, int port)
        {
            byte[] buff = new byte[4]{0,0,0,0};
            conn.SendBytes(buff, SendOption.FragmentedReliable);
            while(true)
            {
                byte[] recv = conn.Receive();
                if(recv != null)
                {
                    Console.WriteLine("recv.Length {0}", recv.Length);
                }
            }
        }
    }
}