using System;
using System.Collections.Generic;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Map;
using Tribes.Core.Systems.Paths;
using Tribes.Core.Systems.Search;
using Tribes.Core.Systems.Story;
using Tribes.Core.Systems.Units.Body;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Animation;

/// <summary>
/// The start of a unit's next action step: a unit just through a building's door takes its place
/// (or hands over a full load of material), an idle unit may turn on the spot or pause, a fighter
/// in reach picks an attack of its weapon, others an animation of their action; casters mark their
/// spell, and the tutorial explains the witch doctor once.
/// </summary>
internal sealed class ActionAnimation
{
    /// <summary>The roll of the step (0..99).</summary>
    private const int RollMask = 0xFFFFFF, RollRange = 100;

    /// <summary>An idle unit turns on the spot below this roll, and pauses below <see cref="PauseRoll"/>.</summary>
    private const int TurnRoll = 3, PauseRoll = 5;

    /// <summary>A unit waiting with nothing saved resumes below this roll.</summary>
    private const int ResumeRoll = 10;

    /// <summary>The waiting and resuming actions, and the actors' action of a story scene.</summary>
    private const int WaitAction = 84, ResumeAction = 85, StagedAction = 105;

    /// <summary>Visiting the witch doctor, and the witch doctor healing a visitor.</summary>
    private const int VisitAction = 51, HealingAction = 55;

    /// <summary>The spells cast in the sanctuary.</summary>
    private const int FirstSpell = 94, LastSpell = 97;

    /// <summary>A full load of building material.</summary>
    private const int FullLoad = 9;

    /// <summary>A builder sitting down to wait for material keeps its animation.</summary>
    private const int FirstMaterialWait = 113, LastMaterialWait = 116;

    /// <summary>A melee weapon's attacks are picked among the first (this + melee skill).</summary>
    private const int BaseMeleeChoices = 2;

    /// <summary>Tutorial flags (<see cref="ScriptState.TutorialFlags"/>): on, and the two witch doctor scenes played.</summary>
    private const int TutorialOn = 1, WitchDoctorShown = 4, UpgradedWitchDoctorShown = 8;

    /// <summary>The tutorial's witch doctor scenes, shown in the first missions only.</summary>
    private const int WitchDoctorScene = 60, UpgradedWitchDoctorScene = 62, LastTutorialMission = 6;

    /// <summary>The sanctuary level of the second witch doctor scene.</summary>
    private const int UpgradedLevel = 2;

    private readonly GameWorld world;
    private readonly UnitActions actions;
    private readonly RouteFinder routes;
    private readonly AnimationChooser chooser;
    private readonly StoryProgress story;
    private readonly Action<Unit> pause;
    private readonly Action<Unit> markSpellCast;
    private readonly IReadOnlyList<ActionCategory> categories;

