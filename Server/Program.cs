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
        static Queue<Connection> queue = new Queue<Connection>();
        static UdpConnectionListener listener;
        static CancellationTokenSource source = new CancellationTokenSource();
        static string host = "192.168.237.130";
        //static string host = "172.26.187.156";

        static async void AcceptLoop()
        {
            CancellationToken token = source.Token;
            while(!token.IsCancellationRequested)
            {
                Connection newconn = await listener.AcceptAsync(token);
                queue.Enqueue(newconn);
                _ = Task.Run(() =>
                {
                    while(true)
                    {
                        foreach(var newconn in queue)
                        {
                            try
                            {
                                byte[] data = newconn.Receive();
                                Console.WriteLine(data.Length);
                            }
                            catch(Exception e)
                            {

                            }
                            //Console.WriteLine(Encoding.UTF8.GetString(data));
                        }
                    }
                });
            }
        }
        static void Main(string[] args)
        {
            listener = new UdpConnectionListener(host, 1230);
            listener.Start();

            CancellationTokenSource source = new CancellationTokenSource();

            Console.WriteLine("Server");
            _ = Task.Run( AcceptLoop );
            string msg = Console.ReadLine();
        }
    }
}
