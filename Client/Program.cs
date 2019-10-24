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
        static UdpClientConnection conn;
        static string remoteHost = "192.168.237.131";
        //static string remoteHost = "172.26.187.156";
        static string host = "192.168.237.1";

        static void Main(string[] args)
        {
            conn = new UdpClientConnection(host, 1234, remoteHost, 1230);

            conn.Connect();
            Console.WriteLine("Client");

            Thread t = new Thread(receiveMsg);
            t.Start();
            Thread t2 = new Thread(sendMsg);
            t2.Start();
        }
        static void sendMsg()
        {
            while (true)
            {
                string msg = Console.ReadLine();
                conn.SendBytes(Encoding.UTF8.GetBytes(msg), SendOption.FragmentedReliable);
                
                                string msg1 = "aaaa";
                                string msg2 = "bbb";
                                string msg3 = "";
                                for (int i = 0; i < 87; i++)
                                    msg3 += "c";
                               string msg4 = "";
                                for (int i = 0; i < 10000; i++)
                                    msg4 += "d";
                                string msg5 = "eee";
                                string msg6 = "";
                                for (int i = 0; i < 10000; i++)
                                    msg6 += "f";  
                
               // conn.SendBytes(Encoding.UTF8.GetBytes(msg1), SendOption.FragmentedReliable);
               // conn.SendBytes(Encoding.UTF8.GetBytes(msg2), SendOption.FragmentedReliable);
               // conn.SendBytes(Encoding.UTF8.GetBytes(msg3), SendOption.FragmentedReliable);
               // conn.SendBytes(Encoding.UTF8.GetBytes(msg4), SendOption.FragmentedReliable);
              //  conn.SendBytes(Encoding.UTF8.GetBytes(msg5), SendOption.FragmentedReliable);
               // conn.SendBytes(Encoding.UTF8.GetBytes(msg6), SendOption.FragmentedReliable);
            }
        }
        static void receiveMsg()
        {
            while (true)
            {
                var data = conn.Receive();
                if (null != data)
                    Console.WriteLine(Encoding.UTF8.GetString(data));
            }
        }
    }
}
