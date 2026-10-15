using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Client.World.Scene;
using Tribes.Graphics.Composition;
using Tribes.Graphics.Imaging;
using Tribes.Graphics.Model;
using Xunit;

namespace Tribes.Client.World.Tests;

/// <summary>
/// Every villager picture takes its tribe's colour: no pose, age, sex, outfit or load leaves the
/// original's light skin tones on the map (babies and the lying and sitting poses included), and the
/// face pictures of the unit panel have a colour for their skin too.
/// </summary>
public sealed class SkinColourTests
{
    /// <summary>The original's light skin tones (the player's ramp, levels 0..2), which never survive a recolour.</summary>
    private static readonly int[] LightSkin = { 0xe9a037, 0xca8c32, 0xb37c2d };

    private const int NewbornAnimation = 71, ChildPortraitAnimation = 14;

    [Theory]
    [InlineData(Tribe.Player)]
    [InlineData(Tribe.Computer)]
    public void EveryVillagerPicture_LosesTheLightSkinTones_InEveryOtherColour(Tribe outfit)
    {
        Repo repo = Repo.Instance;
        var skin = new SkinRecolour(repo.Colours, repo.Catalog, repo.Sheet);
        var skinSheets = new HashSet<string>(skin.SkinSheets, StringComparer.Ordinal);
        var parts = Parts(repo, outfit);
        Assert.Contains(parts, p => p.Sheet == "pi0_20");
        var recoloured = new Dictionary<(string, int), RgbaImage>();
        for (int colour = 0; colour < TribeColours.Count; colour++)
        {
            if (repo.Colours.IsNative(colour, outfit))
            {
                continue;
            }
            var ramp = repo.Colours.Colours[colour].Skin.Select(s => RgbColor.Parse(s).Value).ToHashSet();
            int[] forbidden = LightSkin.Where(c => !ramp.Contains(c)).ToArray();
            foreach (DrawPart part in parts)
            {
                if (!recoloured.TryGetValue((part.Sheet!, colour), out RgbaImage? image))
                {
                    image = (skinSheets.Contains(part.Sheet!) ? skin.Recolour(part.Sheet!, colour, outfit) : null) ?? repo.Sheet(part.Sheet!);
                    recoloured[(part.Sheet!, colour)] = image;
                }
                for (int y = part.SourceY; y < part.SourceY + part.Height; y++)
                {
                    for (int x = part.SourceX; x < part.SourceX + part.Width; x++)
                    {
                        int argb = image[x, y];
                        Assert.False((argb >>> 24) != 0 && forbidden.Contains(argb & 0xFFFFFF),
                            $"{outfit} colour {colour}: sheet {part.Sheet} piece {part.Piece} keeps the skin #{argb & 0xFFFFFF:x6} at {x},{y}");
                    }
                }
            }
        }
    }

    [Fact]
    public void BabiesAndChildPortraits_ChangeWithTheColour()
    {
        Repo repo = Repo.Instance;
        var skin = new SkinRecolour(repo.Colours, repo.Catalog, repo.Sheet);
        var composers = new Composers(repo.Catalog);
        foreach (int animation in new[] { NewbornAnimation, ChildPortraitAnimation })
        {
            var pictures = new HashSet<string>(StringComparer.Ordinal);
            for (int colour = 0; colour < TribeColours.Count; colour++)
            {
                BodyComposition body = composers.Bodies.Compose(new BodyRequest(BodySubject.Villager, animation, 2, 0)
                {
                    Appearance = new VillagerAppearance { Age = 2 },
                });
                var target = new FastRaster.Target(40, 40);
                new FastRaster(id => skin.Recolour(id, colour, Tribe.Player) ?? repo.Sheet(id)).Draw(target, body.Drawing.Parts, 20, 32);
                pictures.Add(string.Join(",", target.Pixels));
            }
            Assert.Equal(TribeColours.Count, pictures.Count);
        }
    }

    [Fact]
    public void FacePictures_TakeEveryColour_AndKeepTheirOwnLookInTheirNativeColour()
    {
        Repo repo = Repo.Instance;
        TribeColours colours = repo.Colours;
        Assert.NotNull(colours.Faces);
        Assert.Equal(0, colours.FaceNative(0));
        Assert.Equal(0, colours.FaceNative(26));
        Assert.Equal(1, colours.FaceNative(27));
        Assert.Equal(1, colours.FaceNative(28));
        var composers = new Composers(repo.Catalog);
        var faceColours = new HashSet<int>();
        for (int portrait = 0; portrait <= 28; portrait++)
        {
            foreach (DrawPart part in composers.Sprites.ComposeSprite(720 + portrait).Parts.Where(p => p.Kind == DrawPartKind.Image))
            {
                RgbaImage sheet = repo.Sheet(part.Sheet!);
                for (int y = part.SourceY; y < part.SourceY + part.Height; y++)
                {
                    for (int x = part.SourceX; x < part.SourceX + part.Width; x++)
                    {
                        faceColours.Add(sheet[x, y] & 0xFFFFFF);
                    }
                }
            }
        }
        var maps = Enumerable.Range(0, TribeColours.Count).Select(colours.FaceSkinMap).ToArray();
        foreach (string listed in colours.Faces!.Colours)
        {
            int rgb = RgbColor.Parse(listed).Value;
            Assert.Contains(rgb, faceColours);
            Assert.Equal(TribeColours.Count, maps.Select(m => m[rgb]).Distinct().Count());
        }
        int blue = maps[7][RgbColor.Parse("#de833e").Value];
        Assert.True((blue & 0xFF) > blue >> 16, $"#{blue:x6} is not blue");
        Assert.Equal(maps[3], colours.FaceSkinMap(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => colours.FaceSkinMap(8));
    }

    /// <summary>Every image part of every villager pose of <paramref name="outfit"/> (children and adults, both sexes, styles, loads).</summary>
    private static List<DrawPart> Parts(Repo repo, Tribe outfit)
    {
        var composers = new Composers(repo.Catalog);
        var parts = new Dictionary<(string, int, int, int, int), DrawPart>();
        foreach (int age in new[] { 2, 30 })
        foreach (Sex sex in new[] { Sex.Male, Sex.Female })
        foreach (int bits in new[] { 0, 5, 10, 15 })
        foreach (int carried in new[] { 0, 3 })
        foreach (BodyAnimation animation in repo.Catalog.BodyAnimations)
        {
            for (int facing = 0; facing < 8; facing++)
            {
                BodyComposition body;
                try
                {
                    body = composers.Bodies.Compose(new BodyRequest(BodySubject.Villager, animation.Id, facing, 0)
                    {
                        Appearance = new VillagerAppearance { Tribe = outfit, Age = age, Sex = sex, CarriedItem = carried, CarriedAmount = carried }
                            .WithAppearanceBits(bits),
                    });
                }
                catch (ArgumentOutOfRangeException)
                {
                    continue;
                }
                foreach (DrawPart part in body.Drawing.Parts.Where(p => p.Kind == DrawPartKind.Image && p.Sheet != null))
                {
                    parts.TryAdd((part.Sheet!, part.SourceX, part.SourceY, part.Width, part.Height), part);
                }
            }
        }
        return parts.Values.ToList();
    }
}
