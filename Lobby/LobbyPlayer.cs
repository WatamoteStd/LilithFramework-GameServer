using Shared.GameData.Units;
using Shared.GameData.Units.Property;

namespace Lobby;

/// <summary>
/// One connected player before the match starts. EntityId stays 0 until the
/// lobby hands the player over to the world.
/// </summary>
internal sealed class LobbyPlayer
{
    public uint PeerId { get; }
    public ulong ClientId { get; }
    public string Nickname { get; set; }
    public HeroId Hero { get; set; }
    public MatchTeam Team { get; set; }
    public bool Ready { get; set; }
    public ushort EntityId { get; set; }

    public LobbyPlayer(uint peerId, ulong clientId, string nickname, HeroId hero, MatchTeam team)
    {
        PeerId = peerId;
        ClientId = clientId;
        Nickname = nickname;
        Hero = hero;
        Team = team;
    }
}
