using SteamStreaming.Protocols.Transport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Google.Protobuf;
using System.Security.Cryptography;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace SteamStreaming.Protocols.Application
{
    using DataPacket = SteamStreamTransport.DataPacket;
    using TypedMessage = SteamStreamTransport.TypedMessage;
    using PacketType = SteamStreamTransport.PacketType;
    public class SteamStreamClient : SteamStreamBase
    {
        private readonly IStreamOutputSink sink;

        private CStartVideoDataMsg videoChannel;
        private CStartAudioDataMsg audioChannel;

        private Timer timer;

        public SteamStreamClient(IPEndPoint endpoint, byte[] authKey, IStreamOutputSink sink)
            : base(endpoint, authKey)
        {
            this.sink = sink;
        }
        
        public new void Connect()
        {
            base.Connect();

            SendControlMessage(EStreamControlMessage.KEstreamControlClientHandshake, new CClientHandshakeMsg()
            {
                Info = new CStreamingClientHandshakeInfo()
            }.ToByteArray());

            timer = new Timer(SendKeepalive, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        private void SendKeepalive(object state)
        {
            SendControlMessage(EStreamControlMessage.KEstreamControlVideoDecoderInfo, new CVideoDecoderInfoMsg()
            {
                Info = "all good"
            });
        }

        protected override void ProcessControlMessage(EStreamControlMessage type, byte[] messageBuffer)
        {
            if (type != EStreamControlMessage.KEstreamControlClientHandshake &&
                type != EStreamControlMessage.KEstreamControlServerHandshake && 
                type != EStreamControlMessage.KEstreamControlAuthenticationRequest &&
                type != EStreamControlMessage.KEstreamControlAuthenticationResponse)
                messageBuffer = DecryptPacket(messageBuffer);

            switch (type)
            {
                case EStreamControlMessage.KEstreamControlServerHandshake:
                    CServerHandshakeMsg serverHandshake = CServerHandshakeMsg.Parser.ParseFrom(messageBuffer);
                    Console.WriteLine(serverHandshake);

                    conn.MTU = serverHandshake.Info.Mtu;

                    CAuthenticationRequestMsg authRequest = new CAuthenticationRequestMsg()
                    {
                        Token = ByteString.CopyFrom(new HMACSHA256(authKey).ComputeHash(Encoding.ASCII.GetBytes("Steam In-Home Streaming"))),
                        Version = EStreamVersion.KEstreamVersionCurrent
                    };
                    Console.WriteLine(authRequest);
                    SendControlMessage(EStreamControlMessage.KEstreamControlAuthenticationRequest, authRequest);
                    break;

                case EStreamControlMessage.KEstreamControlAuthenticationResponse:
                    CAuthenticationResponseMsg authResponse = CAuthenticationResponseMsg.Parser.ParseFrom(messageBuffer);
                    Console.WriteLine(authResponse);
                    break;
                    
                case EStreamControlMessage.KEstreamControlNegotiationInit:
                    CNegotiationInitMsg negotiationInit = CNegotiationInitMsg.Parser.ParseFrom(messageBuffer);
                    Console.WriteLine(negotiationInit);

                    CNegotiatedConfig config = new CNegotiatedConfig()
                    {
                        ReliableData = false,
                        SelectedAudioCodec = EStreamAudioCodec.KEstreamAudioCodecOpus,
                        SelectedVideoCodec = EStreamVideoCodec.KEstreamVideoCodecH264,
                        EnableRemoteHid = false,
                    };
                    config.AvailableVideoModes.Add(new CStreamVideoMode()
                    {
                        Width = 1920,
                        Height = 1080,
                        RefreshRateNumerator = 557685800,
                        RefreshRateDenominator = 9256800
                    });

                    CNegotiationSetConfigMsg negotiationSetConfig = new CNegotiationSetConfigMsg()
                    {
                        Config = config,
                        StreamingClientConfig = new CStreamingClientConfig()
                        {
                            Quality = EStreamQualityPreference.KEstreamQualityBalanced
                        },
                        StreamingClientCaps = new CStreamingClientCaps()
                        {
                            SystemInfo = @"""SystemInfo""
{
	""ostype""		""16""
	""CPUID""		""GenuineIntel""
	""CPUGhz""		""2.39400005340576172""
	""PhysicalCPUCount""		""4""
	""LogicalCPUCount""		""8""
	""SystemRAM""		""20392""
	""VideoVendorID""		""32902""
	""VideoDeviceID""		""1046""
	""VideoRevision""		""6""
	""VideoRAM""		""-1""
	""VideoDisplayX""		""1920""
	""VideoDisplayY""		""1080""
	""VideoDisplayNameID""		""Generic PnP Monitor""
}",
                            SystemCanSuspend = false,
                            SupportsVideoHevc = false
                        }
                    };
                    SendControlMessage(EStreamControlMessage.KEstreamControlNegotiationSetConfig, negotiationSetConfig);
                    break;

                case EStreamControlMessage.KEstreamControlNegotiationSetConfig:
                    CNegotiationSetConfigMsg negotiationConfigAck = CNegotiationSetConfigMsg.Parser.ParseFrom(messageBuffer);
                    Console.WriteLine(negotiationConfigAck);

                    CNegotiationCompleteMsg negotiationComplete = new CNegotiationCompleteMsg();
                    SendControlMessage(EStreamControlMessage.KEstreamControlNegotiationComplete, negotiationComplete);

                    CLogMsg logMsg = new CLogMsg()
                    {
                        Type = 3,
                        Message = "Hello from the custom client!!"
                    };
                    SendStatsMessage(EStreamStatsMessage.KEstreamStatsLogMessage, logMsg);
                    break;

                case EStreamControlMessage.KEstreamControlSetQoS:
                    CSetQoSMsg setQoS = CSetQoSMsg.Parser.ParseFrom(messageBuffer);
                    Console.WriteLine(setQoS);
                    break;

                case EStreamControlMessage.KEstreamControlSetTargetBitrate:
                    CSetTargetBitrateMsg targetBitrate = CSetTargetBitrateMsg.Parser.ParseFrom(messageBuffer);
                    Console.WriteLine(targetBitrate);
                    break;

                case EStreamControlMessage.KEstreamControlStartAudioData:
                    CStartAudioDataMsg startAudioData = CStartAudioDataMsg.Parser.ParseFrom(messageBuffer);
                    Console.WriteLine(startAudioData);

                    audioChannel = startAudioData;
                    break;

                case EStreamControlMessage.KEstreamControlSetSpectatorMode:
                    CSetSpectatorModeMsg spectatorMode = CSetSpectatorModeMsg.Parser.ParseFrom(messageBuffer);
                    Console.WriteLine(spectatorMode);
                    break;

                case EStreamControlMessage.KEstreamControlSetTitle:
                    CSetTitleMsg setTitle = CSetTitleMsg.Parser.ParseFrom(messageBuffer);
                    Console.WriteLine(setTitle);
                    break;

                case EStreamControlMessage.KEstreamControlSetIcon:
                    CSetIconMsg setIcon = CSetIconMsg.Parser.ParseFrom(messageBuffer);
                    //Console.WriteLine(setIcon);
                    break;

                case EStreamControlMessage.KEstreamControlShowCursor:
                    CShowCursorMsg showCursor = CShowCursorMsg.Parser.ParseFrom(messageBuffer);
                    Console.WriteLine(showCursor);
                    break;

                case EStreamControlMessage.KEstreamControlSetCursor:
                    CSetCursorMsg setCursor = CSetCursorMsg.Parser.ParseFrom(messageBuffer);
                    Console.WriteLine(setCursor);
                    break;

                case EStreamControlMessage.KEstreamControlSetActivity:
                    CSetActivityMsg setActivity = CSetActivityMsg.Parser.ParseFrom(messageBuffer);
                    Console.WriteLine(setActivity);
                    break;

                case EStreamControlMessage.KEstreamControlStartVideoData:
                    CStartVideoDataMsg startVideoData = CStartVideoDataMsg.Parser.ParseFrom(messageBuffer);
                    Console.WriteLine(startVideoData);

                    videoChannel = startVideoData;
                    break;

                case EStreamControlMessage.KEstreamControlVideoEncoderInfo:
                    CVideoEncoderInfoMsg encoderInfo = CVideoEncoderInfoMsg.Parser.ParseFrom(messageBuffer);
                    Console.WriteLine(encoderInfo);
                    break;

                default:
                    throw new NotImplementedException("Unknown message: " + type);
            }
        }

        protected override void ProcessStatsMessage(EStreamStatsMessage type, byte[] messageBuffer)
        {
            switch (type)
            {
                default:
                    throw new NotImplementedException("Unknown message: " + type);
            }
        }
        
        protected override void ProcessStreamDataMessage(byte channel, EStreamDataMessage type, byte[] payload)
        {
            if (type != EStreamDataMessage.KEstreamDataPacket)
                throw new NotImplementedException("Unknown message: " + type);

            if (audioChannel != null && channel == audioChannel.Channel)
            {
                sink?.OnAudioPacket(payload);
            }

            if (videoChannel != null && channel == videoChannel.Channel)
            {
                sink?.OnVideoPacket(payload);
            }
        }

        public interface IStreamOutputSink
        {
            void OnVideoPacket(byte[] payload);
            void OnAudioPacket(byte[] payload);
        }
    }
}
