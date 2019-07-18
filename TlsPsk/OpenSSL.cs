using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace TlsPsk
{
    public static class OpenSSL
    {
        [DllImport("ssleay32")]
        public static extern int SSL_library_init(ulong opts, IntPtr settings);

        [DllImport("libeay32")]
        public static extern void RAND_seed(IntPtr buf, int length);

        [DllImport("ssleay32")]
        public static extern IntPtr TLSv1_2_method();

        [DllImport("ssleay32")]
        public static extern IntPtr TLSv1_2_server_method();

        [DllImport("ssleay32")]
        public static extern IntPtr SSL_CTX_new(IntPtr method);
        
        [DllImport("ssleay32")]
        public static extern void SSL_CTX_free(IntPtr ctx);

        [DllImport("ssleay32")]
        public static extern IntPtr BIO_new_ssl(IntPtr ctx, int client);

        [DllImport("ssleay32")]
        public static extern IntPtr BIO_new_ssl_connect(IntPtr ctx);

        [DllImport("libeay32", CharSet = CharSet.Ansi)]
        public static extern IntPtr BIO_new_accept(string port);

        [DllImport("libeay32")]
        public static extern int BIO_do_accept(IntPtr bio);

        public const int BIO_C_SET_ACCEPT = 118;
        public static long BIO_set_accept_bios(IntPtr b, IntPtr bio)
        {
            return BIO_ctrl(b, BIO_C_SET_ACCEPT, 3, bio);
        }

        [DllImport("libeay32")]
        public static extern IntPtr BIO_pop(IntPtr bio);

        [DllImport("libeay32")]
        public static extern void BIO_free(IntPtr bio);

        [DllImport("libeay32")]
        public static extern void BIO_free_all(IntPtr bio);

        [DllImport("libeay32")]
        public static extern int BIO_read(IntPtr bio, IntPtr data, int dlen);

        [DllImport("libeay32")]
        public static extern int BIO_write(IntPtr bio, IntPtr data, int dlen);

        [DllImport("libeay32")]
        public static extern int BIO_test_flags(IntPtr bio, int flags);

        public const int BIO_FLAGS_SHOULD_RETRY = 0x08;
        public static bool BIO_should_retry(IntPtr bio)
        {
            return BIO_test_flags(bio, BIO_FLAGS_SHOULD_RETRY) != 0;
        }

        [DllImport("libeay32")]
        public static extern int BIO_ctrl(IntPtr bio, int cmd, long larg, IntPtr parg);

        public const int BIO_C_SET_CONNECT = 100;
        public static int BIO_set_conn_hostname(IntPtr bio, string name)
        {
            IntPtr name2 = Marshal.StringToHGlobalAnsi(name);
            int ret = BIO_ctrl(bio, BIO_C_SET_CONNECT, 0, name2);
            Marshal.FreeHGlobal(name2);
            return ret;
        }

        public const int BIO_C_DO_STATE_MACHINE = 101;
        public static int BIO_do_handshake(IntPtr bio)
        {
            return BIO_ctrl(bio, BIO_C_DO_STATE_MACHINE, 0, IntPtr.Zero);
        }

        public const int BIO_CTRL_FLUSH = 11;
        public static int BIO_flush(IntPtr bio)
        {
            return BIO_ctrl(bio, BIO_CTRL_FLUSH, 0, IntPtr.Zero);
        }

        public delegate int LogDelegate(string str, UIntPtr len, IntPtr user);
        [DllImport("libeay32")]
        public static extern IntPtr ERR_print_errors_cb(LogDelegate cb, IntPtr user);

        public const int BIO_C_GET_SSL = 110;
        public unsafe static int BIO_get_ssl(IntPtr bio, out IntPtr ssl)
        {
            void* localSsl = null;
            int ret = BIO_ctrl(bio, BIO_C_GET_SSL, 0, new IntPtr(&localSsl));
            ssl = new IntPtr(localSsl);
            return ret;
        }

        [DllImport("ssleay32", CharSet = CharSet.Ansi)]
        public static extern int SSL_set_cipher_list(IntPtr ssl, string str);

        [DllImport("ssleay32", CharSet = CharSet.Ansi)]
        public static extern int SSL_use_psk_identity_hint(IntPtr ssl, string hint);

        public delegate uint ServerPskDelegate(IntPtr ssl, string identity, IntPtr pskOut, uint max_psk_len);
        [DllImport("ssleay32", CharSet = CharSet.Ansi)]
        public static extern int SSL_set_psk_server_callback(IntPtr ssl, ServerPskDelegate callback);

        public delegate uint ClientPskDelegate(IntPtr ssl, string hint, IntPtr identityOut, uint max_identity_len, IntPtr pskOut, uint max_psk_len);

        [DllImport("ssleay32", CharSet = CharSet.Ansi)]
        public static extern int SSL_set_psk_client_callback(IntPtr ssl, ClientPskDelegate callback);
        

        public static void OpenSSLThrow(string func)
        {
            StringBuilder sb = new StringBuilder();
            OpenSSL.ERR_print_errors_cb((str, size, user) => { sb.AppendLine(str); return 0; }, IntPtr.Zero);
            throw new IOException("OpenSSL failed in " + func + "\n" + sb);
        }

        public static void OpenSSLCheck(bool condition, string func)
        {
            if (!condition)
                OpenSSLThrow(func);
        }
    }
}
