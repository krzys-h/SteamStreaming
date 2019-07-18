using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SteamStreaming
{
    public static class RandomExtensions
    {
        public static ulong NextLong(this Random rnd)
        {
            byte[] buffer = new byte[sizeof(ulong)];
            rnd.NextBytes(buffer);
            return BitConverter.ToUInt64(buffer, 0);
        }

        public static byte NextByte(this Random rnd)
        {
            byte[] buffer = new byte[1];
            rnd.NextBytes(buffer);
            return buffer[0];
        }
    }
}
