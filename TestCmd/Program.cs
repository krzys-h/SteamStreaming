using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.Collections;
using SteamStreaming.Enums;
using SteamStreaming.Protocols.Application;
using SteamStreaming.Protocols.Transport;

namespace SteamStreaming
{
    class Program
    {
        static void Main(string[] args)
        {
            SteamDiscovery steamDiscovery = new SteamDiscovery(1337);

            steamDiscovery.MyStatus = new CMsgRemoteClientBroadcastStatus()
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
            steamDiscovery.MyStatus.Users.Add(new CMsgRemoteClientBroadcastStatus.Types.User()
            {
                Steamid = 76561198009414634,
                AuthKeyId = 00000000 // removed
            });
            byte[] psk = Hexlify.StringToByteArray("0000000000000000000000000000000000000000000000000000000000000000"); // removed

            steamDiscovery.OnClientOnline += delegate (SteamDiscovery.Client client) {
                Console.WriteLine("> A new client " + client.status.Hostname + " is waiting on " + client.from.Address.ToString() + ":" + client.status.ConnectPort);

                /*foreach (var user in client.status.Users)
                {
                    WebRequest request = WebRequest.Create("https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?format=json&steamids=" + user.Steamid + "&key=00000000000000000000000000000000"); // removed
                    WebResponse response = request.GetResponse();
                    using (Stream respStream = response.GetResponseStream())
                    using (StreamReader readStream = new StreamReader(respStream, Encoding.UTF8))
                    {
                        string data = readStream.ReadToEnd();
                        Match m = Regex.Match(data, "\"personaname\":\"(.*?)\"");
                        Console.WriteLine("LOGGED_USER;" + m.Groups[1] + ";" + user.Steamid + ";" + client.status.Hostname + ";" + client.from.Address.ToString() + ":" + client.status.ConnectPort + ";" + client.status);
                    }
                }*/
            };
            steamDiscovery.OnClientStatusUpdate += delegate (SteamDiscovery.Client client) {
                Console.WriteLine("> Client status update broadcast from " + client.status.Hostname + " (" + client.from.Address.ToString() + ":" + client.status.ConnectPort + ")");
            };
            steamDiscovery.OnClientOffline += delegate (SteamDiscovery.Client client) {
                Console.WriteLine("> Client " + client.status.Hostname + " went offline (" + client.from.Address.ToString() + ":" + client.status.ConnectPort + ")");
            };

            steamDiscovery.Start();
            
            /*TlsPskClient tlsClient = new TlsPskClient();
            tlsClient.Connect("127.0.0.1:27036", "steam", psk);
            SteamRemote steamRemote = new SteamRemote(tlsClient, false, steamDiscovery.ClientId, steamDiscovery.InstanceId, steamDiscovery.MyStatus);
            steamRemote.MyApps.Add(new CMsgRemoteClientAppStatus.Types.AppStatus()
            {
                AppId = 391540,
                AppState = 4
            });
            steamRemote.Start();
            await Task.Delay(1000);
            steamRemote.WriteRawPacket(EMsg.RemoteClientStartStream, new CMsgRemoteClientStartStream()
            {
                AppId = 391540,
                LaunchOption = -1,
                MaximumResolutionX = 1920,
                MaximumResolutionY = 1080,
                AudioChannelCount = 2
            }.ToByteArray());
            while (true) ;*/

            steamDiscovery.Stop();
        }
    }
}
