using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using LowLevelTransport.Udp;
using LowLevelTransport;

namespace Server
{
    class Program
    {
        static Connection newconn;
        static UdpConnectionListener listener;
        static CancellationTokenSource source = new CancellationTokenSource();
        static string host = "192.168.237.131";

        static async void AcceptLoop()
        {
            CancellationToken token = source.Token;
            while(!token.IsCancellationRequested)
            {
                newconn = await listener.AcceptAsync(token);
                _ = Task.Run( () =>
               {
                       while(true)
                       {
                            byte[] data = newconn.Receive();
                            if(data != null)
                                Console.WriteLine(data.Length);
                                //Console.WriteLine(Encoding.UTF8.GetString(data));
                   }
               });
            }
        }

        static void Main(string[] args)
        {
            listener = new UdpConnectionListener(host, 1230);
            listener.Start();

            CancellationTokenSource source = new CancellationTokenSource();

            Thread t = new Thread(sendMsg);
            t.Start();

            Console.WriteLine("Server");
            _ = Task.Run(AcceptLoop);
        }
        static void sendMsg()
        {
            while (true)
            {
                string msg = Console.ReadLine();
                if (newconn != null)
                   newconn.SendBytes(Encoding.UTF8.GetBytes(msg), SendOption.FragmentedReliable);
                else
                    Console.WriteLine("newconn is null");
                
            }
        }
    }
}
