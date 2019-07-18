using Google.Protobuf;
using SteamStreaming.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SteamStreaming.Protocols.Transport
{
    public class SteamDiscoveryTransport
    {
        public const int STEAM_DISCOVERY_PORT = 27036;
        protected const ulong STEAM_DISCOVERY_MAGIC = 0xA05F4C21FFFFFFFF;

        private UdpClient udpClient;
        private Thread recvThread;

        public bool Active { get; private set; } = false;

        public delegate void PacketReceived(IPEndPoint from, Packet packet);
        public event PacketReceived OnPacketReceived;

        public SteamDiscoveryTransport()
        {
        }

        public void Connect()
        {
            udpClient = new UdpClient();
            udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, STEAM_DISCOVERY_PORT));

            Active = true;
            recvThread = new Thread(RecvThread);
            recvThread.Start();
        }

        public void Close()
        {
            Active = false;
            udpClient.Close();
            recvThread.Join();
            recvThread = null;
            udpClient = null;
        }

        private void RecvThread()
        {
            while (Active)
            {
                try
                {
                    var from = new IPEndPoint(0, 0);
                    var recvBuffer = udpClient.Receive(ref from);
                    if (recvBuffer == null || recvBuffer.Length == 0)
                        continue;

                    var packet = Packet.FromByteArray(recvBuffer);
                    try
                    {
                        OnPacketReceived?.Invoke(from, packet);
                    }
                    catch(Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }
                catch (SocketException)
                {
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
        }

        public void SendPacket(IPEndPoint to, Packet packet)
        {
            byte[] data = packet.ToByteArray();
            udpClient.Send(data, data.Length, to);
        }

        public void SendPacket(IPAddress to, Packet packet)
        {
            SendPacket(new IPEndPoint(to, STEAM_DISCOVERY_PORT), packet);
        }

        public void BroadcastPacket(Packet packet)
        {
            foreach(var iface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (iface.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (iface.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var ipAddr in iface.GetIPProperties().UnicastAddresses)
                {
                    if (ipAddr.Address.GetAddressBytes().Length != 4)
                        continue;
                    SendPacket(NetUtil.CalculateBroadcastAddress(ipAddr.Address, ipAddr.IPv4Mask), packet);
                }
            }
        }

        public class Packet
        {
            public ulong clientId;
            public ulong instanceId;
            public ERemoteClientBroadcastMsg type;
            public byte[] payload; // optional

            public byte[] ToByteArray()
            {
                using (MemoryStream ms = new MemoryStream())
                using (BinaryWriter writer = new BinaryWriter(ms))
                {
                    writer.Write(STEAM_DISCOVERY_MAGIC);

                    CMsgRemoteClientBroadcastHeader hdr = new CMsgRemoteClientBroadcastHeader()
                    {
                        ClientId = clientId,
                        InstanceId = instanceId,
                        MsgType = type
                    };
                    byte[] hdrData = hdr.ToByteArray();
                    writer.Write((uint)hdrData.Length);
                    writer.Write(hdrData);

                    if (payload != null)
                    {
                        writer.Write((uint)payload.Length);
                        writer.Write(payload);
                    }

                    writer.Flush();
                    return ms.ToArray();
                }
            }

            public static Packet FromByteArray(byte[] data)
            {
                using (MemoryStream ms = new MemoryStream(data, false))
                using (BinaryReader reader = new BinaryReader(ms))
                {
                    ulong magic = reader.ReadUInt64();
                    if (magic != STEAM_DISCOVERY_MAGIC)
                        throw new IOException("Invalid packet, bad magic number: 0x" + magic.ToString("X16"));

                    Packet packet = new Packet();

                    uint hdrLen = reader.ReadUInt32();
                    byte[] hdrData = reader.ReadBytes((int)hdrLen);
                    CMsgRemoteClientBroadcastHeader hdr = CMsgRemoteClientBroadcastHeader.Parser.ParseFrom(hdrData);
                    packet.clientId = hdr.ClientId;
                    packet.instanceId = hdr.InstanceId;
                    packet.type = hdr.MsgType;

                    if (ms.Position < ms.Length)
                    {
                        uint payloadLen = reader.ReadUInt32();
                        packet.payload = reader.ReadBytes((int)payloadLen);
                    }

                    return packet;
                }
            }
        };
    }
}
