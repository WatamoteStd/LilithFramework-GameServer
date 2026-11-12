

using System.Buffers;
using System.Buffers.Binary;
using Core;
using GameWorld.Commands;
using Interfaces;
using Shared.Packets.UDP;

namespace Network;

public partial class NetworkManager : INetworkBroadcaster
{

    private IWorldBroadcaster? _worldBroadcaster;
    private Lobby.LobbyManager? _lobby;
    private readonly ReiHost _reiHost;
    private byte[] peerBuffer = new byte[1500];
    private Thread? _receivedThread;
    
    public NetworkManager(ushort port)
    {
        
        _reiHost = new ReiHost(port);

    }

    public void Start(IWorldBroadcaster broadcaster, Lobby.LobbyManager lobby)
    {
        
        _reiHost.Start();
        _worldBroadcaster = broadcaster;
        _lobby = lobby;

        _receivedThread = new Thread(ReceivedLoop) { IsBackground = true, Name = "Net-Receive"};
        _receivedThread.Start();

    }

    private void ReceivedLoop()
    {

        while(true)
        {
            
            foreach(var peer in _reiHost.GetPeers())
            {
        
                while (peer.TryReadPacket(peerBuffer, out var bytesRead))
                {
                    if (bytesRead < 2) continue;

                    PacketTypes packetType = (PacketTypes)BinaryPrimitives.ReadUInt16LittleEndian(peerBuffer);

                    switch(packetType)
                    {
                        // Connection identity and hero choice belong to the lobby.
                        case PacketTypes.C2S_HandshakeRequest:
                        case PacketTypes.C2S_LobbyJoin:
                        case PacketTypes.C2S_LobbyReady:
                            {
                                _lobby?.HandlePacket(peer.Id, packetType, peerBuffer.AsSpan(0, bytesRead));
                            }
                        break;

                        // Gameplay intents go to the world thread.
                        case PacketTypes.C2S_MoveIntent:
                        case PacketTypes.C2S_AttackIntent:
                        case PacketTypes.C2S_ChatMessage:
                            {
                                ForwardToWorld(peer.Id, packetType, bytesRead);
                            }
                        break;

                        default:
                            {
                                Console.WriteLine($"[Net] Drop unknown packet type {(ushort)packetType}.");
                            }
                        break;
                    }


                }

            }

            Thread.Sleep(1);
        }
    }

    /// <summary>
    /// Copies the raw frame (including the two-byte packet type) into a pooled
    /// buffer and hands it to the world: the network thread never touches world state.
    /// </summary>
    private void ForwardToWorld(uint peerId, PacketTypes type, int bytesRead)
    {
        byte[] rented = ArrayPool<byte>.Shared.Rent(peerBuffer.Length);

        try
        {
            peerBuffer.AsSpan(0, bytesRead).CopyTo(rented);
            _worldBroadcaster!.EnqueueCommand(new NetworkCommand
            {
                Payload = rented,
                PayloadLength = (ushort)bytesRead,
                Type = type,
                PeerId = peerId
            });
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(rented);
            throw;
        }
    }

}