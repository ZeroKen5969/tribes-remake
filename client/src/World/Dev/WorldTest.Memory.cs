using System.Collections.Generic;
using Godot;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.World;

namespace Tribes.Client.World.Dev;

/// <summary>
/// <c>--memory-demo</c> in a match of the dev scene's own (three tribes or more): tribe 0 and 1 are
/// allied for one sight update and part again, then tribe 1's buildings are razed out of tribe 0's
/// sight, and the land around tribe 2's camp becomes explored (never seen) for tribe 0. Tribe 0's
/// view then shows tribe 1's camp as it last saw it and no building at tribe 2's camp.
/// </summary>
public partial class WorldTest
{
    private const int DemoExploredRadius = 10;

    private void MemoryDemo(GameWorld world, Dictionary<string, string> args)
    {
        if (!args.ContainsKey("memory-demo") || world.Tribes.Count < 3)
        {
            return;
        }
        world.Relations.SetAlliance(0, 1, true);
        StepFrames(SightFrames);
        world.Relations.SetAlliance(0, 1, false);
        StepFrames(SightFrames);
        var kinds = world.Content.Rules.Buildings.Kinds;
        foreach (Building building in world.BuildingsOf(1))
        {
            if (!building.InUse)
            {
                continue;
            }
            for (int y = building.Y; y < building.Y + kinds[building.Kind].Height; y++)
            {
                for (int x = building.X; x < building.X + kinds[building.Kind].Width; x++)
                {
                    world.Map.SetCell(new GridPoint(x, y), MapCell.Empty);
                }
            }
            building.HitPoints = 0;
        }
        GridPoint camp = world.Tribes[2].Camp;
        for (int y = camp.Y - DemoExploredRadius; y <= camp.Y + DemoExploredRadius; y++)
        {
            for (int x = camp.X - DemoExploredRadius; x <= camp.X + DemoExploredRadius; x++)
            {
                world.Fog(0).SetExplored(new GridPoint(x, y), true);
            }
        }
        GD.Print($"WORLD_TEST memory demo: tribe 0 remembers {world.Fog(0).Buildings.Count} buildings");
    }

    private void StepFrames(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            simulation!.Step();
        }
    }
}
