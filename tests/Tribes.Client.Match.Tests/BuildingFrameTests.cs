using System;
using System.Linq;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Endless;
using Tribes.Graphics.Composition;
using Xunit;

namespace Tribes.Client.MatchPlay.Tests;

/// <summary>The corner marks of the selected building frame its whole footprint, for every kind.</summary>
public sealed class BuildingFrameTests
{
    private const int CellWidth = 22, CellHeight = 16;
    private const int TopLeftMark = 6, TopRightMark = 7, BottomLeftMark = 8, BottomRightMark = 9, MarkFrames = 4;

    [Fact]
    public void Corners_AreTheFootprintsOuterCorners()
    {
        var (tl, tr, bl, br) = BuildingFrame.Corners(3, -2, 2, 3, CellWidth, CellHeight);
        Assert.Equal(new MapPixel(66, -32), tl);
        Assert.Equal(new MapPixel(110, -32), tr);
        Assert.Equal(new MapPixel(66, 16), bl);
        Assert.Equal(new MapPixel(110, 16), br);
    }

    [Fact]
    public void TheFrame_MarksTheSelectedBuilding_OrAnotherTribesBuildingLookedAt()
    {
        var selection = new UnitSelection();
        Assert.Equal(0, BuildingFrame.MarkedBuilding(selection));
        selection.SelectBuilding(7);
        Assert.Equal(7, BuildingFrame.MarkedBuilding(selection));
        selection.ShowOther(Tribes.Core.Maps.CellCode.OfBuilding(45));
        Assert.Equal(45, BuildingFrame.MarkedBuilding(selection));
        selection.ShowOther(Tribes.Core.Maps.CellCode.OfUnit(3));
        Assert.Equal(0, BuildingFrame.MarkedBuilding(selection));
        selection.SetUnits(new[] { 2 });
        Assert.Equal(0, BuildingFrame.MarkedBuilding(selection));
    }

    [Fact]
    public void TheMarks_FrameEveryBuildingKind_AtEveryFrameOfTheirAnimation()
    {
        Repo repo = Repo.Instance;
        GameContent content = EndlessContent.For(repo.Content);
        var kinds = content.Rules.Buildings.Kinds;
        Assert.Contains(kinds, k => k.Width == 3 && k.Height == 3);
        for (int kind = 0; kind < kinds.Count; kind++)
        {
            BuildingKind definition = kinds[kind];
            if (definition.Width <= 0 || definition.Height <= 0)
            {
                continue;
            }
            const int X = 10, Y = 7;
            var (tl, tr, bl, br) = BuildingFrame.Corners(X, Y, definition.Width, definition.Height, CellWidth, CellHeight);
            int left = X * CellWidth, top = Y * CellHeight;
            int right = left + (definition.Width * CellWidth), bottom = top + (definition.Height * CellHeight);
            for (int tick = 0; tick < MarkFrames; tick++)
            {
                var marks = new[]
                {
                    Box(repo, TopLeftMark, tick, tl), Box(repo, TopRightMark, tick, tr),
                    Box(repo, BottomLeftMark, tick, bl), Box(repo, BottomRightMark, tick, br),
                };
                string what = $"kind {kind} ({definition.Width}x{definition.Height}) frame {tick}";
                // Each mark straddles its own corner of the footprint, so together they enclose it.
                Assert.True(marks[0].Left <= left && marks[0].Top <= top && marks[0].Right > left && marks[0].Bottom > top, what + " top-left");
                Assert.True(marks[1].Right >= right && marks[1].Top <= top && marks[1].Left < right && marks[1].Bottom > top, what + " top-right");
                Assert.True(marks[2].Left <= left && marks[2].Bottom >= bottom && marks[2].Right > left && marks[2].Top < bottom, what + " bottom-left");
                Assert.True(marks[3].Right >= right && marks[3].Bottom >= bottom && marks[3].Left < right && marks[3].Top < bottom, what + " bottom-right");
            }
        }
    }

    /// <summary>The pixels mark <paramref name="effect"/> covers at frame <paramref name="tick"/> drawn at <paramref name="at"/>.</summary>
    private static (int Left, int Top, int Right, int Bottom) Box(Repo repo, int effect, int tick, MapPixel at)
    {
        DrawPart[] parts = repo.Assets.Composers.Sprites.ComposeEffect(effect, tick).Parts.Where(p => p.Kind == DrawPartKind.Image).ToArray();
        Assert.NotEmpty(parts);
        return (at.X + parts.Min(p => p.X), at.Y + parts.Min(p => p.Y), at.X + parts.Max(p => p.X + p.Width), at.Y + parts.Max(p => p.Y + p.Height));
    }
}
