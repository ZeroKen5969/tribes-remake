using System.Linq;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Units;
using Tribes.Core.Systems.Units.Animation;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="ActionAnimation"/> with the original, plus edge cases.</summary>
public sealed class ActionAnimationTests
{
    private const int SubStateField = 13, BuildingSlotField = 46, FirstSpell = 94, LastSpell = 97, SpellPendingBit = 0x10;

    private readonly ITestOutputHelper output;

    public ActionAnimationTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Start_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            var game = sample.Restore();
            foreach (int id in UnitSampling.Units(game, 6).Concat(Casters(game)).Distinct())
            {
                int goal = UnitSampling.GoalDistance(game, id);
                foreach (bool atDoor in new[] { false, true })
                {
                    int slot = LegacySamples.Field(game, BuildingSlotField, id);
                    bool door = atDoor && slot >= 0;
                    int subState = door ? SubStates.AtTheDoor : LegacySamples.Field(game, SubStateField, id);
                    if (atDoor)
                    {
                        int spell = FirstSpell + ((id + (sample.Frame / 1000)) % (LastSpell - FirstSpell + 1));
                        int status = LegacySamples.Field(game, UnitSampling.StatusField, id) & ~SpellPendingBit;
                        run.Check(sample, $"Start unit {id} goal {goal} spell {spell}",
                            g =>
                            {
                                new UnitFields(g, id).Set(UnitSampling.ActionField, spell).Set(UnitSampling.StatusField, status);
                                UnitSampling.Turn(g, id, goal).i_I_m(goal);
                            },
                            s =>
                            {
                                new UnitFields(s.World, id).Set(UnitSampling.ActionField, spell).Set(UnitSampling.StatusField, status);
                                s.ActionAnimation.Start(s.World.Unit(id), goal);
                            });
                    }
                    run.Check(sample, $"Start unit {id} goal {goal} door {door}",
                        g => { UnitSampling.Set(g, SubStateField, id, subState); UnitSampling.Turn(g, id, goal).i_I_m(goal); },
                        s => { s.World.Unit(id).ActionSubState = subState; s.ActionAnimation.Start(s.World.Unit(id), goal); });
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 500);
    }

    [Fact]
    public void Start_AFighterInReachPicksAnAttackOfItsWeapon()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        Unit unit = UnitNeedsTests.Adult(systems.World, 30);
        (unit.Action, unit.Weapon, unit.MeleeSkill, unit.TargetIndex) = (0, 0, 3, 60);

        systems.ActionAnimation.Start(unit, 10 + 2);

        Assert.Contains(unit.AnimationNumber, systems.World.Content.Rules.Weapons.Weapons[0].AttackAnimations.Take(5));
        Assert.Equal(2, unit.Facing);
    }

    [Fact]
    public void Start_ABuilderWithAFullLoadHandsItOverAtTheSite()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        GameWorld world = systems.World;
        Building site = world.Building(3);
        (site.Kind, site.HitPoints, site.OperatorOrBeds) = (BuildingKindNumbers.FirstSite, 5, 31);
        world.Construction[0].Building = 3;
        Unit builder = world.Unit(31);
        (builder.TargetIndex, builder.BuildingSlot, builder.ActionSubState, builder.CarriedAmount, builder.CarriedItem) = (3, 0, SubStates.AtTheDoor, 9, 2);
        (builder.Action, builder.AssignedTask) = (77, 77);

        systems.ActionAnimation.Start(builder, 0);

        Assert.Equal((1, 0, 0), (world.Construction[0].Trips, builder.CarriedAmount, builder.CarriedItem));
        Assert.Equal((UnitAnimations.UnloadingMaterial, SubStates.FirstPose), (builder.AnimationNumber, builder.ActionSubState));
    }

    [Fact]
    public void Start_AUnitAtTheDoorOfAnotherGroupChangesItsTaskFirst()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        Building kitchen = Place(systems.World, 6, BuildingKindNumbers.Kitchen);
        Unit cook = Inside(systems.World, 36, 6, action: 54);

        systems.ActionAnimation.Start(cook, 0);

        Assert.Equal((UnitAnimations.ChangingTask, SubStates.FirstPose), (cook.AnimationNumber, cook.ActionSubState));
        Assert.Equal(BuildingWorkMode.Normal, kitchen.WorkMode);
    }

    [Fact]
    public void Start_AVisitorCallsTheWitchDoctorOrLeaves()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        GameWorld world = systems.World;
        Building sanctuary = Place(world, 7, BuildingKindNumbers.Sanctuary);
        Unit doctor = Inside(world, 37, 7, action: 57);
        doctor.ActionSubState = 0;
        sanctuary.OperatorOrBeds = 37;
        Unit visitor = Inside(world, 38, 7, action: 51);

        systems.ActionAnimation.Start(visitor, 0);
        Assert.Equal(55, doctor.Action);

        doctor.BuildingSlot = -1;
        visitor.ActionSubState = SubStates.AtTheDoor;
        visitor.Action = 51;
        systems.ActionAnimation.Start(visitor, 0);
        Assert.Equal(OrderModes.Forced, visitor.OrderMode);
    }

    [Fact]
    public void Start_TheTutorialExplainsTheWitchDoctorOnce()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        GameWorld world = systems.World;
        world.Script.TutorialFlags = 1;
        Place(world, 8, BuildingKindNumbers.Sanctuary);
        Unit doctor = Inside(world, 39, 8, action: ActionNumbers.WitchDoctor);
        doctor.ActionSubState = SubStates.FirstPose;

        systems.ActionAnimation.Start(doctor, 0);

        Assert.Equal(1 | 4, world.Script.TutorialFlags);
        Assert.Equal((true, 60), (world.Script.Active, world.Script.SceneId));
    }

    [Fact]
    public void Start_AnIdleUnitSometimesTurnsOrPauses()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        Unit unit = UnitNeedsTests.Adult(systems.World, 40);
        (unit.Facing, unit.BuildingSlot) = (2, -1);
        UnitSampling.NextRoll(systems.World, 0xFFFFFF, 100, roll => roll == 0);
        systems.ActionAnimation.Start(unit, 0);
        Assert.Equal(1, unit.Facing);

        UnitSampling.NextRoll(systems.World, 0xFFFFFF, 100, roll => roll == 4);
        systems.ActionAnimation.Start(unit, 0);
        Assert.Equal((ActionNumbers.Idle, 83), (unit.SavedAction, unit.Action));
    }

    private static Building Place(GameWorld world, int id, int kind)
    {
        Building building = world.Building(id);
        (building.Kind, building.HitPoints, building.Level, building.X, building.Y) = (kind, 30, 1, 4, 4);
        return building;
    }

    private static Unit Inside(GameWorld world, int id, int building, int action)
    {
        Unit unit = world.Unit(id);
        (unit.HitPoints, unit.Age, unit.TargetIndex, unit.BuildingSlot, unit.ActionSubState) = (10, 30, building, 0, SubStates.AtTheDoor);
        (unit.Action, unit.AssignedTask, unit.X, unit.Y) = (action, ActionNumbers.Idle, 2, 2);
        return unit;
    }

    /// <summary>The units casting spells in the sanctuary (they mark the spell order they work on).</summary>
    private static System.Collections.Generic.IEnumerable<int> Casters(Legacy.GameCanvas game) =>
        LegacySamples.LiveUnits(game).Where(id => LegacySamples.Field(game, UnitSampling.ActionField, id) is >= FirstSpell and <= LastSpell);
}
