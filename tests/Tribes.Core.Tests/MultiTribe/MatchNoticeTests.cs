using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Match;
using Tribes.Core.Systems.Notices;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.MultiTribe;

/// <summary>
/// Notices and hints go to the player of the tribe they are about: every human tribe of a match
/// gets its own, none of them is about another tribe's units, and computer tribes get none.
/// </summary>
public sealed class MatchNoticeTests
{
    private const int Ticks = 8000;

    /// <summary>Notice kinds whose value is the unit they are about (born, died).</summary>
    private static readonly int[] UnitNotices = [1, 3];

    private readonly ITestOutputHelper output;

    public MatchNoticeTests(ITestOutputHelper output) => this.output = output;

    [Theory]
    [InlineData(0, 4, 0)]
    [InlineData(2, 3, 1)]
    public void EveryHumanTribe_GetsItsOwnNoticesAndHints(int map, int humans, int computers)
    {
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content,
            MatchWorlds.Setup(map, 5 + map, MatchWorlds.Controllers(humans, computers)) with { EndsWithLastSide = false });
        GameWorld world = game.World;
        var notices = new List<NoticeEvent>();
        var hints = new List<HintEvent>();
        for (long tick = 0; tick < Ticks; tick++)
        {
            foreach (GameEvent e in game.Step(MatchWorlds.Orders(game, tick, map)).Events)
            {
                if (e is NoticeEvent notice)
                {
                    notices.Add(notice);
                    if (UnitNotices.Contains(notice.Kind) && notice.Value > 0)
                    {
                        Assert.Equal(notice.Tribe, world.TribeOfUnit(notice.Value));
                    }
                }
                else if (e is HintEvent hint)
                {
                    hints.Add(hint);
                    if (hint.Subject > 0)
                    {
                        Assert.Equal(hint.Tribe, world.TribeOfUnit(hint.Subject));
                    }
                }
            }
        }

        for (int tribe = 0; tribe < world.Tribes.Count; tribe++)
        {
            int[] kinds = notices.Where(n => n.Tribe == tribe).Select(n => n.Kind).Distinct().Order().ToArray();
            output.WriteLine($"tribe {tribe}: {notices.Count(n => n.Tribe == tribe)} notices (kinds {string.Join(",", kinds)}), "
                + $"{hints.Count(h => h.Tribe == tribe)} hints");
        }
        for (int tribe = 0; tribe < humans; tribe++)
        {
            Assert.Contains(notices, n => n.Tribe == tribe);
            Assert.Contains(hints, h => h.Tribe == tribe);
        }
        Assert.DoesNotContain(notices, n => n.Tribe >= humans);
        Assert.DoesNotContain(hints, h => h.Tribe >= humans);
    }
}
