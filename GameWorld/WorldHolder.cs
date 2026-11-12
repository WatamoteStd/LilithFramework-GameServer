using Shared.Packets.UDP;
using System.Collections.Concurrent;
using System.Numerics;
using System.Threading.Channels;
using GameWorld.Commands;
using Shared.GameData.Entities;
using Shared.GameData.Entities.Classes;
using Shared.GameData.Units;
using Shared.GameData.Units.Property;
using Shared.Packets;
using Shared.Packets.PacketContent;
using System.Buffers;
using Entities;
using Interfaces;
using System.Diagnostics;
using GameWorld.Entities;

namespace GameWorld;

public class WorldHolder : IWorldBroadcaster
{
    
    private readonly Dictionary<uint, ServerUnitEntity> _peerIdToUnit = new();
    private ushort _nextEntityId = 1;
    private Thread? _gameThread;
    private ConcurrentQueue<NetworkCommand> _commandsQueue = new();
    private readonly ConcurrentQueue<PlayerJoinedCommand> _playerJoins = new();
    private readonly INetworkBroadcaster _broadcaster;
    private volatile bool _isRunning = false;

    // TICK VALUES ==========================================
    private const float FIXED_DELTA_TIME = 1f / 60f; // 0.01666.. Ms (simulation step)
    private readonly double dtTicks; // QPC step (like delta but in CPU value)
    private readonly double maxAcc; // QPC (max ticks clamp)
    private double _acc; // QPC (copilka)
    private long _prev; // QPC 
    private readonly Stopwatch _clock = new();
    private long _tick = 0; 


    // NETWORKING ==============================================

    private S2C_WorldSnapshotPacket _snapshot = S2C_WorldSnapshotPacket.Create();

    public WorldHolder(INetworkBroadcaster broadcaster)
    {
        _broadcaster = broadcaster;
        dtTicks = Stopwatch.Frequency / 60.0;
        maxAcc = 5.0 * dtTicks;

        Execute();
    }

    private void Execute()
    {
        _clock.Start();

        _isRunning = true;
        _gameThread = new Thread(Update);
        _gameThread.IsBackground = true;
        _gameThread.Name = "World-Tick";
        _gameThread.Start();

    }

    /// <summary>
    /// Lobby handoff: entity id, team and hero were already chosen there, so
    /// the world only creates the unit and keeps the id counter ahead.
    /// </summary>
    public UnitEntity AddPlayer(uint peerId, ushort entityId, MatchTeam team, HeroId hero)
    {
        ServerUnitEntity playerUnit = new ServerUnitEntity(peerId, entityId, new Vector3(0, 1, 0), team, hero);
        _peerIdToUnit[peerId] = playerUnit;

        if (entityId >= _nextEntityId) _nextEntityId = (ushort)(entityId + 1);
        return playerUnit;
    }

    private void Update()
    {
        _prev = _clock.ElapsedTicks;
        
        while(_isRunning)
        {

            long now = _clock.ElapsedTicks;
            _acc += now - _prev;
            _prev = now;

            if(_acc > maxAcc) _acc = maxAcc;

            while(_acc >= dtTicks)
            {
                Tick(FIXED_DELTA_TIME);
                _acc -= dtTicks;
            }

            double remaining = dtTicks - _acc;

            if (remaining > 0)
            {
                long deadline = _clock.ElapsedTicks + (long)remaining + 1;

                long sleepTicks = (long)remaining - Stopwatch.Frequency / 1000;
                if(sleepTicks > 0)
                {
                    int sleepMs = (int)(sleepTicks * 1000 / Stopwatch.Frequency);
                    if(sleepMs > 0 ) Thread.Sleep(sleepMs);
                }

                while(_clock.ElapsedTicks < deadline) 
                    Thread.SpinWait(50);
            }

        }

    }

