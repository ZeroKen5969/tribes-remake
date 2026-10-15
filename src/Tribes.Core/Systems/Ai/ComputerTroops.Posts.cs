using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Entities;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Ai;

/// <summary>Posts, weapons, training and the way home of the computer's units.</summary>
internal sealed partial class ComputerTroops
{
    /// <summary>Hard missions from this one on arm their posts with better weapons.</summary>
    private const int BetterWeaponsMission = 4;

    /// <summary>The better weapons: a club becomes a bone mace, anything else a tomahawk; spears of builder posts stay.</summary>
    private const int Club = 0, Spear = 2, BoneMace = 4, Tomahawk = 5;

    /// <summary>Units look around on frames where frame &amp; LookMask equals their slot's low bits.</summary>
    private const int LookMask = 15;

    /// <summary>The two-part mission's units stay out of their territory for its first frames.</summary>
    private const int TwoPartMission = 5, TwoPartGrace = 120;

    /// <summary>The waiting action, which keeps the action it interrupted.</summary>
    private const int WaitAction = 84;

    /// <summary>The service group of construction work, whose units are not taken off it.</summary>
    private const int ConstructionGroup = 2;

    /// <summary>Training categories (pia center, gym, range): a unit training on its cell is not interrupted.</summary>
    private const int FirstTrainingCategory = 13, LastTrainingCategory = 15;

    /// <summary>Training choices: melee, ranged, and scouting for every fourth slot in later missions.</summary>
    private const int MeleeTraining = 0, RangedTraining = 1, ScoutTraining = 2, ScoutSlotMask = 3, ScoutTrainingMission = 4;

    /// <summary>A unit farther than this from home (in rows or columns) walks back.</summary>
    private const int HomeRadius = 5;

    /// <summary>The weapon fetch with no weapon named (the stock's choice), and the first named one.</summary>
    private const int FetchAnyWeapon = 50, FetchFirstWeapon = 44;

    /// <summary>The store, where weapons are fetched.</summary>
    private const int Store = 6;

    /// <summary>
    /// A unit not in the army: it takes its post's weapon (or fetches one when it has none), goes
    /// back into the territory when it wandered off, and when idle takes up its post, trains its
    /// best fighting skill or returns home.
    /// </summary>
    private void ManagePost(Unit unit, int slot, int jobCount)
    {
        IReadOnlyList<AiPost> posts = Ai.Posts;
        int job = state.JobSlot(slot);
        bool noIntruder = state.Intruder < 0;
        if (noIntruder)
        {
            if (job < jobCount)
            {
                unit.Weapon = PostWeapon(posts[job].Weapon, slot);
            }
            else if (unit.Weapon == UnitSpawning.BareHands)
            {
                FetchWeapon(unit);
            }
        }
        if ((world.Frame & LookMask) != (unit.Id & LookMask))
        {
            return;
        }
        bool outside = !Ai.TerritoryRect.Contains(new GridPoint(unit.X, unit.Y));
        if (outside && Category(unit.Action) == 0 && unit.OrderMode == OrderModes.Free
            && (world.Mission != TwoPartMission || world.Frame > TwoPartGrace))
        {
            unit.DestinationX = Ai.Home.X;
            unit.DestinationY = Ai.Home.Y;
            unit.OrderMode = OrderModes.Walk;
        }
        if (!noIntruder || outside)
        {
            return;
        }
        if (job < jobCount)
        {
            TakePost(unit, posts[job]);
        }
        else if (IsIdle(unit) || (IsTraining(unit) && CoupleLinks.IsOnItsCell(world, unit)))
        {
            TrainOrGoHome(unit);
        }
    }

    private int PostWeapon(int weapon, int slot)
    {
        bool better = world.Mission >= BetterWeaponsMission && difficulty != 0
            && (slot >= Ai.BuilderPostCount || weapon != Spear);
        return better ? (weapon == Club ? BoneMace : Tomahawk) : weapon;
    }

    private static bool IsIdle(Unit unit) =>
        unit.Action == ActionNumbers.Idle || (unit.Action == WaitAction && unit.SavedAction == ActionNumbers.Idle);

    private bool IsTraining(Unit unit) =>
        Category(unit.Action) >= FirstTrainingCategory && Category(unit.Action) <= LastTrainingCategory;

    private int Category(int action) => world.Content.Rules.Actions.Actions[action].Category;

    private void TakePost(Unit unit, AiPost post)
    {
        int group = world.Content.Rules.Actions.Categories[Category(unit.AssignedTask)].ServiceGroup;
        if (IsIdle(unit) && group != ConstructionGroup && unit.OrderMode == OrderModes.Free
            && (unit.TaskSiteX != post.Cell.X || unit.TaskSiteY != post.Cell.Y))
        {
            unit.TaskSiteX = post.Cell.X;
            unit.TaskSiteY = post.Cell.Y;
            unit.AssignedTask = post.Action;
        }
    }

    private void TrainOrGoHome(Unit unit)
    {
        int skill = world.Content.Rules.Missions.Missions[world.Mission].Difficulties[difficulty].AttackWaveUnitSkill;
        int choice = (unit.Id & ScoutSlotMask) == 0 && world.Mission >= ScoutTrainingMission ? ScoutTraining
            : unit.MeleeSkill >= unit.RangedSkill ? MeleeTraining : RangedTraining;
        SkillTables skills = world.Content.Rules.Skills;
        TrainingTarget target = skills.TrainingTargets[choice];
        if (EntityProperties.Get(world, skills.Skills[target.Skill].Property, unit.Id) < skill && headFor(unit, target.BuildingKind))
        {
            actions.SetAction(unit, target.Action);
            unit.OrderMode = OrderModes.Forced;
            return;
        }
        ReturnHome(unit);
    }

    /// <summary>A unit in a building comes out; one far from home walks back and idles there.</summary>
    private void ReturnHome(Unit unit)
    {
        if (!CoupleLinks.IsOnItsCell(world, unit))
        {
            unit.OrderMode = OrderModes.Walk;
            return;
        }
        int dx = unit.X - Ai.Home.X;
        int dy = unit.Y - Ai.Home.Y;
        if (dx < -HomeRadius || dx > HomeRadius || dy < -HomeRadius || dy > HomeRadius)
        {
            unit.TaskSiteX = Ai.Home.X;
            unit.TaskSiteY = Ai.Home.Y;
            actions.SetAction(unit, ActionNumbers.Idle);
            unit.AssignedTask = ActionNumbers.Idle;
        }
    }

    /// <summary>
    /// A unit without a weapon that is not fighting nor changing weapons fetches one: from a
    /// building it first comes out; on a cell centre it heads for the store, or idles without one.
    /// </summary>
    private void FetchWeapon(Unit unit)
    {
        if (unit.Action <= ActionNumbers.LastBasicAttack || (unit.Action >= FetchFirstWeapon && unit.Action <= FetchAnyWeapon))
        {
            return;
        }
        if (!CoupleLinks.IsOnItsCell(world, unit))
        {
            actions.SetAction(unit, ActionNumbers.StayInside);
            unit.OrderMode = OrderModes.Forced;
            return;
        }
        if (unit.OffsetX != 0 || unit.OffsetY != 0)
        {
            return;
        }
        actions.SetAction(unit, FetchAnyWeapon);
        if (headFor(unit, Store))
        {
            orders.FinishOrder(unit, OrderModes.Walk);
            return;
        }
        actions.SetAction(unit, ActionNumbers.Idle);
    }
}
