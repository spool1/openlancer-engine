// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LibreLancer.Net.Protocol;
using LibreLancer.Net.Protocol.RpcPackets;
using LibreLancer.Server;
using LiteNetLib;
using LiteNetLib.Utils;
using Microsoft.VisualBasic;

namespace LibreLancer.Net
{
	public class GameListener
    {
        static GameListener()
        {
            NetDebug.Logger = new NetDebugLogger();
        }

        private GameServer game;
        private static readonly object TagConnecting = new();

        public int Port = LNetConst.DefaultPort;
        public int MaxConnections = 200;
        public string AppIdentifier = LNetConst.DefaultAppIdent;

        private bool running = false;
        private Thread netThread = null!;
		public NetManager Server = null!;
        private HttpClient http = null!;
        private IPConnectionLimiter limiter = new(16);

		public GameListener(GameServer srv)
        {
            this.game = srv;
        }

		public void Start()
		{
			running = true;
            netThread = new Thread(NetThread)
            {
                Name = "Server Listener"
            };
            netThread.Start();
		}

        private AutoResetEvent stopHandle = null!;
        private DateTime lastKeyPrint = DateTime.UnixEpoch;

        void PlayerConnect(ConnectionRequest request, Guid guid, IPKey ipKey)
        {
            var peer = request.Accept();
            var remote = new RemotePacketClient(peer);
            var p = new Player(remote, game, guid, ipKey);
            peer.Tag = p;
            _ = Task.Run(async () => await p.OnLoggedIn());
            lock (game.ConnectedPlayers)
            {
                game.ConnectedPlayers.Add(p);
            }
        }

