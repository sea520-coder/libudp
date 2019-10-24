using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace LowLevelTransport.Utils
{
    public static class NetworkUtils
    {
        public static IPAddress GetIPAddr(string hostNameOrAddress)
        {
            if (string.IsNullOrEmpty(hostNameOrAddress) || hostNameOrAddress == "0.0.0.0")
            {
                return IPAddress.Any;
            }

            var addrs = Dns.GetHostAddresses(hostNameOrAddress);

            if (addrs.Length == 0)
            {
                throw new Exception($"could not get {hostNameOrAddress}'s IP address");
            }

            return addrs[0];
        }

        public static async Task<IPAddress> GetIPAddrAsync(string hostNameOrAddress)
        {
            if (string.IsNullOrEmpty(hostNameOrAddress) || hostNameOrAddress == "0.0.0.0")
            {
                return IPAddress.Any;
            }

            var addrs = await Dns.GetHostAddressesAsync(hostNameOrAddress);

            if (addrs.Length == 0)
            {
                throw new Exception($"could not get {hostNameOrAddress}'s IP address");
            }

            return addrs[0];
        }
    }
}

