using FFmpeg.AutoGen;
using SteamStreaming;
using SteamStreaming.Enums;
using SteamStreaming.Protocols.Application;
using SteamStreaming.Protocols.Transport;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using TlsPsk;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Security.Cryptography;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

//Szablon elementu Pusta strona jest udokumentowany na stronie https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x415

namespace XSteamBox
{
    /// <summary>
    /// Pusta strona, która może być używana samodzielnie lub do której można nawigować wewnątrz ramki.
    /// </summary>
    public sealed partial class MainPage : Page, SteamStreamClient.IStreamOutputSink
    {
        class DebugWriter : TextWriter
        {
            public override void WriteLine(string value)
            {
                Debug.WriteLine(value);
                base.WriteLine(value);
            }

            public override void Write(string value)
            {
                Debug.Write(value);
                base.Write(value);
            }

            public override Encoding Encoding
            {
                get { return Encoding.Unicode; }
            }
        }

        public MainPage()
        {
            this.InitializeComponent();
            Console.SetOut(new DebugWriter());
            
            TlsPskConnection.Seed(CryptographicBuffer.GenerateRandom(32).ToArray());
        }

        private unsafe AVCodecContext* avctx;
        private unsafe AVCodecParserContext* avparser;
        private unsafe SwsContext* sws_ctx = null;
        private libav_log_callback cb;

        private async void ButtonConnect_Click(object sender, RoutedEventArgs e)
        {
            string connectAddr = InputIP.Text + ":" + InputPort.Text;
            string connectPSK = InputPSK.Text;
            ButtonConnect.IsEnabled = false;
            Debug.WriteLine("Connect to " + connectAddr);

            var myStatus = new CMsgRemoteClientBroadcastStatus()
            {
                Version = 8,
                MinVersion = 6,
                ConnectPort = SteamDiscoveryTransport.STEAM_DISCOVERY_PORT,
                Hostname = "my-fake-name",
                EnabledServices = (uint)ERemoteClientService.KEremoteClientServiceGameStreaming,
                Ostype = (int)EOSType.Windows10,
                Is64Bit = true,
                Euniverse = (int)EUniverse.Public,
                GamesRunning = false,
            };
            myStatus.Users.Add(new CMsgRemoteClientBroadcastStatus.Types.User()
            {
                Steamid = 76561198009414634, // adjust to yours
                AuthKeyId = 00000000 // removed
            });
            byte[] psk = Hexlify.StringToByteArray(connectPSK); // you can get this (and the AuthKeyId above) in C:\Program Files (x86)\Steam\userdata\[your user id]\config\localconfig.vdf under the SharedAuth section

            TlsPskClient tlsClient = new TlsPskClient();
            tlsClient.Connect(connectAddr, "steam", psk);
            SteamRemote steamRemote = new SteamRemote(tlsClient, false, 1337, new Random().NextLong(), myStatus);
            steamRemote.MyApps.Add(new CMsgRemoteClientAppStatus.Types.AppStatus()
            {
                AppId = 391540,
                AppState = 4
            });
            await steamRemote.Start();
            CMsgRemoteClientStartStreamResponse startResponse = await steamRemote.StartStream(new CMsgRemoteClientStartStream()
            {
                AppId = 391540,
                LaunchOption = -1,
                MaximumResolutionX = 1920,
                MaximumResolutionY = 1080,
                AudioChannelCount = 2
            });
            
            SteamStreamClient stream = new SteamStreamClient(new IPEndPoint(IPAddress.Parse(InputIP.Text), (int)startResponse.StreamPort), startResponse.AuthToken.ToByteArray(), this);
            stream.Connect();
            
            unsafe
            {
                ffmpeg.av_log_set_level(ffmpeg.AV_LOG_VERBOSE);
                //ffmpeg.av_log_set_level(ffmpeg.AV_LOG_DEBUG);
                cb = libav_log;
                ffmpeg.av_log_set_callback(new av_log_set_callback_callback_func() { Pointer = Marshal.GetFunctionPointerForDelegate(cb) });

                AVCodec* codec = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_H264);
                if (codec == null)
                    throw new InvalidOperationException("Unsupported codec");
                avparser = ffmpeg.av_parser_init((int)codec->id);
                avparser->flags |= ffmpeg.PARSER_FLAG_COMPLETE_FRAMES;
                avctx = ffmpeg.avcodec_alloc_context3(codec);
                if (ffmpeg.avcodec_open2(avctx, codec, null) < 0)
                    throw new Exception("Could not open codec");
            }
        }

