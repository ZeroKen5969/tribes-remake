using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Graphics.Tests;

/// <summary>
/// Every case of the reference file, drawn by the original game's own drawing code, composed
/// with Tribes.Graphics and rasterised: the pixels must be identical.
/// </summary>
public sealed class OriginalPixelTests
{
    private const int ShownMismatches = 15;
    private readonly ITestOutputHelper output;

    public OriginalPixelTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Theory]
    [InlineData("P", "pieces (id, palette variant)")]
    [InlineData("S", "composite sprites (id, palette variant)")]
    [InlineData("E", "effect animations (id, tick)")]
    [InlineData("V", "villagers (animation, facing, frame, appearance)")]
    [InlineData("Q", "villagers with the map screen's selection marks")]
    [InlineData("A", "animals (animation, facing, frame)")]
    [InlineData("B", "buildings (kind, level, pass, cell, clock, occupants, stock)")]
    [InlineData("N", "resource nodes (kind, stage, pass)")]
    [InlineData("T", "terrain objects (tile, pass, clock)")]
    [InlineData("R", "traps (code, pass, caught unit)")]
    [InlineData("W", "placement previews")]
    [InlineData("M", "title scenery")]
    public void MatchesTheOriginal(string kind, string what)
    {
        TestAssets assets = TestAssets.Instance;
        List<ReferenceCase> cases = ReferenceCases.Cases.Where(c => c.Kind == kind).ToList();
        Assert.NotEmpty(cases);
        var mismatches = new ConcurrentBag<string>();
        Parallel.ForEach(cases, rc =>
        {
            string actual = ReferenceCases.Render(assets, ReferenceCases.Compose(assets.Composers, rc));
            if (actual != rc.Expected)
            {
                mismatches.Add(rc.Line + "  =>  " + actual);
            }
        });
        output.WriteLine($"{what}: {cases.Count - mismatches.Count}/{cases.Count} identical");
        if (!mismatches.IsEmpty)
        {
            var text = new StringBuilder($"{mismatches.Count} of {cases.Count} {what} differ, e.g.:\n");
            foreach (string m in mismatches.OrderBy(m => m, System.StringComparer.Ordinal).Take(ShownMismatches))
            {
                text.Append(m).Append('\n');
            }
            Assert.Fail(text.ToString());
        }
    }

    [Fact]
    public void EveryAnimationIdIsCovered()
    {
        var catalog = TestAssets.Instance.Catalog;
        IReadOnlyList<ReferenceCase> cases = ReferenceCases.Cases;
        var sprites = cases.Where(c => c.Kind == "S").Select(c => c.Values[0]).ToHashSet();
        var pieces = cases.Where(c => c.Kind == "P").Select(c => c.Values[0]).ToHashSet();
        var effects = cases.Where(c => c.Kind == "E").Select(c => c.Values[0]).ToHashSet();
        var villagers = cases.Where(c => c.Kind == "V").Select(c => c.Values[0]).ToHashSet();
        var animals = cases.Where(c => c.Kind == "A").Select(c => c.Values[0]).ToHashSet();
        var buildings = cases.Where(c => c.Kind == "B").Select(c => c.Values[0]).ToHashSet();
        var tiles = cases.Where(c => c.Kind == "T").Select(c => c.Values[0]).ToHashSet();
        var effectFrames = cases.Where(c => c.Kind == "E" && c.Values[1] >= 0
            && c.Values[1] < catalog.Effects[c.Values[0]].Frames.Count).Count();
        output.WriteLine($"pieces {pieces.Count}/{catalog.Pieces.Count}, sprites {sprites.Count}/{catalog.Sprites.Count}, "
            + $"effects {effects.Count}/{catalog.Effects.Count} ({effectFrames} frames), "
            + $"body animations as villagers {villagers.Count}/{catalog.BodyAnimations.Count}, as animals {animals.Count}/{catalog.BodyAnimations.Count}, "
            + $"building kinds {buildings.Count}/26, terrain object tiles {tiles.Count}/81");
        Assert.Equal(catalog.Pieces.Count, pieces.Count);
        Assert.Equal(catalog.Sprites.Count, sprites.Count);
        Assert.Equal(catalog.Effects.Count, effects.Count);
        Assert.Equal(catalog.Effects.Sum(e => e.Frames.Count), effectFrames);
        Assert.Equal(catalog.BodyAnimations.Count, villagers.Count);
        Assert.Equal(catalog.BodyAnimations.Count, animals.Count);
        Assert.Equal(26, buildings.Count);
        Assert.Equal(81, tiles.Count);
    }
}
