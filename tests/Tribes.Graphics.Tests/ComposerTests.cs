using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tribes.Graphics.Composition;
using Tribes.Graphics.Model;
using Tribes.Graphics.Serialization;
using Xunit;

namespace Tribes.Graphics.Tests;

/// <summary>Behaviour of the composers that the pixel comparisons do not reach.</summary>
public sealed class ComposerTests
{
    private static Composers C => TestAssets.Instance.Composers;

    private static SpriteCatalog Catalog => TestAssets.Instance.Catalog;

    [Fact]
    public void MissingReferencesStopTheCompositionAndMarkItTruncated()
    {
        Assert.True(C.Sprites.ComposeSprite(-1).Truncated);
        Assert.True(C.Sprites.ComposePiece(Catalog.Pieces.Count).Truncated);
        Assert.True(C.Sprites.ComposeEffect(Catalog.Effects.Count, 0).Truncated);
        Assert.True(C.Bodies.Compose(new BodyRequest(BodySubject.Villager, Catalog.BodyAnimations.Count, 0, 0)).Drawing.Truncated);
        Assert.True(C.Objects.Compose(new ObjectRequest(Catalog.ObjectGraphics.Count, ObjectPass.Upper)).Drawing.Truncated);
        Assert.True(C.Objects.Compose(new ObjectRequest(-1, ObjectPass.Ground)).Drawing.Truncated);
        Assert.Empty(C.Sprites.ComposePiece(0).Parts);
        Assert.False(C.Sprites.ComposePiece(0).Truncated);
        Assert.Same(DrawList.Empty, DrawList.Empty);
        Assert.Empty(DrawList.Empty.Parts);
    }

    [Fact]
    public void ThePaletteVariantIsPassedOn()
    {
        DrawList list = C.Sprites.ComposeSprite(1390, 4);
        Assert.Equal(4, list.PaletteVariantAfter);
        DrawList recoloured = C.Sprites.ComposeSprite(1390, 0);
        Assert.Contains(recoloured.Parts, p => p.Sheet == "pi9_00");
        Assert.DoesNotContain(C.Sprites.ComposeSprite(1390).Parts, p => p.Sheet != null && p.Sheet.StartsWith("pi9", StringComparison.Ordinal));
    }

