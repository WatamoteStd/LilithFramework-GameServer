using System.Text;
using GameWorld.Commands;
using Interfaces;
using Network;
using Shared.GameData.Units;
using Shared.GameData.Units.Property;
using Shared.Lobby;
using Shared.Packets;
using Shared.Packets.PacketContent;
using Shared.Packets.UDP;

namespace Lobby;

/// <summary>
/// Pre-match connection state: who is connected, their nickname and hero,
/// and when to hand everyone over to the world.
///
/// Everything here runs on the network receive thread (one thread), so no
/// locking is needed. The only cross-thread call is EnqueuePlayerJoined.
/// </summary>
public sealed class LobbyManager
{
    private readonly NetworkManager _network;
    private readonly IWorldBroadcaster _world;

    private readonly List<LobbyPlayer> _players = new();
    private readonly Dictionary<uint, LobbyPlayer> _byPeer = new();

    private bool _started;
    private ushort _nextEntityId = 1;

    public LobbyManager(NetworkManager network, IWorldBroadcaster world)
    {
        _network = network;
        _world = world;
    }

    public bool IsStarted => _started;

    public void HandlePacket(uint peerId, PacketTypes type, ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 2) return;
        ReadOnlySpan<byte> payload = packet[2..];

        switch (type)
        {
            case PacketTypes.C2S_HandshakeRequest:
                OnHandshake(peerId, payload);
                break;

            case PacketTypes.C2S_LobbyJoin:
                OnJoin(peerId, payload);
                break;

            case PacketTypes.C2S_LobbyReady:
                OnReady(peerId, payload);
                break;
        }
    }

    private void OnHandshake(uint peerId, ReadOnlySpan<byte> payload)
    {
        C2S_HandshakeRequestPacket request = default;
        PacketTranslator.Read(payload, ref request);

        if (request.Version != C2S_HandshakeRequestPacket.CurrentProtocolVersion)
        {
            Console.WriteLine($"[Lobby] Peer {peerId} rejected: protocol {request.Version}, expected {C2S_HandshakeRequestPacket.CurrentProtocolVersion}.");
            SendDrop(peerId);
            return;
        }

        if (_started || _players.Count >= S2C_LobbyStatePacket.MaxPlayers)
        {
            Console.WriteLine($"[Lobby] Peer {peerId} rejected: lobby is full or the match already started.");
            SendDrop(peerId);
            return;
        }

        if (_byPeer.TryGetValue(peerId, out LobbyPlayer? existing))
        {
            // A resent handshake keeps the same identity: just repeat the welcome.
            SendWelcome(existing);
            SendState(existing);
            return;
        }

        LobbyPlayer player = new(
            peerId,
            request.ClientId,
            $"Player{peerId}",
            FirstHero(),
            NextTeam());

        _players.Add(player);
        _byPeer.Add(peerId, player);

        Console.WriteLine($"[Lobby] Peer {peerId} joined. Players: {_players.Count}.");

        SendWelcome(player);
        BroadcastState();
    }

    private void OnJoin(uint peerId, ReadOnlySpan<byte> payload)
    {
        if (!_byPeer.TryGetValue(peerId, out LobbyPlayer? player)) return;

        C2S_LobbyJoinPacket join = default;
        PacketTranslator.Read(payload, ref join);

        if (UnitsLibrary.TryGetHero(join.Hero, out _))
        {
            player.Hero = join.Hero;
        }

        Span<byte> normalized = stackalloc byte[NicknameRules.MaxBytes];
        if (join.Nickname is not null && NicknameRules.TryNormalize(join.Nickname, normalized, out int length))
        {
            player.Nickname = Encoding.UTF8.GetString(normalized[..length]);
        }

        BroadcastState();
    }

    private void OnReady(uint peerId, ReadOnlySpan<byte> payload)
    {
        if (!_byPeer.TryGetValue(peerId, out LobbyPlayer? player)) return;

        C2S_LobbyReadyPacket ready = default;
        PacketTranslator.Read(payload, ref ready);

        player.Ready = ready.Ready != 0;
        BroadcastState();

        TryStartMatch();
    }

    private void TryStartMatch()
    {
        if (_started || _players.Count == 0) return;

        for (int i = 0; i < _players.Count; i++)
        {
            if (!_players[i].Ready) return;
        }

        _started = true;
        Console.WriteLine($"[Lobby] All {_players.Count} player(s) ready. Starting match.");

        BroadcastState();

        for (int i = 0; i < _players.Count; i++)
        {
            LobbyPlayer player = _players[i];
            player.EntityId = _nextEntityId++;

            S2C_HandshakeResponsePacket response = new()
            {
                Response = S2C_HandshakeResponsePacket.ResponseType.Ok,
                EntityId = player.EntityId,
                Team = player.Team,
                ServerTick = 0,
                Hero = player.Hero
            };

            _network.SendToPeer(player.PeerId, ref response);
            _world.EnqueuePlayerJoined(new PlayerJoinedCommand
            {
                PeerId = player.PeerId,
                EntityId = player.EntityId,
                Team = player.Team,
                Hero = player.Hero
            });

            Console.WriteLine($"[Lobby] Peer {player.PeerId} -> entity {player.EntityId}, team {player.Team}, hero {player.Hero}, nick '{player.Nickname}'.");
        }
    }

    private void BroadcastState()
    {
        for (int i = 0; i < _players.Count; i++)
        {
            SendState(_players[i]);
        }
    }

    private void SendState(LobbyPlayer player)
    {
        S2C_LobbyStatePacket state = S2C_LobbyStatePacket.Create();
        state.Count = (byte)_players.Count;

        for (int i = 0; i < _players.Count; i++)
        {
            LobbyPlayer source = _players[i];
            ref LobbyPlayerState row = ref state.Players[i];

            row.PlayerId = source.PeerId;
            row.SetNickname(Encoding.UTF8.GetBytes(source.Nickname));
            row.Hero = source.Hero;
            row.Team = source.Team;
            row.Ready = source.Ready ? (byte)1 : (byte)0;
        }

        _network.SendToPeer(player.PeerId, ref state);
    }

    private void SendWelcome(LobbyPlayer player)
    {
        S2C_LobbyWelcomePacket welcome = new() { PlayerId = player.PeerId };
        _network.SendToPeer(player.PeerId, ref welcome);
    }

    private void SendDrop(uint peerId)
    {
        S2C_HandshakeResponsePacket drop = new()
        {
            Response = S2C_HandshakeResponsePacket.ResponseType.Droped
        };

        _network.SendToPeer(peerId, ref drop);
    }

    private static HeroId FirstHero()
    {
        for (int id = 1; id <= UnitsLibrary.Count; id++)
        {
            HeroId hero = (HeroId)id;
            if (UnitsLibrary.TryGetHero(hero, out _)) return hero;
        }

        return HeroId.None;
    }

    private MatchTeam NextTeam()
    {
        int light = 0;
        int dark = 0;

        for (int i = 0; i < _players.Count; i++)
        {
            if (_players[i].Team == MatchTeam.Light) light++;
            else if (_players[i].Team == MatchTeam.Dark) dark++;
        }

        return light <= dark ? MatchTeam.Light : MatchTeam.Dark;
    }
}
