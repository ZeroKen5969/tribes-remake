using System;
using System.Collections.Generic;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Vision;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Story;

/// <summary>
/// The start of each frame, before the units: the human-ruled tribes' sight is worked out every
/// few frames, a rally call musters a player's army now and then, the computer tribes' better
/// weapons arrive at their time, and one objective keeps its counter at 0 so the units can count it up.
/// </summary>
internal sealed class FrameStart
{
    /// <summary>The player's sight is worked out on frames where these bits are 0.</summary>
    private const int SightMask = 3;

    /// <summary>A rally call musters the army on frames where frame &amp; RallyMask == RallyFrame.</summary>
    private const int RallyMask = 63, RallyFrame = 2;

    /// <summary>The computer's weapon stock once its better weapons arrive.</summary>
    private const int ArrivedStock = 5000;

    /// <summary>Weapon arrival times are given in units of ArrivalUnit / (1 + ArrivalPerDifficulty * difficulty) frames.</summary>
    private const int ArrivalUnit = 1000, ArrivalPerDifficulty = 3;

    /// <summary>The story stage whose objective counter is counted up by the units each frame.</summary>
    private const int CountedStage = 2;

    /// <summary>Units whose action and task weigh more than this in a call to arms are mustered.</summary>
    private const int MusterWeight = 1;

    /// <summary>A unit with any fighting or scouting skill above this is mustered.</summary>
    private const int MusterSkill = 4;

    /// <summary>The weapon kind of a unit without a weapon.</summary>
    private const int BareHands = Entities.UnitSpawning.BareHands;

    private readonly GameWorld world;
    private readonly SightSystem sight;
    private readonly IReadOnlyList<ActionDefinition> actions;
    private readonly IReadOnlyList<ActionCategory> categories;

    /// <summary>Starts the frames of <paramref name="world"/>.</summary>
    public FrameStart(GameWorld world, SightSystem sight)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.sight = sight ?? throw new ArgumentNullException(nameof(sight));
        actions = world.Content.Rules.Actions.Actions;
        categories = world.Content.Rules.Actions.Categories;
    }

    /// <summary>The start of a frame.</summary>
    public void Run()
    {
        if ((world.Frame & SightMask) == 0)
        {
            sight.UpdateSight();
        }
        if ((world.Frame & RallyMask) == RallyFrame)
        {
            foreach (TribeState tribe in world.Tribes)
            {
                if (tribe.Controller.IsHumanRuled && TribeRoles.Army(world, tribe.Index).Rally)
                {
                    MusterArmy(tribe.Index);
                }
            }
        }
        foreach (TribeState tribe in world.Tribes)
        {
            if (tribe.Controller.RunsAi)
            {
                ArriveWeapons(tribe);
            }
        }
        if (world.Script.ObjectiveStage == CountedStage)
        {
            world.Script.ObjectiveTimer = 0;
        }
    }

    /// <summary>A call to arms of the original's player (see <see cref="MusterArmy(int)"/>).</summary>
    public void MusterArmy() => MusterArmy(TribeRoles.Player(world));

    /// <summary>
    /// A call to arms of <paramref name="tribe"/>: every slot of the tribe that is not yet in the
    /// army is called to the rally point and joins the army — everyone on an all-out order,
    /// otherwise the armed units with some fighting or scouting skill whose action and task make
    /// them available.
    /// </summary>
    public void MusterArmy(int tribe)
    {
        ArmyOrders army = TribeRoles.Army(world, tribe);
        foreach (Unit unit in world.UnitsOf(tribe))
        {
            bool available = Weight(unit.Action) + Weight(unit.AssignedTask) > MusterWeight && unit.Weapon != BareHands
                && (unit.MeleeSkill > MusterSkill || unit.RangedSkill > MusterSkill || unit.ScoutingSkill > MusterSkill);
            bool alreadyMustered = army.Rally && (unit.Status & UnitStatus.Mustered) != 0;
            if ((army.AllOut || available) && !alreadyMustered)
            {
                unit.Status |= UnitStatus.RallyPending | UnitStatus.Mustered;
            }
        }
    }

    /// <summary>The computer tribe's better weapons arrive at their time (sooner at a higher difficulty).</summary>
    private void ArriveWeapons(TribeState computer)
    {
        MissionAi ai = world.Content.Rules.Missions.Missions[world.Mission].Ai;
        int unit = ArrivalUnit / (1 + (TribeRoles.Difficulty(world, computer.Index) * ArrivalPerDifficulty));
        if (world.Frame == ai.StoneAxeUnlockTime * unit)
        {
            computer[TribeCounter.StoneAxes] = ArrivedStock;
        }
        if (world.Frame == ai.MaceAndTomahawkUnlockTime * unit)
        {
            computer[TribeCounter.BoneMaces] = ArrivedStock;
            computer[TribeCounter.Tomahawks] = ArrivedStock;
        }
    }

    private int Weight(int action) => categories[actions[action].Category].CallToArmsWeight;
}
