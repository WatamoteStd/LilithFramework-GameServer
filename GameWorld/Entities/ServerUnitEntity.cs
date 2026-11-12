using System.Numerics;
using Shared.GameData.Entities;
using Shared.GameData.Units;
using Shared.GameData.Units.Property;

namespace GameWorld.Entities;

public class ServerUnitEntity : UnitEntity
{
    public ServerUnitEntity(ulong playerIdentif, ushort entityId, Vector3 spawnPos, MatchTeam team, HeroId hero = HeroId.None, byte rotation = 0) 
        : base (playerIdentif, entityId, spawnPos, team, hero, rotation)
    {
    }

    public override void Update(float deltaTime)
    {
        if (CurrentState == States.Move)
        {
            Move(deltaTime);
        }
    }

    public override void SetMoveTarget(float x, float z)
    {
        _moveTarget = new Vector3(x, Position.Y, z);
        CurrentState = States.Move;
    }

    private void Move(float delta)
    {
        Vector3 toTarget = _moveTarget - Position;
        float distance = toTarget.Length();

        // Если дошли до цели (или почти)
        if (distance < 0.1f)
        {
            Position = _moveTarget;
            CurrentState = States.Idle;
            return;
        }

        // Идем к цели
        Vector3 direction = toTarget / distance;
        float step = Speed * delta;

        if (step >= distance)
        {
            Position = _moveTarget;
            CurrentState = States.Idle;
        }
        else
        {
            Position += direction * step;
        }
    }
}