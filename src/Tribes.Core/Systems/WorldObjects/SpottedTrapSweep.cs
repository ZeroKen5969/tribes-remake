using System;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.World;

namespace Tribes.Core.Systems.WorldObjects;

/// <summary>
/// The sweep that clears spotted traps from the map: after a pause, one row of the playable area
/// per frame is swept from the top down, and every spotted trap in it becomes an empty cell; then
/// the pause starts again.
/// </summary>
internal sealed class SpottedTrapSweep
{
    /// <summary>Frames between the end of one sweep and the first row of the next.</summary>
    public const int PauseFrames = 100;

    private readonly GameWorld world;

    /// <summary>Sweeps the map of <paramref name="world"/>.</summary>
    public SpottedTrapSweep(GameWorld world) => this.world = world ?? throw new ArgumentNullException(nameof(world));

    /// <summary>One frame of the sweep (the counter is <see cref="GameWorld.RowSweep"/>).</summary>
    public void Advance()
    {
        world.RowSweep++;
        if (world.RowSweep < PauseFrames)
        {
            return;
        }
        WorldMap map = world.Map;
        GridRect bounds = map.Bounds;
        int row = bounds.Top + world.RowSweep - PauseFrames;
        if (row >= bounds.Bottom - 1)
        {
            world.RowSweep = 0;
        }
        for (int column = bounds.Left; column < bounds.Right; column++)
        {
            var cell = new GridPoint(column, row);
            if (map.CellAt(cell).Content == CellContent.SpottedTrap)
            {
                map.SetCell(cell, MapCell.Empty);
            }
        }
    }
}
