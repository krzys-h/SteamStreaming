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

namespace SteamStreaming.Protocols.Application
{
    using DataPacket = SteamStreamTransport.DataPacket;
    using TypedMessage = SteamStreamTransport.TypedMessage;
    using PacketType = SteamStreamTransport.PacketType;
    public abstract class SteamStreamBase {
        protected SteamStreamTransport conn;
        protected readonly byte[] authKey;

        protected ulong encryptCounter = 0;

        public SteamStreamBase(IPEndPoint endpoint, byte[] authKey)
        {
            conn = new SteamStreamTransport(endpoint);
            conn.OnPacketReceived += OnPacketReceived;
            this.authKey = authKey;
        }

        protected void Connect()
        {
            conn.Connect();
        }

        protected void Close()
        {
            conn.Close();
        }

        private void OnPacketReceived(DataPacket dataPacket)
        {
            TypedMessage msg = TypedMessage.FromByteArray(dataPacket.payload);
            if (dataPacket.channel < (byte)EStreamChannel.KEstreamChannelDataChannelStart)
            {
                if (dataPacket.channel == (byte)EStreamChannel.KEstreamChannelDiscovery)
                    throw new NotImplementedException();
                else if (dataPacket.channel == (byte)EStreamChannel.KEstreamChannelControl)
                    ProcessControlMessage((EStreamControlMessage)msg.messageType, msg.message);
                else if (dataPacket.channel == (byte)EStreamChannel.KEstreamChannelStats)
                    ProcessStatsMessage((EStreamStatsMessage)msg.messageType, msg.message);
            }
            else
            {
                ProcessStreamDataMessage(dataPacket.channel, (EStreamDataMessage)msg.messageType, msg.message);
            }
        }

        protected abstract void ProcessControlMessage(EStreamControlMessage type, byte[] messageBuffer);
        protected abstract void ProcessStatsMessage(EStreamStatsMessage type, byte[] messageBuffer);
        protected abstract void ProcessStreamDataMessage(byte channel, EStreamDataMessage type, byte[] payload);

        protected void SendControlMessage(EStreamControlMessage type, byte[] messageBuffer)
        {
            if (type != EStreamControlMessage.KEstreamControlClientHandshake &&
                type != EStreamControlMessage.KEstreamControlServerHandshake &&
                type != EStreamControlMessage.KEstreamControlAuthenticationRequest &&
                type != EStreamControlMessage.KEstreamControlAuthenticationResponse)
                messageBuffer = EncryptPacket(messageBuffer);

            conn.SendPacket(new DataPacket()
            {
                packetType = PacketType.Reliable,
                channel = (byte)EStreamChannel.KEstreamChannelControl,
                payload = new TypedMessage()
                {
                    messageType = (byte)type,
                    message = messageBuffer
                }.ToByteArray()
            });
        }

        protected void SendControlMessage(EStreamControlMessage type, IMessage message)
        {
            SendControlMessage(type, message.ToByteArray());
        }

        protected void SendStatsMessage(EStreamStatsMessage type, byte[] messageBuffer)
        {
            conn.SendPacket(new DataPacket()
            {
                packetType = PacketType.Reliable,
                channel = (byte)EStreamChannel.KEstreamChannelStats,
                payload = new TypedMessage()
                {
                    messageType = (byte)type,
                    message = messageBuffer
                }.ToByteArray()
            });
        }

        protected void SendStatsMessage(EStreamStatsMessage type, IMessage message)
        {
            SendStatsMessage(type, message.ToByteArray());
        }

        protected byte[] DecryptPacket(byte[] data)
        {
            byte[] hmac = new byte[16];
            byte[] encData = new byte[data.Length - 16];
            Array.Copy(data, 0, hmac, 0, 16);
            Array.Copy(data, 16, encData, 0, data.Length - 16);

            Aes aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Key = authKey;
            aes.IV = hmac;
            ICryptoTransform aesDecryptor = aes.CreateDecryptor();

            using (MemoryStream ms = new MemoryStream())
            using (CryptoStream cryptoStream = new CryptoStream(ms, aesDecryptor, CryptoStreamMode.Write))
            {
                cryptoStream.Write(encData, 0, encData.Length);
                cryptoStream.FlushFinalBlock();
                byte[] decData = ms.ToArray();

                byte[] hmacComputed = new HMACMD5(authKey).ComputeHash(decData);
                if (!hmac.SequenceEqual(hmacComputed))
                    throw new IOException("Invalid HMAC");

                byte[] counterData = new byte[8]; // TODO: check the counter
                byte[] payload = new byte[decData.Length - 8];
                Array.Copy(decData, 0, counterData, 0, 8);
                Array.Copy(decData, 8, payload, 0, decData.Length - 8);

                return payload;
            }
        }

        protected byte[] EncryptPacket(byte[] data)
        {
            byte[] decData = new byte[data.Length + 8];
            Array.Copy(BitConverter.GetBytes(encryptCounter++), 0, decData, 0, 8);
            Array.Copy(data, 0, decData, 8, data.Length);

            byte[] hmac = new HMACMD5(authKey).ComputeHash(decData);

            Aes aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Key = authKey;
            aes.IV = hmac;
            ICryptoTransform aesEncryptor = aes.CreateEncryptor();

            using (MemoryStream ms = new MemoryStream())
            using (CryptoStream cryptoStream = new CryptoStream(ms, aesEncryptor, CryptoStreamMode.Write))
            {
                cryptoStream.Write(decData, 0, decData.Length);
                cryptoStream.FlushFinalBlock();
                byte[] encData = ms.ToArray();

                byte[] payload = new byte[encData.Length + 16];
                Array.Copy(hmac, 0, payload, 0, 16);
                Array.Copy(encData, 0, payload, 16, encData.Length);
                return payload;
            }
        }
    }
}