        private void NetThread()
        {
            stopHandle = new AutoResetEvent(false);
            http = new HttpClient();
            EventBasedNetListener listener = new EventBasedNetListener();
            EventBasedNetListener broadcastListener = new EventBasedNetListener();
            NetManager broadcastServer = new NetManager(broadcastListener);
            var unique = Guid.NewGuid();
            listener.ConnectionRequestEvent += request =>
            {
                if (Server.ConnectedPeersCount > MaxConnections)
                {
                    var dw = new PacketWriter();
                    dw.Put(DisconnectReason.TooManyConnections);
                    request.Reject(dw);
                    return;
                }

                var ipKey = IPKey.From(request.RemoteEndPoint.Address);
                if (!limiter.TryConnect(ipKey))
                {
                    FLLog.Debug("Server", $"Connect limit exceeded {request.RemoteEndPoint.Address}");
                    var dw = new PacketWriter();
                    dw.Put(DisconnectReason.InvalidEndpoint);
                    request.Reject(dw);
                    return;
                }

                if (!new PacketReader(request.Data).TryGetString(out var key))
                {
                    FLLog.Debug("Server", $"Connect with no key {request.RemoteEndPoint}");
                    var dw = new PacketWriter();
                    dw.Put(DisconnectReason.NoProtocolHash);
                    limiter.Disconnect(ipKey);
                    request.Reject(dw);
                    return;
                }
                if (key != AppIdentifier + GeneratedProtocol.PROTOCOL_HASH)
                {
                    var n = DateTime.UtcNow;
                    if (n - lastKeyPrint > TimeSpan.FromSeconds(10))
                    {
                        FLLog.Info("Server", $"{request.RemoteEndPoint} Bad Key: '{key}' != '{AppIdentifier}{GeneratedProtocol.PROTOCOL_HASH}'");
                        lastKeyPrint = n;
                    }
                    var dw = new PacketWriter();
                    dw.Put(DisconnectReason.BadProtocolHash);
                    limiter.Disconnect(ipKey);
                    request.Reject(dw);
                    return;
                }

                var reqReader = new PacketReader(request.Data);
                if (!AuthenticationInfo.TryGet(reqReader, out var auth))
                {
                    var dw = new PacketWriter();
                    dw.Put(DisconnectReason.AuthenticationInfo);
                    if (!string.IsNullOrEmpty(game.LoginUrl))
                    {
                        dw.Put((byte)AuthenticationKind.Token);
                        dw.Put(game.LoginUrl);
                    }
                    else
                    {
                        dw.Put((byte)AuthenticationKind.GUID);
                        Span<byte> guidBytes = stackalloc byte[16];
                        game.ServerGuid.TryWriteBytes(guidBytes);
                        dw.Put(guidBytes);
                    }
                    FLLog.Debug("Server", "Sending login info");
                    limiter.Disconnect(ipKey);
                    request.Reject(dw);
                }
                else if(!string.IsNullOrEmpty(game.LoginUrl))
                {
                    if (auth.Kind != AuthenticationKind.Token)
                    {
                        FLLog.Info("Login", $"Login failed for {request.RemoteEndPoint}");
                        var dw = new PacketWriter();
                        dw.Put(DisconnectReason.LoginError);
                        limiter.Disconnect(ipKey);
                        request.Reject(dw);
                    }
                    else
                    {
                        Task.Run(async () =>
                        {
                            var guid = await http.VerifyToken(game.LoginUrl, auth.GetString());
                            if (guid == Guid.Empty)
                            {
                                FLLog.Info("Login", $"Login failed for {request.RemoteEndPoint}");
                                var dw = new PacketWriter();
                                dw.Put(DisconnectReason.LoginError);
                                limiter.Disconnect(ipKey);
                                request.Reject(dw);
                            }
                            else
                            {
                                PlayerConnect(request, guid, ipKey);
                            }
                        });
                    }
                }
                else if (auth.TryGetGuid(out var authGuid))
                {
                    PlayerConnect(request, authGuid, ipKey);
                }
                else
                {
                    FLLog.Info("Login", $"Invalid GUID for {request.RemoteEndPoint}");
                    var dw = new PacketWriter();
                    dw.Put(DisconnectReason.ConnectionError);
                    limiter.Disconnect(ipKey);
                    request.Reject(dw);
                }
            };
            listener.PeerConnectedEvent += peer =>
            {
                FLLog.Info("Server", $"Connected: {peer.Address}");
            };
            listener.PeerDisconnectedEvent += (peer, info) =>
            {
                FLLog.Info("Server", $"Disconnected: {peer.Address}. Reason: {info.Reason}");
                if (peer.Tag is Player player)
                {
                    limiter.Disconnect(player.Key);
                    player.Disconnected();
                    lock (game.ConnectedPlayers)
                    {
                        game.ConnectedPlayers.Remove(player);
                    }
                }
            };
            broadcastListener.ConnectionRequestEvent += request => request.RejectForce();
            broadcastListener.NetworkReceiveUnconnectedEvent += (point, reader, type) =>
            {
                if (type == UnconnectedMessageType.Broadcast)
                {
                    new PacketReader(reader).TryGetULong(out ulong key);
                    if (key != LNetConst.BroadcastKey) return;
                    var dw = new PacketWriter();
                    dw.Put((byte) 1);
                    dw.Put(unique);
                    dw.Put(game.ServerName);
                    dw.Put(game.ServerDescription);
                    dw.Put(game.GameData.Items.DataVersion);
                    dw.PutVariableUInt32((uint) Server.ConnectedPeersCount);
                    dw.PutVariableUInt32((uint) MaxConnections);
                    dw.PutVariableUInt32((uint)Port);
                    broadcastServer.SendUnconnectedMessage(dw, point);
                }
            };
            listener.NetworkReceiveUnconnectedEvent += (point, msg, type) =>
            {
                try
                {
                    var reader = new PacketReader(msg);
                    if (type == UnconnectedMessageType.BasicMessage)
                    {
                        if (!reader.TryGetUInt(out uint magic)) return;
                        if (magic != LNetConst.PingMagic) return;
                        var dw = new PacketWriter();
                        dw.Put((byte) 0);
                        Server.SendUnconnectedMessage(dw, point);
                    }
                }
                finally
                {
                    msg.Recycle();
                }
            };
            listener.NetworkReceiveEvent += (peer, msg, channel, method) =>
            {
                try
                {
                    var reader = new PacketReader(msg);
                    var pkt = Packets.Read(reader);
                    var player = (Player) peer.Tag;
                    player.EnqueuePacket(pkt);
                }
#if !DEBUG
                catch (Exception e)
                {
                    FLLog.Warning("Server", $"Error when reading packet {e}");
                    var dw = new PacketWriter();
                    dw.Put(DisconnectReason.ConnectionError);
                    peer.Disconnect(dw);
                    if (peer.Tag is Player p)
                        p.Disconnected();
                }
#else
                catch (Exception)
                {
                    throw;
                }
                #endif
                finally
                {
                    msg.Recycle();
                }
            };
            listener.DeliveryEvent += (peer, data) =>
            {
                if (data is Action onAck)
                    onAck();
            };
            broadcastServer.ReuseAddress = true;
            broadcastServer.IPv6Enabled = true;
            broadcastServer.BroadcastReceiveEnabled = true;
            broadcastServer.UnsyncedEvents = true;
            broadcastServer.Start(LNetConst.BroadcastPort);
            Server = new NetManager(listener)
            {
                IPv6Enabled = true,
                UnconnectedMessagesEnabled = true,
                ChannelsCount = 3,
                UnsyncedEvents = true
            };
            Server.Start(Port);
            FLLog.Info("Server", "Listening on port " + Port);
            stopHandle.WaitOne();
            http.Dispose();
            Server.Stop();
            broadcastServer.Stop();
        }

		public void Stop()
        {
            stopHandle.Set();
			running = false;
			netThread.Join();
        }
	}
}
