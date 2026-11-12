using Shared.GameData.Units;
using Shared.GameData.Units.Property;

namespace GameWorld.Commands;

/// <summary>
/// Lobby -> world handoff. The lobby owns identity and hero choice; the world
/// only creates the unit and starts simulating it.
/// </summary>
public readonly struct PlayerJoinedCommand
{
    public required uint PeerId { get; init; }
    public required ushort EntityId { get; init; }
    public required MatchTeam Team { get; init; }
    public required HeroId Hero { get; init; }
}
