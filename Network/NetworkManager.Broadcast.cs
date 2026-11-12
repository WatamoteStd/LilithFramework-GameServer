
using Shared.Packets;
using Shared.Packets.PacketContent;
using Shared.Packets.UDP;

namespace Network;

public partial class NetworkManager
{
    
    public void SendToPeer(uint peerId, ref S2C_HandshakeResponsePacket pck)
    {
        Span<byte> buffer = stackalloc byte[64];
        int length = PacketTranslator.Serialize(PacketTypes.S2C_HandshakeResponse, buffer, ref pck);

        SendTo(peerId, buffer[..length]);
    }

    public void SendToPeer(uint peerId, ref S2C_LobbyWelcomePacket pck)
    {
        Span<byte> buffer = stackalloc byte[16];
        int length = PacketTranslator.Serialize(PacketTypes.S2C_LobbyWelcome, buffer, ref pck);

        SendTo(peerId, buffer[..length]);
    }

    public void SendToPeer(uint peerId, ref S2C_LobbyStatePacket pck)
    {
        Span<byte> buffer = stackalloc byte[S2C_LobbyStatePacket.MaxSize + 2];
        int length = PacketTranslator.Serialize(PacketTypes.S2C_LobbyState, buffer, ref pck);

        SendTo(peerId, buffer[..length]);
    }


    public void SendToAllPeers(ref S2C_WorldSnapshotPacket pck)
    {
        
        var peers = _reiHost.GetPeers();
        Span<byte> buffer = stackalloc byte[1500];
        int writen = PacketTranslator.Serialize(PacketTypes.S2C_WorldSnapshot, buffer, ref pck);

        for(int i = 0; i < peers.Length; i++)
        {
            SendTo(peers[i].Id, buffer[..writen]);
        }

    }

    public bool HasPeers() => _reiHost.PeerCount > 0;


    private void SendTo(uint peerId, ReadOnlySpan<byte> data)
    {
        var peers = _reiHost.GetPeers();
        for(int i = 0; i < peers.Length; i++)
        {
            if(peers[i].Id == peerId)
            {
                _reiHost.SendTo(peers[i], data);
                return;
            }
        }
    }

}