using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using SteamStreaming.Protocols.Transport;

namespace SteamStreaming.Protocols.Application
{
    public class SteamDiscovery
    {
        public readonly ulong ClientId;
        public readonly ulong InstanceId;

        public readonly ReadOnlyCollection<Client> DiscoveredClients;
        public CMsgRemoteClientBroadcastStatus MyStatus;

        public delegate void ClientEvent(Client client);
        public event ClientEvent OnClientOnline;
        public event ClientEvent OnClientStatusUpdate;
        public event ClientEvent OnClientOffline;

        private SteamDiscoveryTransport conn;
        private readonly List<Client> discovered;
        private uint broadcastSeqNum = 0;

        public SteamDiscovery(ulong clientId)
        {
            this.ClientId = clientId;
            this.InstanceId = new Random().NextLong();

            conn = new SteamDiscoveryTransport();
            conn.OnPacketReceived += OnPacketReceived;

            discovered = new List<Client>();
            DiscoveredClients = new ReadOnlyCollection<Client>(discovered);
        }

        public void Start()
        {
            conn.Connect();
            BroadcastMyStatus();
            BroadcastDiscovery();
        }

        public void Stop()
        {
            BroadcastOffline();
            conn.Close();

            broadcastSeqNum = 0;
            discovered.Clear();
        }

        private void UpdateClientStatus(IPEndPoint from, ulong clientId, ulong instanceId, CMsgRemoteClientBroadcastStatus status)
        {
            Client client = discovered.Where((x) => x.clientId == clientId).FirstOrDefault();

            if (client != null && client.instanceId != instanceId)
            {
                // We have a previous instance of this client, but it has since rebooted - disconnect from the stale copy
                discovered.Remove(client);
                OnClientOffline(client);
                client = null;
            }

            bool newClient = false;
            if (client == null)
            {
                // This is a new client
                client = new Client()
                {
                    clientId = clientId,
                    instanceId = instanceId
                };
                discovered.Add(client);
                newClient = true;
            }

            if (status != null)
            {
                // We have a new status for this client
                // (could also be the first status for this client)
                client.from = from;
                client.status = status;
                if (newClient)
                    OnClientOnline(client); // do this only after status was set
                OnClientStatusUpdate(client);
            }
            else
            {
                // The client sent an offline message, remove it
                discovered.Remove(client);
                if (!newClient)
                    OnClientOffline(client);
                client = null;
            }
        }

        private void OnPacketReceived(IPEndPoint from, SteamDiscoveryTransport.Packet packet)
        {
            if (packet.clientId == this.ClientId)
                return;

            switch(packet.type)
            {
                case ERemoteClientBroadcastMsg.KEremoteClientBroadcastMsgDiscovery:
                    CMsgRemoteClientBroadcastDiscovery discovery = CMsgRemoteClientBroadcastDiscovery.Parser.ParseFrom(packet.payload);
                    BroadcastMyStatusTo(from);
                    break;

                case ERemoteClientBroadcastMsg.KEremoteClientBroadcastMsgStatus:
                    CMsgRemoteClientBroadcastStatus status = CMsgRemoteClientBroadcastStatus.Parser.ParseFrom(packet.payload);
                    UpdateClientStatus(from, packet.clientId, packet.instanceId, status);
                    break;

                case ERemoteClientBroadcastMsg.KEremoteClientBroadcastMsgOffline:
                    UpdateClientStatus(from, packet.clientId, packet.instanceId, null);
                    break;

                default:
                    throw new IOException("Unknown message type: " + packet.type);
            }
        }

        public void BroadcastDiscovery()
        {
            BroadcastPacket(ERemoteClientBroadcastMsg.KEremoteClientBroadcastMsgDiscovery, new CMsgRemoteClientBroadcastDiscovery()
            {
                SeqNum = ++broadcastSeqNum
            }.ToByteArray());
        }

        public void BroadcastMyStatusTo(IPEndPoint to)
        {
            if (MyStatus == null)
                return;

            MyStatus.Timestamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            SendPacket(to, ERemoteClientBroadcastMsg.KEremoteClientBroadcastMsgStatus, MyStatus.ToByteArray());
        }

        public void BroadcastMyStatus()
        {
            if (MyStatus == null)
                return;

            MyStatus.Timestamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            BroadcastPacket(ERemoteClientBroadcastMsg.KEremoteClientBroadcastMsgStatus, MyStatus.ToByteArray());
        }

        public void BroadcastOffline()
        {
            BroadcastPacket(ERemoteClientBroadcastMsg.KEremoteClientBroadcastMsgOffline, null);
        }

        public class Client
        {
            public IPEndPoint from { get; internal set; }
            public ulong clientId { get; internal set; }
            public ulong instanceId { get; internal set; }
            public CMsgRemoteClientBroadcastStatus status { get; internal set; }

            internal Client()
            {
            }

            public override string ToString()
            {
                return "[" + from + "] [" + clientId + ":" + instanceId + "] " + status;
            }
        }

        #region Packet handling

        private void SendPacket(IPEndPoint endpoint, ERemoteClientBroadcastMsg type, byte[] payload)
        {
            conn.SendPacket(endpoint, new SteamDiscoveryTransport.Packet()
            {
                clientId = ClientId,
                instanceId = InstanceId,
                type = type,
                payload = payload
            });
        }

        private void BroadcastPacket(ERemoteClientBroadcastMsg type, byte[] payload)
        {
            conn.BroadcastPacket(new SteamDiscoveryTransport.Packet()
            {
                clientId = ClientId,
                instanceId = InstanceId,
                type = type,
                payload = payload
            });
        }

        #endregion
    }
}
