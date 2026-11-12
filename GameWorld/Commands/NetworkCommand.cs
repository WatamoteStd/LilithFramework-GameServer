
using Shared.Packets.UDP;

namespace GameWorld.Commands;

public readonly struct NetworkCommand
{
    
    public required byte[] Payload {get; init;}
    public required ushort PayloadLength {get; init;}
    public required PacketTypes Type {get; init;}
    public required uint PeerId {get; init;}

}