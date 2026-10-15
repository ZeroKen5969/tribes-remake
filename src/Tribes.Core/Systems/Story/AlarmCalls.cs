using System;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Entities;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Story;

/// <summary>
/// The tribes' alarm calls: each one lasts a while, and as long as it lasts the adults standing on
/// its cell or next to it are marked (<see cref="UnitStatus.Diseased"/>).
/// </summary>
internal sealed class AlarmCalls
{
    /// <summary>An alarm ages one step every this many frames (a mask of the frame number).</summary>
    private const int AgingMask = 7;

    /// <summary>The age at which an alarm call is over.</summary>
    private const int Lifetime = 100;

    /// <summary>The age of a free alarm slot.</summary>
    private const int Free = -1;

    private readonly GameWorld world;

    /// <summary>Keeps the alarm calls of <paramref name="world"/>.</summary>
    public AlarmCalls(GameWorld world)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
    }

    /// <summary>
    /// One frame of every tribe's alarm calls, from the first up to the first free slot: they age,
    /// expire, and mark the adults around them.
    /// </summary>
    public void Update()
    {
        foreach (TribeState tribe in world.Tribes)
        {
            for (int slot = 0; slot < TribeState.AlarmSlots && tribe.Alarms[slot].Age >= 0; slot++)
            {
                AlarmCall alarm = tribe.Alarms[slot];
                if ((world.Frame & AgingMask) == 0)
                {
                    alarm.Age++;
                }
                if (alarm.Age >= Lifetime)
                {
                    alarm.Age = Free;
                }
                else
                {
                    MarkAround(new GridPoint(alarm.X, alarm.Y));
                }
            }
        }
    }

    private void MarkAround(GridPoint centre)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                var cell = new GridPoint(centre.X + dx, centre.Y + dy);
                if (!world.Map.InPlay(cell))
                {
                    continue;
                }
                MapCell content = world.Map.CellAt(cell);
                if (content.Content == CellContent.Unit && world.Unit(content.Index).Age >= UnitAges.Adult)
                {
                    world.Unit(content.Index).Status |= UnitStatus.Diseased;
                }
            }
        }
    }
}
