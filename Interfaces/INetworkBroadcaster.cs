
using Shared.Packets.PacketContent;
using Shared.Packets.UDP;

namespace Interfaces;

public interface INetworkBroadcaster
{
    
    void SendToPeer(uint peerId, ref S2C_HandshakeResponsePacket pck);

}