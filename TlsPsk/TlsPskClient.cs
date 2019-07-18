using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace TlsPsk
{
    public class TlsPskServer
    {
        private IntPtr ctx, in_bio;
        private string identityHint;
        private byte[] psk;

        public void Listen(string port, string identityHint, byte[] psk)
        {
            this.identityHint = identityHint;
            this.psk = psk;
            IntPtr tls;
            OpenSSL.OpenSSLCheck((tls = OpenSSL.TLSv1_2_server_method()) != IntPtr.Zero, "TLS_server_method");
            OpenSSL.OpenSSLCheck((ctx = OpenSSL.SSL_CTX_new(tls)) != IntPtr.Zero, "SSL_CTX_new");

            IntPtr ssl_bio;
            OpenSSL.OpenSSLCheck((ssl_bio = OpenSSL.BIO_new_ssl(ctx, 0)) != IntPtr.Zero, "BIO_new_ssl");

            OpenSSL.OpenSSLCheck((in_bio = OpenSSL.BIO_new_accept(port)) != IntPtr.Zero, "BIO_new_accept");
            OpenSSL.BIO_set_accept_bios(in_bio, ssl_bio);

            OpenSSL.OpenSSLCheck(OpenSSL.BIO_do_handshake(in_bio) == 1, "BIO_do_handshake"); // first call sets up the accept socket, subsequent get the connections
        }

        public TlsPskConnection Accept()
        {
            OpenSSL.OpenSSLCheck(OpenSSL.BIO_do_handshake(in_bio) == 1, "BIO_do_handshake");
            IntPtr conn = OpenSSL.BIO_pop(in_bio);

            IntPtr ssl;
            OpenSSL.BIO_get_ssl(conn, out ssl);
            OpenSSL.OpenSSLCheck(ssl != IntPtr.Zero, "BIO_get_ssl");
            OpenSSL.SSL_use_psk_identity_hint(ssl, identityHint);
            OpenSSL.SSL_set_psk_server_callback(ssl, (ssl_, identity, pskOut, max_psk_len) =>
            {
                Marshal.Copy(psk, 0, pskOut, (int)Math.Min(psk.Length, max_psk_len));
                return (uint)psk.Length;
            });

            OpenSSL.OpenSSLCheck(OpenSSL.BIO_do_handshake(conn) == 1, "BIO_do_handshake");

            return new TlsPskConnection(conn);
        }

        public void Close()
        {
            if (in_bio != IntPtr.Zero)
                OpenSSL.BIO_free_all(in_bio);
            if (ctx != IntPtr.Zero)
                OpenSSL.SSL_CTX_free(ctx);
        }
    }

    public class TlsPskClient : TlsPskConnection
    {
        private IntPtr ctx;

        public void Connect(string address, string identity, byte[] psk)
        {
            IntPtr tls, web, ssl;

            OpenSSL.OpenSSLCheck((tls = OpenSSL.TLSv1_2_method()) != IntPtr.Zero, "TLS_method");
            OpenSSL.OpenSSLCheck((ctx = OpenSSL.SSL_CTX_new(tls)) != IntPtr.Zero, "SSL_CTX_new");

            OpenSSL.OpenSSLCheck((web = OpenSSL.BIO_new_ssl_connect(ctx)) != IntPtr.Zero, "BIO_new_ssl_connect");
            OpenSSL.OpenSSLCheck(OpenSSL.BIO_set_conn_hostname(web, address) == 1, "BIO_set_conn_hostname");
            OpenSSL.BIO_get_ssl(web, out ssl);
            OpenSSL.OpenSSLCheck(ssl != IntPtr.Zero, "BIO_get_ssl");
            
            OpenSSL.SSL_set_psk_client_callback(ssl, (ssl_, hint, identityOut, max_identity_len, pskOut, max_psk_len) =>
            {
                byte[] identityBuf = Encoding.ASCII.GetBytes(identity);

                Marshal.Copy(identityBuf, 0, identityOut, (int)Math.Min(identityBuf.Length, max_identity_len));
                Marshal.Copy(psk, 0, pskOut, (int)Math.Min(psk.Length, max_psk_len));

                return (uint)psk.Length;
            });

            OpenSSL.OpenSSLCheck(OpenSSL.BIO_do_handshake(web) == 1, "BIO_do_handshake");

            base.Connect(web);
        }

        public override void Close()
        {
            base.Close();

            if (ctx != IntPtr.Zero)
                OpenSSL.SSL_CTX_free(ctx);
        }
    }

    public class TlsPskConnection
    {
        public bool IsConnected => web != null;
        public Stream Stream { get; private set; }

        private IntPtr web;

        static TlsPskConnection()
        {
            OpenSSL.SSL_library_init(0, IntPtr.Zero);
        }

        public static unsafe void Seed(byte[] buf)
        {
            fixed(byte* bufPtr = buf)
            {
                OpenSSL.RAND_seed(new IntPtr(bufPtr), buf.Length);
            }
        }

        public TlsPskConnection()
        {
        }

        public TlsPskConnection(IntPtr bio)
        {
            Connect(bio);
        }

        protected void Connect(IntPtr bio)
        {
            web = bio;
            Stream = new BIOStream(web);
        }

        public virtual void Close()
        {
            Stream = null;
            if (web != IntPtr.Zero)
                OpenSSL.BIO_free_all(web);
        }

        public class BIOStream : Stream
        {
            private readonly IntPtr bio;

            public BIOStream(IntPtr bio)
            {
                this.bio = bio;
            }

            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => true;

            public override long Length => throw new NotImplementedException();

            public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

            public override void Flush()
            {
                OpenSSL.OpenSSLCheck(OpenSSL.BIO_flush(bio) == 1, "BIO_flush");
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                IntPtr buf = Marshal.AllocHGlobal(count);
                int readCount;
                do
                {
                    readCount = OpenSSL.BIO_read(bio, buf, count);
                } while (readCount <= 0 && OpenSSL.BIO_should_retry(bio));
                OpenSSL.OpenSSLCheck(readCount > 0, "BIO_write");
                Marshal.Copy(buf, buffer, offset, readCount);
                Marshal.FreeHGlobal(buf);
                return readCount;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotImplementedException();
            }

            public override void SetLength(long value)
            {
                throw new NotImplementedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                IntPtr buf = Marshal.AllocHGlobal(count);
                Marshal.Copy(buffer, offset, buf, count);
                int writeCount;
                do
                {
                    writeCount = OpenSSL.BIO_write(bio, buf, count);
                } while (writeCount <= 0 && OpenSSL.BIO_should_retry(bio));
                OpenSSL.OpenSSLCheck(writeCount == count, "BIO_write");
                Marshal.FreeHGlobal(buf);
            }
        }
    }
}
