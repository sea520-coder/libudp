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
        static string remoteHost = "192.168.237.131";
        static string host = "192.168.237.1";
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
                sendMsg(conn, port);
              //  Thread t2 = new Thread(sendMsg);
             //   t2.Start();
             //   receiveMsg();
            }
            else
            {
                Console.WriteLine("client connect server failed");
            }
            conn.Close();
        }
        static void Main(string[] args)
        {
            Thread t1 = new Thread(ConnectHost);
        //    Thread t2 = new Thread(ConnectHost);
            t1.Start(1234);
       //     t2.Start(1235);
            Thread.Sleep(100000);
        }
        static void sendMsg(Connection conn, int port)
        {
            while (true)
            {  
                string msg1 = "";
                if(port == 1234)
                {
                    for(int i = 0; i < 60000; i++)
                    {
                        msg1 += "a";
                    }
                }
                else if(port == 1235)
                {
                    for(int i = 0; i < 60000; i++)
                    {
                        msg1 += "b";
                    }
                }
                conn.SendBytes(Encoding.UTF8.GetBytes(msg1), SendOption.FragmentedReliable);
                //      conn.SendBytes(Encoding.UTF8.GetBytes(msg2), SendOption.FragmentedReliable);
                //      conn.SendBytes(Encoding.UTF8.GetBytes(msg3), SendOption.FragmentedReliable);
                //      conn.SendBytes(Encoding.UTF8.GetBytes(msg4), SendOption.FragmentedReliable);
                //      conn.SendBytes(Encoding.UTF8.GetBytes(msg5), SendOption.FragmentedReliable);
                //      conn.SendBytes(Encoding.UTF8.GetBytes(msg6), SendOption.FragmentedReliable);
                string msg = Console.ReadLine();
                conn.SendBytes(Encoding.UTF8.GetBytes(msg), SendOption.FragmentedReliable); 
           }
        }
     /* static void receiveMsg()
        {
            while (true)
            {
                var data = conn.Receive();
                if (null != data)
                    Console.WriteLine(Encoding.UTF8.GetString(data));
            }
        }
        */
    }
}
