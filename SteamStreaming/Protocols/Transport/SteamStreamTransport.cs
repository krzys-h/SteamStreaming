using Ralph.Crc32C;
using SteamStreaming.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SteamStreaming.Protocols.Transport
{
    public class SteamStreamTransport
    {
        private const long DEFAULT_MTU = long.MaxValue;

        private readonly IPEndPoint endpoint;

        private UdpClient udpClient;
        private Stopwatch stopwatch;
        private Thread recvThread;

        private byte localId;
        private byte remoteId;

        private class ChannelData
        {
            public ushort localSeq = 0;
            public ushort remoteSeq = 0;

            public SortedList<ushort, NetworkPacket> sendQueue = new SortedList<ushort, NetworkPacket>();
            public SortedList<ushort, NetworkPacket> receiveQueue = new SortedList<ushort, NetworkPacket>();
        };
        private Dictionary<byte, ChannelData> channels = new Dictionary<byte, ChannelData>();

        public bool Active { get; private set; } = false;
        public uint CurrentTimestamp => (uint)(stopwatch.ElapsedTicks / 100);
        public long MTU { get; set; } = DEFAULT_MTU;

        public SteamStreamTransport(IPEndPoint endpoint)
        {
            this.endpoint = endpoint;
        }

        public void Connect()
        {
            udpClient = new UdpClient();
            udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

            stopwatch = new Stopwatch();
            stopwatch.Start();

            localId = new Random().NextByte();
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(ms))
            {
                writer.Write(0x3c8f3dc7); // magic, version, whatever
                writer.Flush();

                SendRawPacket(new NetworkPacket()
                {
                    usingChecksum = false,
                    packetType = PacketType.SYN,
                    from = localId,
                    to = 0,
                    channel = (byte)EStreamChannel.KEstreamChannelDiscovery,
                    payload = ms.ToArray()
                });
            }

            NetworkPacket syn_ack;
            do
            {
                syn_ack = ReadRawPacket();
            } while (syn_ack.packetType != PacketType.SYN_ACK || syn_ack.to != localId);
            remoteId = syn_ack.from;

            Active = true;
            recvThread = new Thread(RecvThread);
            recvThread.Start();
        }

        public void Close()
        {
            SendRawPacket(new NetworkPacket()
            {
                packetType = PacketType.FIN,
                from = localId,
                to = remoteId
            });

            NetworkPacket fin_ack;
            do
            {
                fin_ack = ReadRawPacket();
            } while (fin_ack.packetType != PacketType.FIN || fin_ack.to != localId || fin_ack.from != remoteId);

            Reset();
        }

        protected void Reset()
        {
            udpClient.Close();
            Active = false;
            recvThread.Join();
            recvThread = null;
            udpClient = null;
            stopwatch.Stop();
            stopwatch = null;
            localId = 0;
            remoteId = 0;
            MTU = DEFAULT_MTU;
            channels.Clear();
        }

        private void RecvThread()
        {
            while(Active)
            {
                try
                {
                    var from = new IPEndPoint(0, 0);
                    var recvBuffer = udpClient.Receive(ref from);
                    if (recvBuffer == null || recvBuffer.Length == 0)
                        continue;

                    NetworkPacket packet = NetworkPacket.FromByteArray(recvBuffer);
                    if ((packet.from != remoteId && packet.from != 0) || (packet.to != localId && packet.to != 0))
                        continue;
                    switch(packet.packetType)
                    {
                        case PacketType.SYN:
                        case PacketType.SYN_ACK:
                            throw new InvalidOperationException();

                        case PacketType.FIN:
                            SendRawPacket(new NetworkPacket()
                            {
                                packetType = PacketType.FIN,
                                from = localId,
                                to = remoteId
                            });

                            Reset();
                            break;

                        case PacketType.Raw:
                            if (packet.channel != (byte)EStreamChannel.KEstreamChannelDiscovery)
                                throw new InvalidOperationException();

                            TypedMessage msg = TypedMessage.FromByteArray(packet.payload);
                            EStreamDiscoveryMessage msgType = (EStreamDiscoveryMessage)msg.messageType;
                            switch(msgType)
                            {
                                case EStreamDiscoveryMessage.KEstreamDiscoveryPingRequest:
                                    msg.messageType = (byte)EStreamDiscoveryMessage.KEstreamDiscoveryPingResponse;
                                    SendRawPacket(new NetworkPacket()
                                    {
                                        packetType = PacketType.Raw,
                                        from = packet.to,
                                        to = packet.from,
                                        channel = (byte)EStreamChannel.KEstreamChannelDiscovery,
                                        payload = msg.ToByteArray()
                                    });
                                    break;

                                default:
                                    throw new InvalidOperationException();
                            }
                            break;

                        case PacketType.Reliable:
                        case PacketType.ReliableFragment:
                        case PacketType.Unreliable:
                        case PacketType.UnreliableFragment:
                            ChannelData channelData = channels.GetOrNew(packet.channel);
                            if (!channelData.receiveQueue.ContainsKey(packet.seq))
                            {
                                channelData.receiveQueue.Add(packet.seq, packet);
                                if (packet.seq < channelData.remoteSeq)
                                    Console.WriteLine("WARNING! Late packet " + packet.seq + " we are already at " + channelData.remoteSeq);
                            }
                            else
                                Console.WriteLine("WARNING! Got a resend of " + packet.seq);

                            // TODO: Clear the recv queue
                            ushort? lastValidPacketSeq = channelData.remoteSeq > 0 ? (ushort)(channelData.remoteSeq - 1) : (ushort?)null;
                            bool droppedIncompletePacket = false;
                            foreach (NetworkPacket firstPacket in channelData.receiveQueue.Values)
                            {
                                if (firstPacket.seq < channelData.remoteSeq)
                                    continue;
                                if (firstPacket.packetType == PacketType.ReliableFragment || firstPacket.packetType == PacketType.UnreliableFragment)
                                    continue;

                                // Check if the packet has all the fragments
                                bool isComplete = true;
                                for(ushort neededSeq = firstPacket.seq; neededSeq <= firstPacket.seq + firstPacket.fragment; ++neededSeq)
                                {
                                    if (!channelData.receiveQueue.ContainsKey(neededSeq))
                                    {
                                        isComplete = false;
                                        break;
                                    }
                                    else
                                    {
                                        lastValidPacketSeq = neededSeq;
                                    }
                                }
                                if (!isComplete)
                                {
                                    // The packet is not yet fully complete
                                    if (firstPacket.packetType == PacketType.Unreliable)
                                    {
                                        droppedIncompletePacket = true;
                                        continue; // this is unreliable so check if maybe next packet is complete
                                    }
                                    else
                                        break; // we cannot skip any packet on reliable transports
                                }

                                if (channelData.remoteSeq != firstPacket.seq)
                                    Console.WriteLine("WARNING! Dropping unreliable packets! Jump from " + channelData.remoteSeq + " to " + firstPacket.seq + (droppedIncompletePacket ? " (previous incomplete)" : ""));

                                // We have a complete packet, receive it
                                List<NetworkPacket> reassembleList = new List<NetworkPacket>();
                                for (ushort seq = firstPacket.seq; seq <= firstPacket.seq + firstPacket.fragment; ++seq)
                                {
                                    if (seq != firstPacket.seq)
                                    {
                                        if (firstPacket.packetType == PacketType.Reliable)
                                            Debug.Assert(channelData.receiveQueue[seq].packetType == PacketType.ReliableFragment);
                                        else if (firstPacket.packetType == PacketType.Unreliable)
                                            Debug.Assert(channelData.receiveQueue[seq].packetType == PacketType.UnreliableFragment);
                                        else
                                            Debug.Assert(false);
                                    }
                                    reassembleList.Add(channelData.receiveQueue[seq]);
                                }
                                DataPacket dataPacket = ReassembleDataPacket(reassembleList);
                                try
                                {
                                    OnPacketReceived?.Invoke(dataPacket);
                                }
                                catch (Exception e)
                                {
                                    Console.WriteLine(e);
                                }
                                channelData.remoteSeq = (ushort)(firstPacket.seq + firstPacket.fragment + 1);
                            }
                            
                            if ((packet.packetType == PacketType.Reliable || packet.packetType == PacketType.ReliableFragment) && lastValidPacketSeq.HasValue)
                            {
                                using (MemoryStream ms = new MemoryStream())
                                using (BinaryWriter writer = new BinaryWriter(ms))
                                {
                                    writer.Write(channelData.receiveQueue[lastValidPacketSeq.Value].timestamp);
                                    writer.Flush();

                                    SendRawPacket(new NetworkPacket()
                                    {
                                        packetType = PacketType.ReliableACK,
                                        from = localId,
                                        to = remoteId,
                                        channel = packet.channel,
                                        seq = lastValidPacketSeq.Value,
                                        payload = ms.ToArray()
                                    });
                                }
                            }
                            break;

                        case PacketType.ReliableACK:
                            //Console.WriteLine("ACKed channel=" + packet.channel + ", seq=" + packet.seq);
                            // TODO
                            break;
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

        private List<NetworkPacket> FragmentDataPacket(DataPacket dataPacket, ushort firstSeq)
        {
            List<NetworkPacket> packets = new List<NetworkPacket>();
            for (long start = 0; start < dataPacket.payload.LongLength;)
            {
                NetworkPacket packet = new NetworkPacket();
                if (dataPacket.packetType == PacketType.Reliable)
                    packet.packetType = start == 0 ? PacketType.Reliable : PacketType.ReliableFragment;
                else if (dataPacket.packetType == PacketType.Unreliable)
                    packet.packetType = start == 0 ? PacketType.Unreliable : PacketType.UnreliableFragment;
                else
                    throw new InvalidOperationException();
                packet.from = dataPacket.from;
                packet.to = dataPacket.to;
                packet.channel = dataPacket.channel;
                packet.seq = firstSeq++;
                long byteCount = Math.Min(dataPacket.payload.LongLength - start, MTU);
                packet.payload = new byte[byteCount];
                Array.Copy(dataPacket.payload, start, packet.payload, 0, byteCount);

                packets.Add(packet);
                start += byteCount;
            }

            foreach (var pair in packets.Select((packet, i) => new { i, packet }))
            {
                if (pair.i == 0)
                    pair.packet.fragment = (ushort)(packets.Count - 1);
                else
                    pair.packet.fragment = (ushort)(pair.i - 1);
            }

            return packets;
        }

        private DataPacket ReassembleDataPacket(List<NetworkPacket> packets)
        {
            DataPacket dataPacket = new DataPacket();

            NetworkPacket first = packets.First();
            dataPacket.packetType = first.packetType;
            dataPacket.from = first.from;
            dataPacket.to = first.to;
            dataPacket.channel = first.channel;
            long totalLength = packets.Select((x) => x.payload.LongLength).Sum();
            dataPacket.payload = new byte[totalLength];
            long start = 0;
            foreach(NetworkPacket packet in packets)
            {
                Array.Copy(packet.payload, 0, dataPacket.payload, start, packet.payload.LongLength);
                start += packet.payload.LongLength;
            }
            Debug.Assert(start == totalLength);

            return dataPacket;
        }

        public delegate void PacketReceived(DataPacket dataPacket);
        public event PacketReceived OnPacketReceived;

        public void SendRawPacket(NetworkPacket packet)
        {
            packet.timestamp = CurrentTimestamp;
            byte[] data = packet.ToByteArray();
            udpClient.Send(data, data.Length, endpoint);
        }

        public NetworkPacket ReadRawPacket()
        {
            var remoteEP = new IPEndPoint(IPAddress.Any, 0);
            byte[] data = udpClient.Receive(ref remoteEP);
            Debug.Assert(remoteEP.Equals(endpoint));
            return NetworkPacket.FromByteArray(data);
        }

        public void SendPacket(DataPacket dataPacket)
        {
            ChannelData channelData = channels.GetOrNew(dataPacket.channel);

            dataPacket.from = localId;
            dataPacket.to = remoteId;

            List<NetworkPacket> packets = FragmentDataPacket(dataPacket, channelData.localSeq);
            foreach (NetworkPacket packet in packets)
            {
                // TODO: resending, acks
                if (packet.packetType == PacketType.Reliable || packet.packetType == PacketType.ReliableFragment)
                    channelData.sendQueue.Add(packet.seq, packet);
                SendRawPacket(packet);
                channelData.localSeq = (ushort)(packet.seq + 1);
            }
        }

        public enum PacketType : byte
        {
            /// <summary>
            /// Fragmentation and acknowledgement are disabled, no sequence numbers. Used only during MTU probing.
            /// </summary>
            Raw = 0,
            /// <summary>
            /// Request to start connection (for transport layer use only)
            /// </summary>
            SYN = 1,
            /// <summary>
            /// Acknowledge connection start (for transport layer use only)
            /// </summary>
            SYN_ACK = 2,
            /// <summary>
            /// Packets that don't require acknowledgement
            /// </summary>
            Unreliable = 3,
            /// <summary>
            /// If an Unreliable packet is fragmented, the following packets get this type
            /// </summary>
            UnreliableFragment = 4,
            /// <summary>
            /// Packets that require ackowledgement
            /// </summary>
            Reliable = 5,
            /// <summary>
            /// If a Reliable packet is fragmented, the following packets get this type
            /// </summary>
            ReliableFragment = 6,
            /// <summary>
            /// Ackowledgement for Reliable packets
            /// </summary>
            ReliableACK = 7,
            /// <summary>
            /// Request and acknowledge connection end (for transport layer use only)
            /// </summary>
            FIN = 9,
        }

        public class NetworkPacket
        {
            public bool usingChecksum = true;
            public PacketType packetType;
            public byte retryCount = 0;
            public byte from = 0;
            public byte to = 0;
            public byte channel = (byte)EStreamChannel.KEstreamChannelDiscovery;
            public ushort fragment = 0;
            public ushort seq = 0;
            public uint timestamp;
            public byte[] payload = new byte[0];

            public byte[] ToByteArray()
            {
                using (MemoryStream ms = new MemoryStream())
                using (BinaryWriter writer = new BinaryWriter(ms))
                {
                    writer.Write((byte)(((byte)packetType) | (usingChecksum ? 0x80 : 0)));
                    writer.Write(retryCount);
                    writer.Write(from);
                    writer.Write(to);
                    writer.Write(channel);
                    writer.Write(fragment);
                    writer.Write(seq);
                    writer.Write(timestamp);
                    writer.Write(payload);
                    writer.Flush();
                    if (usingChecksum)
                    {
                        byte[] data = ms.ToArray();
                        Crc32C crc = new Crc32C();
                        crc.Update(data, 0, data.Length);
                        uint checksum = crc.GetIntValue();
                        writer.Write(checksum);
                        writer.Flush();
                    }
                    return ms.ToArray();
                }
            }

            public static NetworkPacket FromByteArray(byte[] data)
            {
                NetworkPacket packet = new NetworkPacket();
                using (MemoryStream ms = new MemoryStream(data, false))
                using (BinaryReader reader = new BinaryReader(ms))
                {
                    byte packetFlags = reader.ReadByte();
                    packet.usingChecksum = (packetFlags & 0x80) != 0;
                    packet.packetType = (PacketType)(byte)(packetFlags & ~0x80);
                    packet.retryCount = reader.ReadByte();
                    packet.from = reader.ReadByte();
                    packet.to = reader.ReadByte();
                    packet.channel = reader.ReadByte();
                    packet.fragment = reader.ReadUInt16();
                    packet.seq = reader.ReadUInt16();
                    packet.timestamp = reader.ReadUInt32();
                    if (packet.usingChecksum)
                    {
                        packet.payload = reader.ReadBytes(data.Length - 13 - 4);
                        uint checksum = reader.ReadUInt32();
                        Crc32C crc = new Crc32C();
                        crc.Update(data, 0, data.Length - 4);
                        uint validChecksum = crc.GetIntValue();
                        if (checksum != validChecksum)
                            throw new IOException("Invalid checksum! Got 0x" + checksum.ToString("X8") + ", should be 0x" + validChecksum.ToString("X8"));
                    }
                    else
                    {
                        packet.payload = reader.ReadBytes(data.Length - 13);
                    }
                    return packet;
                }
            }
        }

        public class DataPacket
        {
            public PacketType packetType; // Reliable or Unreliable only
            public byte from = 0;
            public byte to = 0;
            public byte channel = (byte)EStreamChannel.KEstreamChannelDiscovery;
            public byte[] payload = new byte[0];
        };

        public class TypedMessage
        {
            public byte messageType;
            public byte[] message;
            
            public byte[] ToByteArray()
            {
                using (MemoryStream ms = new MemoryStream())
                using (BinaryWriter writer = new BinaryWriter(ms))
                {
                    writer.Write(messageType);
                    writer.Write(message);
                    writer.Flush();
                    return ms.ToArray();
                }
            }

            public static TypedMessage FromByteArray(byte[] data)
            {
                TypedMessage packet = new TypedMessage();
                using (MemoryStream ms = new MemoryStream(data, false))
                using (BinaryReader reader = new BinaryReader(ms))
                {
                    packet.messageType = reader.ReadByte();
                    packet.message = reader.ReadBytes(data.Length - 1);
                    return packet;
                }
            }
        }
    }
}
