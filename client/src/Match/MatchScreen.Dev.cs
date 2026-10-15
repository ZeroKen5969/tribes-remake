using System;
using System.Linq;
using Godot;
using Tribes.Client.MatchPlay.Dev;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Client.World.Scene;
using Tribes.Core.Content;
using Tribes.Core.World;

namespace Tribes.Client.MatchPlay;

/// <summary>Development cheats for scripted checks (debug builds, single player only).</summary>
public partial class MatchScreen
{
    private const int TribeCounterCount = TribeState.CounterCount;

    /// <summary>
    /// Runs development cheat <paramref name="command"/>: win, lose (start the rules' result
    /// countdown), hint N (show hint N's dialog), chief (the tribe waits for a new chief), finish (the selected site or upgrade is
    /// done), unlock KIND LEVEL, stock COUNTER AMOUNT, damage HP (the selected building has HP
    /// left), reveal (the player has explored the whole fixed map), message X Y (a short message about
    /// cell X, Y). False when cheats are not allowed here.
    /// </summary>
    public bool DevCheat(string command, int first = 0, int second = 0)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!MatchDev.CheatsAllowed || runner.IsNetwork)
        {
            return false;
        }
        GameWorld world = World;
        switch (command)
        {
            case "win":
                Cheat(VictoryCountdownStart);
                break;
            case "lose":
                Cheat(DefeatCountdownStart);
                break;
            case "hint":
                // The hint dialog of hint number first about the first villager (checks the dialog only; the rules are not asked).
                ShowHint(new Tribes.Core.Systems.Notices.HintEvent(first, BuildingOffers.LivingUnits(world, Math.Max(0, Tribe)).FirstOrDefault(), 0) { Tribe = Math.Max(0, Tribe) });
                break;
            case "chief":
                // The campaign asks for a new chief at once; in a match the chief dies and the rules ask when it is quiet.
                if (world.Mode == WorldMode.Match && Tribe >= 0 && world.Tribes[Tribe][TribeCounter.Chief] is int chief and >= 1)
                {
                    world.Unit(chief).HitPoints = 0;
                }
                else
                {
                    world.Script.AwaitingNewChief = true;
                }
                break;
            case "message":
                // A short message about cell (first, second), as a notice with a place shows.
                hud.Messages.Add("dev", 0, (first, second));
                break;
            case "finish" when selection.Kind == SelectionKind.Building:
                FinishBuilding(world, world.Building(selection.Building));
                break;
            case "unlock" when first >= 0 && first < GameWorld.UnlockableKinds:
                world.SetUnlockLevel(first, second);
                break;
            case "stock" when first >= 0 && first < TribeCounterCount:
                world.Tribes[Math.Max(0, Tribe)].SetCounter(first, second);
                break;
            case "damage" when selection.Kind == SelectionKind.Building:
                world.Building(selection.Building).HitPoints = Math.Max(1, first);
                break;
            case "reveal" when !MapArea.IsChunked(world.Map):
                RevealMap(world);
                break;
            default:
                return false;
        }
        MatchDev.Log($"MATCH_CHEAT {command} {first} {second}");
        view.Refresh(world);
        return true;
    }

    private void RevealMap(GameWorld world)
    {
        FogOfWar fog = world.Fog(Math.Max(0, Tribe));
        GridSize size = world.Map.PlayableSize;
        for (int y = 0; y < size.Height; y++)
        {
            for (int x = 0; x < size.Width; x++)
            {
                fog.SetExplored(new GridPoint(x, y), true);
            }
        }
    }

    private static void FinishBuilding(GameWorld world, Building building)
    {
        if (RuleNumbers.IsSite(building.Kind))
        {
            var kinds = world.Content.Rules.Buildings.Kinds;
            int target = Math.Clamp(building.Aux, 0, kinds.Count - 1);
            building.HitPoints = kinds[target].MaxHitPoints[0];
            return;
        }
        foreach (UpgradeJob job in world.Upgrades)
        {
            if (job.Building == building.Id)
            {
                job.Needed = Math.Max(job.Needed, 1);
                job.Done = job.Needed;
            }
        }
    }
}
