using SteamStreaming.Enums;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using TlsPsk;

namespace SteamStreaming.Protocols.Transport
{
    public class SteamRemoteTransport
    {
        private static readonly uint STEAM_MAGIC = BitConverter.ToUInt32(Encoding.ASCII.GetBytes("VT01"), 0);
        private const uint PROTO_MASK = 0x80000000;

        private readonly TlsPskConnection conn;
        private Thread recvThread;
        private BinaryReader reader;
        private BinaryWriter writer;

        public bool Active { get; private set; } = false;

        public delegate void PacketReceived(Packet packet);
        public event PacketReceived OnPacketReceived;

        public SteamRemoteTransport(TlsPskConnection conn)
        {
            this.conn = conn;
        }

        public void Start()
        {
            Active = true;
            reader = new BinaryReader(conn.Stream);
            writer = new BinaryWriter(conn.Stream);
            recvThread = new Thread(RecvThread);
            recvThread.Start();
        }

        public void Stop()
        {
            Active = false;
            recvThread.Join();
            recvThread = null;
            reader = null;
            writer = null;
        }

        private void RecvThread()
        {
            while (Active)
            {
                try
                {
                    var packet = Packet.FromStream(reader);
                    OnPacketReceived?.Invoke(packet);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
        }

        public void SendPacket(Packet packet)
        {
            packet.ToStream(writer);
        }

        public void SendPacket(EMsg type, byte[] payload)
        {
            SendPacket(new Packet()
            {
                type = type,
                payload = payload
            });
        }

        public class Packet
        {
            public EMsg type;
            public byte[] payload;

            public static Packet FromStream(BinaryReader reader)
            {
                Packet packet = new Packet();

                uint len = reader.ReadUInt32();
                uint magic = reader.ReadUInt32();
                if (magic != STEAM_MAGIC)
                    throw new IOException("Invalid packet, bad magic number: 0x" + magic.ToString("X16"));
                uint msgType = reader.ReadUInt32();
                byte[] packetPayload = reader.ReadBytes((int)len - 4);
                if ((msgType & PROTO_MASK) == PROTO_MASK)
                {
                    packet.type = (EMsg)(msgType & (~PROTO_MASK));

                    using (BinaryReader reader2 = new BinaryReader(new MemoryStream(packetPayload, false)))
                    {
                        uint headerSize = reader2.ReadUInt32();
                        byte[] header = reader2.ReadBytes((int)headerSize); // TODO: header is unknown... seems like a protobuf with field 11 set to -1, but sent only sometimes

                        packet.payload = reader2.ReadBytes((int)len - 4 - (int)headerSize);
                    }

                    return packet;
                }
                else
                {
                    throw new IOException("Non-protobuf message 0x" + msgType.ToString("X8"));
                }
            }

            public void ToStream(BinaryWriter writer)
            {
                byte[] header = null;
                uint headerLen = 0;
                writer.Write((uint)payload.Length + 8 + headerLen);
                writer.Write(STEAM_MAGIC);
                writer.Write(((uint)type) | PROTO_MASK);
                writer.Write(headerLen);
                if (header != null)
                    writer.Write(header);
                writer.Write(payload);
                writer.Flush();
            }
        }
    }
}