    /// <summary>
    /// Creates the system for <paramref name="world"/>; <paramref name="pause"/> makes a unit pause
    /// its work for a rest, <paramref name="markSpellCast"/> marks the spell order of the building
    /// a caster runs as being cast.
    /// </summary>
    public ActionAnimation(GameWorld world, UnitActions actions, RouteFinder routes, AnimationChooser chooser,
        StoryProgress story, Action<Unit> pause, Action<Unit> markSpellCast)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
        this.routes = routes ?? throw new ArgumentNullException(nameof(routes));
        this.chooser = chooser ?? throw new ArgumentNullException(nameof(chooser));
        this.story = story ?? throw new ArgumentNullException(nameof(story));
        this.pause = pause ?? throw new ArgumentNullException(nameof(pause));
        this.markSpellCast = markSpellCast ?? throw new ArgumentNullException(nameof(markSpellCast));
        categories = world.Content.Rules.Actions.Categories;
    }

    /// <summary>Starts <paramref name="unit"/>'s next action step; <paramref name="goalDistance"/> is how far its goal is.</summary>
    public void Start(Unit unit, int goalDistance)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (TakePlace(unit, out int roll))
        {
            return;
        }
        Fidget(unit, goalDistance, roll);
        int animated;
        if ((unit.Action <= ActionNumbers.LastBasicAttack || UnitMood.IsTowerAction(unit.Action))
            && GridDistances.Cells(goalDistance) <= routes.ActionRange(unit))
        {
            animated = unit.Weapon;
            WeaponKind weapon = world.Content.Rules.Weapons.Weapons[unit.Weapon];
            int choices = weapon.RangedActionOffset == 0 ? BaseMeleeChoices + unit.MeleeSkill : 1;
            unit.Animation = weapon.AttackAnimations[world.Random.NextMasked(RollMask, choices)];
        }
        else
        {
            AnimationPick pick = chooser.Choose(unit, goalDistance, roll);
            if (pick.TurnEnds)
            {
                return;
            }
            animated = pick.Action;
        }
        if (actions.Definition(animated).Range.Constant is not 0 && animated != StagedAction)
        {
            unit.Facing = GridDistances.Facing(goalDistance);
        }
        if (animated >= FirstSpell && animated <= LastSpell && (unit.Status & UnitStatus.SpellPending) == 0)
        {
            markSpellCast(unit);
            unit.Status |= UnitStatus.SpellPending;
        }
        if (UnitThoughts.ShowsSpellIcon(unit.AnimationNumber))
        {
            unit.Thought = Thoughts.FirstSpell + (roll & 1);
        }
        if (animated == ActionNumbers.WitchDoctor)
        {
            ExplainWitchDoctor(unit);
        }
    }

    /// <summary>An idle unit turns or pauses now and then; a waiting unit with nothing to go back to resumes.</summary>
    private void Fidget(Unit unit, int goalDistance, int roll)
    {
        if (unit.Action != ActionNumbers.Idle || !unit.IsCentred)
        {
            if (unit.Action == WaitAction && unit.SavedAction == ActionNumbers.Idle && roll < ResumeRoll)
            {
                actions.SetAction(unit, ResumeAction);
            }
        }
        else if (roll < TurnRoll)
        {
            unit.Facing = (unit.Facing + (roll % TurnRoll) + Facings.Count - 1) & (Facings.Count - 1);
        }
        else if (roll < PauseRoll && goalDistance == 0 && (unit.Status & UnitStatus.Hidden) == 0)
        {
            pause(unit);
        }
    }

    /// <summary>
    /// A unit just through the door of a building takes its place: it changes task group first if
    /// it must, or hands over a full load of material; otherwise the step's roll is drawn. Returns
    /// whether that took the step.
    /// </summary>
    private bool TakePlace(Unit unit, out int roll)
    {
        roll = 0;
        if (unit.Action == ActionNumbers.WaitForMaterial && unit.Animation is >= FirstMaterialWait and <= LastMaterialWait)
        {
            return true;
        }
        if (unit.BuildingSlot >= 0 && unit.ActionSubState == SubStates.AtTheDoor
            && unit.TargetIndex >= 1 && unit.TargetIndex <= world.BuildingSlots)
        {
            Building building = world.Building(unit.TargetIndex);
            if (unit.Action == VisitAction)
            {
                CallWitchDoctor(unit, building);
            }
            if (ChangesGroup(unit))
            {
                unit.Animation = UnitAnimations.ChangingTask;
                unit.ActionSubState = building.WorkMode != BuildingWorkMode.Normal ? SubStates.InBusyBuilding
                    : building.Kind == BuildingKindNumbers.Disco ? SubStates.InDisco : SubStates.FirstPose;
                return true;
            }
            if (building.OperatorOrBeds == unit.Id && unit.CarriedAmount == FullLoad
                && (building.Kind >= BuildingKindNumbers.FirstSite || building.WorkMode != BuildingWorkMode.Normal))
            {
                HandOverMaterial(unit, building);
                return true;
            }
        }
        roll = world.Random.NextMasked(RollMask, RollRange);
        return false;
    }

    /// <summary>A visitor of the sanctuary calls the witch doctor to heal; without one at work it leaves.</summary>
    private void CallWitchDoctor(Unit unit, Building building)
    {
        if (building.OperatorOrBeds < 1 || building.OperatorOrBeds > world.UnitSlots)
        {
            return;
        }
        Unit doctor = world.Unit(building.OperatorOrBeds);
        if (doctor.ActionSubState >= 0 && doctor.BuildingSlot >= 0)
        {
            doctor.Action = HealingAction;
        }
        else
        {
            unit.OrderMode = OrderModes.Forced;
            unit.Action = ActionNumbers.LeaveBuilding;
        }
    }

    /// <summary>Whether the unit's action belongs to a workplace group other than its task's.</summary>
    private bool ChangesGroup(Unit unit)
    {
        int taskGroup = categories[actions.Definition(unit.AssignedTask).Category].ServiceGroup;
        int actionGroup = categories[actions.Definition(unit.Action).Category].ServiceGroup;
        return taskGroup != actionGroup && actionGroup > 0;
    }

    /// <summary>The operator of a site or a building in work unloads its full load: one more delivery.</summary>
    private void HandOverMaterial(Unit unit, Building building)
    {
        if (building.Kind >= BuildingKindNumbers.FirstSite)
        {
            foreach (ConstructionJob job in world.Construction)
            {
                if (job.Building == building.Id)
                {
                    job.Trips++;
                    break;
                }
            }
        }
        else
        {
            foreach (UpgradeJob job in world.Upgrades)
            {
                if (job.Building == building.Id)
                {
                    job.Needed = -job.Needed;
                    break;
                }
            }
        }
        unit.CarriedAmount = 0;
        unit.CarriedItem = 0;
        unit.ActionSubState = building.WorkMode != BuildingWorkMode.Normal ? SubStates.InBusyBuilding : SubStates.FirstPose;
        unit.Animation = UnitAnimations.UnloadingMaterial;
    }

    /// <summary>The tutorial's scene about the witch doctor (and about an upgraded sanctuary), once each.</summary>
    private void ExplainWitchDoctor(Unit unit)
    {
        ScriptState script = world.Script;
        int flags = script.TutorialFlags;
        bool first = (flags & (TutorialOn | WitchDoctorShown)) == TutorialOn;
        bool upgraded = (flags & (TutorialOn | UpgradedWitchDoctorShown)) == TutorialOn
            && unit.TargetIndex >= 1 && unit.TargetIndex <= world.BuildingSlots
            && world.Building(unit.TargetIndex).Level == UpgradedLevel;
        if (!TribeRoles.IsPlayer(world, unit.Tribe) || world.Mission > LastTutorialMission || !(first || upgraded))
        {
            return;
        }
        story.CastActors(unit.Id, 0, 1);
        story.StartScene(first ? WitchDoctorScene : UpgradedWitchDoctorScene);
        script.TutorialFlags = flags | (first ? WitchDoctorShown : UpgradedWitchDoctorShown);
    }
}
