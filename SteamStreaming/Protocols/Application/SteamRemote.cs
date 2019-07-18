using SteamStreaming.Enums;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using SteamStreaming.Protocols.Transport;
using System.Net;
using SteamStreaming.Protocols.Application;
using TlsPsk;

namespace SteamStreaming
{
    public class SteamRemote
    {
        private static readonly uint STEAM_MAGIC = BitConverter.ToUInt32(Encoding.ASCII.GetBytes("VT01"), 0);
        private const uint PROTO_MASK = 0x80000000;

        public bool Active { get; private set; } = false;

        private readonly TlsPskConnection socket;
        private SteamRemoteTransport conn;
        private readonly ulong clientId;
        private readonly ulong instanceId;
        private readonly CMsgRemoteClientBroadcastStatus myStatus;

        private TaskCompletionSource<object> handshakeComplete;
        private TaskCompletionSource<CMsgRemoteClientStartStreamResponse> startStreamTCS;

        public List<CMsgRemoteClientAppStatus.Types.AppStatus> MyApps { get; private set; } = new List<CMsgRemoteClientAppStatus.Types.AppStatus>();
        private bool IsServer { get; }
        private bool IsClient => !IsServer;

        public SteamRemote(TlsPskConnection socket, bool isServer, ulong clientId, ulong instanceId, CMsgRemoteClientBroadcastStatus myStatus)
        {
            this.socket = socket;
            conn = new SteamRemoteTransport(socket);
            IsServer = isServer;
            this.clientId = clientId;
            this.instanceId = instanceId;
            this.myStatus = myStatus;
        }

        public Task Start()
        {
            handshakeComplete = new TaskCompletionSource<object>();
            conn.OnPacketReceived += OnPacketReceived;
            conn.Start();
            if (IsServer)
            {
                SendAuth();
            }
            return handshakeComplete.Task;
        }

        public void Stop()
        {
            conn.Stop();
            conn.OnPacketReceived -= OnPacketReceived;
        }

        private void OnPacketReceived(SteamRemoteTransport.Packet packet)
        {
            switch (packet.type)
            {
                case EMsg.RemoteClientAuth:
                    CMsgRemoteClientAuth auth = CMsgRemoteClientAuth.Parser.ParseFrom(packet.payload);
                    Console.WriteLine(auth);

                    if (IsClient)
                    {
                        SendAuth();
                    }
                    else
                    {
                        conn.SendPacket(EMsg.RemoteClientAuthResponse, new CMsgRemoteClientAuthResponse()
                        {
                            Eresult = (int)EResult.OK
                        }.ToByteArray());
                    }
                    break;

                case EMsg.RemoteClientAuthResponse:
                    CMsgRemoteClientAuthResponse authAck = CMsgRemoteClientAuthResponse.Parser.ParseFrom(packet.payload);
                    Console.WriteLine(authAck);

                    conn.SendPacket(EMsg.RemoteClientAuthResponse, new CMsgRemoteClientAuthResponse()
                    {
                        Eresult = (int)EResult.OK
                    }.ToByteArray());

                    if (IsServer)
                    {
                        CMsgRemoteClientAppStatus appStatusUpdate = new CMsgRemoteClientAppStatus();
                        appStatusUpdate.StatusUpdates.AddRange(MyApps);
                        conn.SendPacket(EMsg.RemoteClientAppStatus, appStatusUpdate.ToByteArray());
                    }
                    break;

                case EMsg.RemoteClientAppStatus:
                    CMsgRemoteClientAppStatus appStatus = CMsgRemoteClientAppStatus.Parser.ParseFrom(packet.payload);
                    Console.WriteLine(appStatus);

                    if (IsClient)
                    {
                        CMsgRemoteClientAppStatus appStatusUpdate = new CMsgRemoteClientAppStatus();
                        appStatusUpdate.StatusUpdates.AddRange(MyApps);
                        conn.SendPacket(EMsg.RemoteClientAppStatus, appStatusUpdate.ToByteArray());
                    }
                    if (!handshakeComplete.Task.IsCompleted)
                    {
                        handshakeComplete.SetResult(null);
                    }
                    break;

                case EMsg.RemoteClientPing:
                    CMsgRemoteClientPing ping = CMsgRemoteClientPing.Parser.ParseFrom(packet.payload);
                    conn.SendPacket(EMsg.RemoteClientPingResponse, new CMsgRemoteClientPingResponse().ToByteArray());
                    break;

                case EMsg.RemoteClientStartStreamResponse:
                    CMsgRemoteClientStartStreamResponse startResponse = CMsgRemoteClientStartStreamResponse.Parser.ParseFrom(packet.payload);
                    Console.WriteLine((EResult)startResponse.ELaunchResult);

                    if ((EResult)startResponse.ELaunchResult == EResult.OK)
                    {
                        Console.WriteLine(startResponse);
                        startStreamTCS.SetResult(startResponse);
                    }
                    break;

                default:
                    throw new NotImplementedException("Unknown message: " + packet.type);
            }
        }

        private void SendAuth()
        {
            CMsgRemoteClientAuth authResp = new CMsgRemoteClientAuth()
            {
                ClientId = this.clientId,
                InstanceId = this.instanceId,
                Status = this.myStatus
            };
            conn.SendPacket(EMsg.RemoteClientAuth, authResp.ToByteArray());
        }

        public Task<CMsgRemoteClientStartStreamResponse> StartStream(CMsgRemoteClientStartStream startStream)
        {
            startStreamTCS = new TaskCompletionSource<CMsgRemoteClientStartStreamResponse>();
            conn.SendPacket(EMsg.RemoteClientStartStream, startStream.ToByteArray());
            return startStreamTCS.Task;
        }
    }
}
