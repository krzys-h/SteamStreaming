using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SteamStreaming.Util
{
    public static class Extensions
    {
        public static V GetOrNew<K, V>(this IDictionary<K, V> dict, K key) where V : new()
        {
            V val;
            if (!dict.TryGetValue(key, out val))
            {
                val = new V();
                dict.Add(key, val);
            }
            return val;
        }
    }
}
