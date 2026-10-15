using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.World;
using Tribes.Legacy;
using Tribes.Parity;
using Xunit;

namespace Tribes.Core.Tests;

/// <summary>
/// <see cref="EntityProperties"/> reaches the model property the original's field number names:
/// against the parity bridge's field table, and against the original's own state.
/// </summary>
public sealed class EntityPropertiesTests
{
    private const int FirstBuildingField = 83;
    private const int FirstAnimalField = 99;
    private const int FirstNodeField = 116;

    private static readonly string Root = TestPaths.RepoRoot();
    private static readonly GameContent Content = GameContent.Load(Path.Combine(Root, "data"));

    private static readonly EntityProperty[] Constants =
    {
        EntityProperty.UnitUnused, EntityProperty.UnitConstantZero, EntityProperty.UnitConstantOne,
        EntityProperty.UnitConstantMinusOne,
    };

    private static readonly EntityProperty[] PresentationOnly =
    {
        EntityProperty.UnitSelectionGroup, EntityProperty.UnitTaskIcon, EntityProperty.UnitCivilSkillIcon1,
        EntityProperty.UnitCivilSkillIcon2, EntityProperty.UnitCombatSkillIcon1, EntityProperty.UnitCombatSkillIcon2,
        EntityProperty.UnitProjectileDrawNext,
    };

    [Fact]
    public void EveryFieldNumber_IsAProperty()
    {
        EntityProperty[] all = Enum.GetValues<EntityProperty>();
        Assert.Equal(Enumerable.Range(0, 121), all.Select(p => (int)p));
        Assert.Equal(FirstBuildingField, EntityFields.Units.Count + Constants.Length + PresentationOnly.Length);
        Assert.Equal(FirstAnimalField - FirstBuildingField, EntityFields.Buildings.Count);
        Assert.Equal(FirstNodeField - FirstAnimalField, EntityFields.Animals.Count);
        Assert.Equal(EntityFields.Nodes.Count, all.Length - FirstNodeField);
        Assert.All(all, p => Assert.Equal(!PresentationOnly.Contains(p), EntityProperties.IsHeldByModel(p)));
        Assert.All(all, p => Assert.Equal(Constants.Contains(p), EntityProperties.IsConstant(p)));
    }

    [Fact]
    public void Properties_ReachTheParityBridgesFields()
    {
        GameWorld world = NewWorld();
        Check(EntityFields.Units, 0, EntityKind.Unit, world.Unit(7), 7, world);
        Check(EntityFields.Buildings, FirstBuildingField, EntityKind.Building, world.Building(33), 33, world);
        Check(EntityFields.Animals, FirstAnimalField, EntityKind.Animal, world.Animal(5), 5, world);
        Check(EntityFields.Nodes, FirstNodeField, EntityKind.ResourceNode, world.Map.ResourceNodes[9], 9, world);
    }

    [Fact]
    public void Constants_ReadTheirValueAndIgnoreWrites()
    {
        GameWorld world = NewWorld();
        int[] values = { 0, 0, 1, -1 };
        for (int i = 0; i < Constants.Length; i++)
        {
            EntityProperties.Set(world, Constants[i], 3, 99);
            EntityProperties.Add(world, Constants[i], 3, 5);
            Assert.Equal(values[i], EntityProperties.Get(world, Constants[i], 3));
            Assert.Equal(EntityKind.Unit, EntityProperties.EntityOf(Constants[i]));
        }
    }

    [Fact]
    public void PresentationOnlyProperties_CannotBeUsed()
    {
        GameWorld world = NewWorld();
        foreach (EntityProperty p in PresentationOnly)
        {
            Assert.Throws<NotSupportedException>(() => EntityProperties.Get(world, p, 1));
            Assert.Throws<NotSupportedException>(() => EntityProperties.Set(world, p, 1, 0));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => EntityProperties.EntityOf((EntityProperty)121));
        Assert.Throws<ArgumentNullException>(() => EntityProperties.Get(null!, EntityProperty.UnitX, 1));
        Assert.Throws<ArgumentNullException>(() => EntityProperties.Set(null!, EntityProperty.UnitX, 1, 0));
    }