        [DllImport("msvcrt.dll", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vsprintf(StringBuilder buffer, string format, IntPtr args);

        [DllImport("msvcrt.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int _vscprintf(string format, IntPtr ptr);

        public delegate void libav_log_callback(IntPtr user, int level, string fmt, IntPtr args);
        public void libav_log(IntPtr user, int level, string fmt, IntPtr args)
        {
            if ((level & 0xFF) > ffmpeg.av_log_get_level())
                return;
            var sb = new StringBuilder(_vscprintf(fmt, args) + 1);
            vsprintf(sb, fmt, args);
            var formattedMessage = sb.ToString();
            if (formattedMessage.Contains("No accelerated colorspace conversion found"))
                return;
            Console.Write(formattedMessage);
        }

        
        public void OnVideoPacket(byte[] payload)
        {
            DoPacketWrite(payload);

            unsafe
            {
                fixed (byte* payloadBuf = payload)
                {
                    int ret;

                    AVPacket avpkt;
                    ffmpeg.av_init_packet(&avpkt);

                    ret = ffmpeg.av_parser_parse2(avparser, avctx, &avpkt.data, &avpkt.size, payloadBuf, payload.Length, ffmpeg.AV_NOPTS_VALUE, ffmpeg.AV_NOPTS_VALUE, 0);
                    if (ret < 0)
                        throw new Exception("av_parser_parse2: " + ret);
                    if (ret != payload.Length)
                        throw new Exception("Parsed only " + ret + " out of " + payload.Length);
                    if (avpkt.size == 0)
                        throw new Exception("Where is my frame?!");
                    /*avpkt.data = payloadBuf;
                    avpkt.size = payload.Length;*/

                    AVFrame* frame = ffmpeg.av_frame_alloc();
                    ret = ffmpeg.avcodec_send_packet(avctx, &avpkt);
                    if (ret < 0)
                        throw new Exception("send_packet: " + ret);
                    int i = 0;
                    while (true)
                    {
                        ret = ffmpeg.avcodec_receive_frame(avctx, frame);
                        if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN)) // needs more input for more frames
                            break;
                        if (ret < 0)
                            throw new Exception("receive_frame: " + ret);

                        int stride = 4 * frame->width;
                        byte[] imgData = new byte[stride * frame->height];
                        fixed (byte* dataPtr = imgData)
                        {
                            sws_ctx = ffmpeg.sws_getCachedContext(sws_ctx, frame->width, frame->height, AVPixelFormat.AV_PIX_FMT_YUV420P, frame->width, frame->height, AVPixelFormat.AV_PIX_FMT_BGRA, 0, null, null, null);
                            ffmpeg.sws_scale(sws_ctx, frame->data, frame->linesize, 0, frame->height, new byte*[] { dataPtr }, new int[] { stride });
                        }
                        
                        OnFrameDecoded(frame->width, frame->height, imgData);
                        i++;
                    }
                    ffmpeg.av_frame_free(&frame);
                }
            }
        }

        private IOutputStream fos;
        private async void DoPacketWrite(byte[] payload)
        {
            if (fos == null)
            {
                StorageFile file = await ApplicationData.Current.LocalFolder.CreateFileAsync("stream.h264", CreationCollisionOption.ReplaceExisting);
                Console.WriteLine(file.Path);
                IRandomAccessStream fileHandle = await file.OpenAsync(FileAccessMode.ReadWrite);
                fos = fileHandle.GetOutputStreamAt(0);
            }
            await fos.WriteAsync(CryptographicBuffer.CreateFromByteArray(payload));
        }

        private async void OnFrameDecoded(int width, int height, byte[] imgData)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                var bitmap = new WriteableBitmap(width, height);
                using (MemoryStream memoryStream = new MemoryStream(imgData))
                {
                    using (Stream stream = bitmap.PixelBuffer.AsStream())
                    {
                        memoryStream.CopyTo(stream);
                    }
                }

                VideoFeed.Source = bitmap;
            });
        }

        public void OnAudioPacket(byte[] payload)
        {
            //throw new NotImplementedException();
        }
    }
}