    [Fact]
    public void ProjectilesPickTheirSpriteByDirectionAndFrame()
    {
        ProjectileGraphic spear = Catalog.Projectiles.Single(p => p.Weapon == 2);
        Assert.Equal(spear.FirstSprite + spear.FrameOffsets[spear.DirectionStride + 3], C.Sprites.ProjectileSprite(2, 1, 3));
        Assert.Equal(-1, C.Sprites.ProjectileSprite(0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => C.Sprites.ProjectileSprite(5, 99, 0));
        Assert.Equal(new[] { 1, 2, 5 }, Catalog.Projectiles.Select(p => p.Weapon));
    }

    [Fact]
    public void BodiesRejectFacingsOutsideTheCompass()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => C.Bodies.Compose(new BodyRequest(BodySubject.Villager, 0, 8, 0)));
        Assert.Throws<ArgumentNullException>(() => C.Bodies.Compose(null!));
        BodyComposition portrait = C.Bodies.Compose(new BodyRequest(BodySubject.Villager, 8, -8, 0) { Portrait = true });
        Assert.NotEmpty(portrait.Drawing.Parts);
    }

    [Fact]
    public void AnchorsAreTheTorsoAndHeadSlots()
    {
        BodyComposition villager = C.Bodies.Compose(new BodyRequest(BodySubject.Villager, 0, 2, 0));
        Pose pose = Catalog.Poses[Catalog.BodyAnimations[0].Frames[2 * Catalog.BodyAnimations[0].FramesPerDirection].Pose];
        BodyFrame frame = Catalog.BodyAnimations[0].Frames[2 * Catalog.BodyAnimations[0].FramesPerDirection];
        PoseSlot head = pose.Slots.First(s => s.Type == SlotType.Head);
        Assert.Equal(new AnchorPoint(frame.X + head.X, frame.Y + head.Y), villager.HeadAnchor);
        BodyComposition animal = C.Bodies.Compose(new BodyRequest(BodySubject.Animal, 2, 0, 0));
        Assert.Equal(animal.TorsoAnchor, animal.HeadAnchor);
        AnchorPoint effect = C.Bodies.EffectSlotOffset(0, 2, 0, SlotType.Head);
        Assert.Equal(new AnchorPoint(head.X, head.Y), effect);
        AnchorPoint fallback = C.Bodies.EffectSlotOffset(0, 2, 0, (SlotType)99);
        Assert.Equal(new AnchorPoint(pose.Slots[^1].X, pose.Slots[^1].Y), fallback);
    }

    [Fact]
    public void AppearanceBitsSplitIntoHeadAndTorso()
    {
        VillagerAppearance look = VillagerAppearance.Default.WithAppearanceBits(0b1110);
        Assert.Equal(2, look.HeadStyle);
        Assert.Equal(3, look.TorsoStyle);
        Assert.False(look.IsChild);
        Assert.True((look with { Age = 3 }).IsChild);
    }

    [Fact]
    public void ObjectRequestsRejectUnknownGroupsAndTiles()
    {
        var request = ObjectRequest.Building(2, 1, 4, ObjectPass.Upper) with
        {
            Occupants = new[] { new Occupant(8, 0, 0, 0, 0, VillagerAppearance.Default) },
        };
        Assert.Throws<ArgumentException>(() => C.Objects.Compose(request));
        Assert.Throws<ArgumentOutOfRangeException>(() => ObjectGraphicIds.TerrainObject(10));
        Assert.Equal(ObjectGraphicIds.Building(3), ObjectGraphicIds.Trap(3));
        Assert.Equal(27, ObjectGraphicIds.ResourceNode(1));
        Assert.Throws<ArgumentNullException>(() => C.Objects.Compose(null!));
    }

    [Fact]
    public void OccupantsAreReportedWithTheirAnchors()
    {
        var request = ObjectRequest.Building(2, 1, 4, ObjectPass.Upper) with
        {
            Occupants = new[] { new Occupant(0, 0, 3, 7, 0, VillagerAppearance.Default) },
        };
        ObjectComposition composed = C.Objects.Compose(request);
        PlacedOccupant occupant = Assert.Single(composed.Occupants);
        Assert.Equal(0, occupant.Group);
        Assert.NotNull(occupant.HeadAnchor);
        IReadOnlyList<Occupant> scene = MenuScene.Occupants(13);
        Assert.Equal(new[] { 13 % 8, 13 % 8, 13 % 10 }, scene.Select(o => o.Frame));
        Assert.Equal(3, C.Objects.Compose(MenuScene.Request(ObjectPass.Upper, 13)).Occupants.Count);
    }

    [Fact]
    public void TerrainOverlaysOnlyForTreesRocksAndTile243()
    {
        Assert.True(TerrainComposer.HasOverlay(180));
        Assert.True(TerrainComposer.HasOverlay(243));
        Assert.False(TerrainComposer.HasOverlay(179));
        Assert.Same(DrawList.Empty, C.Terrain.ComposeOverlay(10, 0, 0));
        Assert.NotEmpty(C.Terrain.ComposeOverlay(200, 5, 3).Parts);
        Assert.Throws<ArgumentNullException>(() => C.Terrain.ComposeView(null!));
        Assert.Equal(C.Sprites.ComposeSprite(12).Parts, C.Terrain.ComposeTile(12).Parts);
    }

    [Fact]
    public void DrawPartsMoveAsAWhole()
    {
        DrawPart line = DrawPart.Line(1, 2, 3, 4, new RgbColor(5), -1).Offset(10, 20);
        Assert.Equal((11, 22, 13, 24), (line.X, line.Y, line.EndX, line.EndY));
        DrawPart image = DrawPart.Image("pi0_00", 0, 0, 1, 1, 1, 2, 7).Offset(10, 20);
        Assert.Equal((11, 22, 0, 0), (image.X, image.Y, image.EndX, image.EndY));
    }

    [Fact]
    public void ValidationFindsBrokenCatalogs()
    {
        SpriteCatalog good = Catalog;
        Assert.Throws<InvalidOperationException>(() => (good with { Pieces = good.Pieces.Skip(1).ToList() }).Validate());
        Assert.Throws<InvalidOperationException>(() => (good with { ObjectGraphicFirstParts = good.ObjectGraphicFirstParts.Skip(1).ToList() }).Validate());
        var sprites = good.Sprites.ToList();
        sprites[5] = sprites[5] with { Parts = new[] { new SpritePart(99999, 0, 0) } };
        Assert.Throws<InvalidOperationException>(() => (good with { Sprites = sprites }).Validate());
        var effects = good.Effects.ToList();
        effects[0] = effects[0] with { Frames = new[] { new EffectFrame(-3, 0, 0) } };
        Assert.Throws<InvalidOperationException>(() => (good with { Effects = effects }).Validate());
        var animations = good.BodyAnimations.ToList();
        animations[0] = animations[0] with { FramesPerDirection = animations[0].FramesPerDirection + 1 };
        Assert.Throws<InvalidOperationException>(() => (good with { BodyAnimations = animations }).Validate());
        animations = good.BodyAnimations.ToList();
        animations[0] = animations[0] with { Frames = animations[0].Frames.Select(f => f with { Pose = 9999 }).ToList() };
        Assert.Throws<InvalidOperationException>(() => (good with { BodyAnimations = animations }).Validate());
    }

    [Fact]
    public void ComposersRejectCatalogsTheyCannotIndex()
    {
        SpriteCatalog good = Catalog;
        var sheets = good.Sheets.ToList();
        (sheets[0], sheets[1]) = (sheets[1], sheets[0]);
        Assert.Throws<ArgumentException>(() => new SpriteComposer(good with { Sheets = sheets }));
        var effects = good.Effects.ToList();
        effects[1] = effects[1] with { FirstFrame = effects[1].FirstFrame + 1 };
        Assert.Throws<ArgumentException>(() => new SpriteComposer(good with { Effects = effects }));
        var animations = good.BodyAnimations.ToList();
        animations[1] = animations[1] with { FirstFrame = animations[1].FirstFrame + 1 };
        Assert.Throws<ArgumentException>(() => new BodyComposer(new SpriteComposer(good with { BodyAnimations = animations })));
        var firsts = good.ObjectGraphicFirstParts.ToList();
        firsts[1]++;
        var sprites = new SpriteComposer(good with { ObjectGraphicFirstParts = firsts });
        Assert.Throws<ArgumentException>(() => new ObjectComposer(sprites, new BodyComposer(sprites)));
        Assert.Throws<ArgumentNullException>(() => new SpriteComposer(null!));
        Assert.Throws<ArgumentNullException>(() => new Composers(null!));
        Assert.Throws<ArgumentNullException>(() => new TerrainComposer(null!, C.Objects));
        Assert.Throws<ArgumentNullException>(() => new ObjectComposer(C.Sprites, null!));
        Assert.Throws<ArgumentNullException>(() => new PartRasterizerHolder(null!).Make());
    }

    private sealed class PartRasterizerHolder
    {
        private readonly Func<string, Imaging.RgbaImage> sheets;

        public PartRasterizerHolder(Func<string, Imaging.RgbaImage> sheets)
        {
            this.sheets = sheets;
        }

        public Imaging.PartRasterizer Make() => new(sheets);
    }

    [Fact]
    public void JsonHelpersReadWriteAndRejectEmptyDocuments()
    {
        Assert.Throws<InvalidDataException>(() => CatalogJson.Deserialize<SpriteCatalog>("null"));
        string directory = Path.Combine(Path.GetTempPath(), "tribes-graphics-tests", Guid.NewGuid().ToString("N"));
        string file = Path.Combine(directory, "nested", "colour.json");
        CatalogJson.Write(file, new UiColor("x", new RgbColor(0xABCDEF), "y"));
        Assert.Equal(new RgbColor(0xABCDEF), CatalogJson.Read<UiColor>(file).Color);
        Assert.Contains("\"#abcdef\"", File.ReadAllText(file), StringComparison.Ordinal);
        Assert.Throws<System.Text.Json.JsonException>(() => CatalogJson.Deserialize<UiColor>("{\"color\": 5}"));
        Directory.Delete(directory, recursive: true);
    }
}