    [Fact]
    public void Writes_AreStoredAsTheModelStoresThem()
    {
        GameWorld world = NewWorld();
        EntityProperties.Set(world, EntityProperty.UnitCarriedAmount, 4, 300);
        Assert.Equal(44, world.Unit(4).CarriedAmount);
        EntityProperties.Set(world, EntityProperty.UnitStatus, 4, -64);
        Assert.Equal(UnitStatus.Mustered | UnitStatus.OneOffHunt, world.Unit(4).Status);
        Assert.Equal(-64, EntityProperties.Get(world, EntityProperty.UnitStatus, 4));
        EntityProperties.Set(world, EntityProperty.BuildingWorkMode, 2, 2);
        Assert.Equal(BuildingWorkMode.Repairing, world.Building(2).WorkMode);
        EntityProperties.Add(world, EntityProperty.NodeAmount, 0, 20);
        EntityProperties.Add(world, EntityProperty.NodeAmount, 0, -5);
        Assert.Equal(15, world.Map.ResourceNodes[0].Amount);
    }

    [Fact]
    public void Get_ReadsWhatTheOriginalHolds()
    {
        var failures = new List<string>();
        int framesChecked = 0;
        int unitsSeen = 0;
        Tribes.Trace.Program.Run(Path.Combine(Root, "original", "game", "res"), 2400, 4L, 80, (frame, game) =>
        {
            if (frame < 1200 || frame % 600 != 0 || failures.Count > 0)
            {
                return;
            }
            GameWorld world = LegacyRules.Import(game, Content);
            foreach (EntityProperty p in Enum.GetValues<EntityProperty>().Where(EntityProperties.IsHeldByModel))
            {
                foreach (int slot in Slots(EntityProperties.EntityOf(p), world))
                {
                    int expected = game.bArr_e[game.sArr_b[(int)p] + slot];
                    int actual = EntityProperties.Get(world, p, slot);
                    if (expected != actual && failures.Count < 10)
                    {
                        failures.Add($"frame {frame}: {p} of slot {slot}: original {expected}, model {actual}");
                    }
                }
            }
            unitsSeen += Enumerable.Range(1, world.UnitSlots).Count(id => world.Unit(id).InUse);
            framesChecked++;
        });

        Assert.True(failures.Count == 0, string.Join("\n", failures));
        Assert.True(framesChecked >= 2, $"only {framesChecked} frames checked");
        Assert.True(unitsSeen > 0, "no units in play");
    }

    private static void Check<T>(IReadOnlyList<EntityField<T>> fields, int first, EntityKind kind, T entity, int slot,
        GameWorld world)
    {
        foreach (EntityField<T> field in fields)
        {
            var p = (EntityProperty)(first + field.Number);
            Assert.Equal(kind, EntityProperties.EntityOf(p));
            EntityProperties.Set(world, p, slot, 23);
            Assert.True(field.Get(entity) == 23, $"{p} does not write {field.Name}");
            field.Set(entity, 41);
            Assert.True(EntityProperties.Get(world, p, slot) == 41, $"{p} does not read {field.Name}");
            EntityProperties.Add(world, p, slot, 2);
            Assert.Equal(43, field.Get(entity));
        }
    }

    private static IEnumerable<int> Slots(EntityKind kind, GameWorld world) => kind switch
    {
        EntityKind.Unit => Enumerable.Range(1, world.UnitSlots),
        EntityKind.Building => Enumerable.Range(1, world.BuildingSlots),
        EntityKind.Animal => Enumerable.Range(0, world.AnimalSlots),
        _ => Enumerable.Range(0, world.Map.ResourceNodes.Count),
    };

    private static GameWorld NewWorld() =>
        new(Content, new WorldMap(new GridSize(10, 10), new GridSize(10, 10)), WorldLayout.Original, new GameRandom(1));
}
