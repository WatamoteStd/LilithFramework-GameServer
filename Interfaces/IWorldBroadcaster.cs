
using GameWorld.Commands;

namespace Interfaces;

public interface IWorldBroadcaster
{
    void EnqueueCommand(NetworkCommand cmd);

    /// <summary>
    /// Called by the lobby when a player is ready and the match starts.
    /// Implementations must be thread-safe: the lobby runs on the network thread.
    /// </summary>
    void EnqueuePlayerJoined(PlayerJoinedCommand cmd);
}