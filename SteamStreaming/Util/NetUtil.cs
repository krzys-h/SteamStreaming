using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace SteamStreaming.Util
{
    public static class NetUtil
    {
        public static IPAddress CalculateNetworkAddress(IPAddress host, IPAddress mask)
        {
            byte[] ipBytes = host.GetAddressBytes();
            byte[] maskBytes = mask.GetAddressBytes();

            byte[] netBytes = new byte[ipBytes.Length];

            for (int i = 0; i < ipBytes.Length; i++)
            {
                netBytes[i] = (byte)(ipBytes[i] & maskBytes[i]);
            }

            return new IPAddress(netBytes);
        }

        public static IPAddress CalculateBroadcastAddress(IPAddress host, IPAddress mask)
        {
            byte[] ipBytes = host.GetAddressBytes();
            byte[] maskBytes = mask.GetAddressBytes();

            byte[] broadcastBytes = new byte[ipBytes.Length];
            
            for (int i = 0; i < ipBytes.Length; i++)
            {
                broadcastBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
            }

            return new IPAddress(broadcastBytes);
        }
    }
}
