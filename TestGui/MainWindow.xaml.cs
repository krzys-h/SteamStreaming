using SteamStreaming;
using SteamStreaming.Enums;
using SteamStreaming.Protocols.Transport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Google.Protobuf;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.IO;
using SteamStreaming.Protocols.Application;
using System.Net;
using FFmpeg.AutoGen;
using TlsPsk;

namespace TestGui
{
    /// <summary>
    /// Logika interakcji dla klasy MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, SteamStreamClient.IStreamOutputSink
    {
        private unsafe AVCodecContext* avctx;

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

        public MainWindow()
        {
            InitializeComponent();
            Console.SetOut(new DebugWriter());

            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
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
                Steamid = 76561198009414634,
                AuthKeyId = 00000000 // removed
            });
            byte[] psk = Hexlify.StringToByteArray("0000000000000000000000000000000000000000000000000000000000000000"); // removed

            TlsPskClient tlsClient = new TlsPskClient();
            tlsClient.Connect("127.0.0.1:27036", "steam", psk);
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

            /*Process process = new Process();
            process.StartInfo.FileName = @"C:\Program Files (x86)\Steam\streaming_client.exe";
            process.StartInfo.WorkingDirectory = @"C:\Program Files (x86)\Steam";
            process.StartInfo.Arguments = "--gameid " + 391540 + " --server 127.0.0.1:" + startResponse.StreamPort + " --quality 2 " + Hexlify.ByteArrayToString(startResponse.AuthToken.ToByteArray());
            Console.WriteLine(process.StartInfo.Arguments);
            process.Start();*/
            SteamStreamClient stream = new SteamStreamClient(new IPEndPoint(IPAddress.Parse("127.0.0.1"), (int)startResponse.StreamPort), startResponse.AuthToken.ToByteArray(), this);
            stream.Connect();

            unsafe
            {
                ffmpeg.av_log_set_level(ffmpeg.AV_LOG_INFO);

                AVCodec* codec = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_H264);
                if (codec == null)
                    throw new InvalidOperationException("Unsupported codec");
                avctx = ffmpeg.avcodec_alloc_context3(codec);
                avctx->width = 1920;
                avctx->height = 1080;
                if (ffmpeg.avcodec_open2(avctx, codec, null) < 0)
                    throw new Exception("Could not open codec");
            }
        }
        
        public void OnVideoPacket(byte[] payload)
        {
            unsafe
            {
                fixed (byte* payloadBuf = payload)
                {
                    AVPacket avpkt;
                    avpkt.size = payload.Length;
                    avpkt.data = payloadBuf;
                    AVFrame* frame = ffmpeg.av_frame_alloc();
                    AVFrame* frameRGB = ffmpeg.av_frame_alloc();
                    int ret = ffmpeg.avcodec_send_packet(avctx, &avpkt);
                    if (ret < 0)
                        throw new Exception("send_packet: " + ret);
                    while (true)
                    {
                        ret = ffmpeg.avcodec_receive_frame(avctx, frame);
                        if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN)) // needs more input for more frames
                            break;
                        if (ret < 0)
                            throw new Exception("receive_frame: " + ret);

                        int stride = 3 * frame->width;
                        byte[] imgData = new byte[stride * frame->height];
                        fixed(byte* dataPtr = imgData)
                        {
                            SwsContext* ctx = ffmpeg.sws_getContext(frame->width, frame->height, AVPixelFormat.AV_PIX_FMT_YUV420P, frame->width, frame->height, AVPixelFormat.AV_PIX_FMT_RGB24, 0, null, null, null);
                            ffmpeg.sws_scale(ctx, frame->data, frame->linesize, 0, frame->height, new byte*[] { dataPtr }, new int[] { stride });
                        }
                        BitmapSource bitmap = BitmapSource.Create(frame->width, frame->height, 96, 96, PixelFormats.Rgb24, null, imgData, stride);
                        bitmap.Freeze();

                        using (var fileStream = new FileStream("test.png", FileMode.Create))
                        {
                            BitmapEncoder encoder = new PngBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(bitmap));
                            encoder.Save(fileStream);
                        }

                        Dispatcher.Invoke(() =>
                        {
                            OutputImage.Source = bitmap;
                        });
                    }
                    ffmpeg.av_frame_free(&frame);
                    ffmpeg.av_frame_free(&frameRGB);
                }
            }
        }

        public void OnAudioPacket(byte[] payload)
        {
            //throw new NotImplementedException();
        }
    }
}