    private void Tick(float deltaTime)
    {
        
        while(_playerJoins.TryDequeue(out PlayerJoinedCommand join))
        {
            UnitEntity joined = AddPlayer(join.PeerId, join.EntityId, join.Team, join.Hero);
            Console.WriteLine($"[World] Player joined: peer {join.PeerId}, entity {joined.EntityId}, team {joined.EntityTeam}, hero {joined.Hero}.");
        }

        while(_commandsQueue.TryDequeue(out var cmd))
        {
                
                switch(cmd.Type)
                {
                    case PacketTypes.C2S_ChatMessage:
                        HandleChat(cmd);
                    break;

                    case PacketTypes.C2S_MoveIntent:
                    {

                        if(cmd.PayloadLength == 0)
                        {
                            ArrayPool<byte>.Shared.Return(cmd.Payload);
                            break;
                        }

                        if(!_peerIdToUnit.TryGetValue(cmd.PeerId, out var unit))
                        {
                            Console.WriteLine($"[WorldHolder] PeerId:{cmd.PeerId} no existing units found.");
                            ArrayPool<byte>.Shared.Return(cmd.Payload);
                            continue;
                        }

                        C2S_MoveIntentPacket pck = default;
                        PacketTranslator.Read(cmd.Payload.AsSpan(2, cmd.PayloadLength - 2), ref pck);
                        ArrayPool<byte>.Shared.Return(cmd.Payload);

                        unit.SetMoveTarget(pck.X, pck.Z);

                    }
                    break;

                    default:
                        Console.WriteLine($"[World] Unhandled cmd: {cmd.Type}");
                        if(cmd.Payload is not null) ArrayPool<byte>.Shared.Return(cmd.Payload);
                    break;

                }

        }

        foreach(var pair in _peerIdToUnit)
        {
            
            var entity = pair.Value;
            entity.Update(FIXED_DELTA_TIME);

        }

        _tick++;

        if(_peerIdToUnit.Count > 0 && _broadcaster.HasPeers())
        {
            BuildSnapshot();
            _broadcaster.SendToAllPeers(ref _snapshot);
        }

    }

    /// <summary>
    /// Avoid race conditions.
    /// Added command from server thread to world.
    /// </summary>
    /// <param name="command"></param>
    public void EnqueueCommand(NetworkCommand command) => _commandsQueue.Enqueue(command);

    public void EnqueuePlayerJoined(PlayerJoinedCommand command) => _playerJoins.Enqueue(command);

    // Chat history, keyed by simulation tick. Kept on the world thread: no
    // validation beyond the packet length, exactly as the design says.
    private const int ChatHistoryLimit = 200;
    private readonly List<ChatMessageRecord> _chatHistory = new();

    private void HandleChat(NetworkCommand cmd)
    {
        if (cmd.Payload is null) return;

        try
        {
            C2S_ChatMessagePacket incoming = default;
            PacketTranslator.Read(cmd.Payload.AsSpan(2, cmd.PayloadLength - 2), ref incoming);

            string text = incoming.GetText();
            if (text.Length == 0) return;

            ChatMessageRecord record = new((uint)_tick, cmd.PeerId, text);
            _chatHistory.Add(record);

            if (_chatHistory.Count > ChatHistoryLimit) _chatHistory.RemoveAt(0);

            S2C_ChatMessagePacket outbound = S2C_ChatMessagePacket.FromRecord(record);
            _broadcaster.SendToAllPeers(ref outbound);

            Console.WriteLine($"[Chat] tick {record.Tick} peer {record.SenderId}: {text}");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(cmd.Payload);
        }
    }



    // WORLD HOLDER => NETWORK WORLD =============================
    private void BuildSnapshot()
    {
        
        int count = 0;
        int max = S2C_WorldSnapshotPacket.MaxEntities;

        foreach(var unit in _peerIdToUnit.Values)
        {
            
            if(count >= max)
            {
                Console.WriteLine($"[World] WARN: snapshot overflow, {_peerIdToUnit.Count} units, capped at max {max}");
                break;
            }

            ref var e = ref _snapshot.Entities[count];

            e.Id = unit.EntityId;
            e.Hero = unit.Hero;
            e.Team = unit.EntityTeam;
            e.Flags = unit.IsDead ? EntityFlags.Dead : EntityFlags.None;
            e.X = unit.Position.X;
            e.Z = unit.Position.Z;
            e.Facing = 0;
            e.Health = (ushort)unit.Health;
            e.MaxHealth = (ushort)unit.MaxHealth;
            e.Mana = (ushort)unit.Mana;
            e.MaxMana = (ushort)unit.MaxMana;
            e.Speed = unit.Speed; 
            e.Radius = unit.Radius;
            e.Damage = unit.Damage;
            e.AttackTime = unit.BasicAttackTime;
            e.AttackRange = unit.AttackRange;

            count++;

        }

        _snapshot.Count = (byte)count;
        _snapshot.Tick = (uint)_tick;

    }

    public void Stop()
    {
        _isRunning = false;
        _gameThread?.Join();
    }

}