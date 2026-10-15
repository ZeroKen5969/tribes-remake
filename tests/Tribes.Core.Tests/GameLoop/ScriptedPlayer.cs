using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Commands;
using Tribes.Core.Simulation;
using Tribes.Core.World;

namespace Tribes.Core.Tests.GameLoop;

/// <summary>
/// A made-up player whose input on each tick depends only on the tick and the game's state: it
/// taps through scenes, now and then sends units somewhere, orders buildings, calls the army,
/// holds the game for a few ticks or toggles hints. Two games given its input stay alike.
/// </summary>
internal static class ScriptedPlayer
{
    private const int Player = 0;

    /// <summary>The input for tick <paramref name="tick"/> of <paramref name="game"/>.</summary>
    public static TickInput InputFor(GameSimulation game, long tick, int seed)
    {
        var random = new Random(unchecked((int)(tick * 7919) ^ seed));
        var orders = new List<GameOrder>();
        if (game.CanContinueScene && random.Next(4) == 0)
        {
            orders.Add(new AdvanceScene(Player));
        }
        int roll = random.Next(100);
        GameWorld world = game.World;
        var cell = new Core.Content.GridPoint(random.Next(world.Map.PlayableSize.Width), random.Next(world.Map.PlayableSize.Height));
        if (roll < 6)
        {
            int[] units = LiveUnits(world).Where(_ => random.Next(3) == 0).Take(6).ToArray();
            if (units.Length > 0)
            {
                orders.Add(new MoveUnits(Player, new UnitSet(units), cell, 0, random.Next(2) == 0));
            }
        }
        else if (roll < 8)
        {
            orders.Add(new PlaceBuilding(Player, random.Next(12), cell));
        }
        else if (roll == 8)
        {
            orders.Add(new CallArmy(Player, random.Next(2) == 0 ? ArmyCall.Rally : ArmyCall.AllOut, cell));
        }
        else if (roll == 9)
        {
            orders.Add(new CancelArmyCall(Player, random.Next(2) == 0 ? ArmyCall.Rally : ArmyCall.AllOut));
        }
        else if (roll == 10 && random.Next(10) == 0)
        {
            orders.Add(new SetHints(Player, random.Next(2) == 0));
        }
        return new TickInput
        {
            Orders = orders.Select(PlayerOrder.From).ToArray(),
            Paused = roll == 11,
            RosterOpen = roll < 3,
        };
    }

    private static IEnumerable<int> LiveUnits(GameWorld world)
    {
        return world.UnitsOf(Player).Where(unit => unit.HitPoints > 0).Select(unit => unit.Id);
    }
}
