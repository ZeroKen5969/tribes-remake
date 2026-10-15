using System;
using System.Linq;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Notices;
using Tribes.Core.World;
using Tribes.Legacy;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="HintSystem"/> (cool-downs, hint conditions, shown hints, notices) with the original, plus edge cases.</summary>
public sealed class HintSystemTests
{
    private const int Hints = 21;
    private const int ShownScreen = 20;

    private readonly ITestOutputHelper output;

    public HintSystemTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void UpdateCooldowns_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All)
        {
            foreach (bool enabled in new[] { true, false })
            {
                short[] cooldowns = Enumerable.Range(0, HintState.Channels).Select(i => (short)((sample.Frame / 1000 * (i + 3) * 37 % 2100) - 1)).ToArray();
                run.Check(sample, $"UpdateCooldowns enabled {enabled}",
                    g =>
                    {
                        g.i_l = enabled ? 1 : 0;
                        Array.Copy(cooldowns, g.world_a.sArr_d, HintState.Channels);
                        g.world_a.ax_();
                    },
                    s =>
                    {
                        s.World.Hints.Enabled = enabled;
                        for (int i = 0; i < HintState.Channels; i++)
                        {
                            s.World.Hints.SetCooldown(i, cooldowns[i]);
                        }
                        s.Hints.UpdateCooldowns();
                    });
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 60);
    }

    [Fact]
    public void Offer_MatchesTheOriginal()
    {
        var run = new ParityRun();
        int shown = 0;
        foreach (LegacySample sample in LegacySamples.All)
        {
            int subject = LegacySamples.LiveUnits(sample.Restore()).FirstOrDefault();
            foreach (bool freeChannels in new[] { false, true })
            {
                run.Check(sample, $"Offer free {freeChannels}",
                    g => string.Concat(Enumerable.Range(0, Hints).Select(hint =>
                    {
                        if (freeChannels)
                        {
                            Array.Fill(g.world_a.sArr_d, (short)-1);
                        }
                        g.b_ap = -1;
                        g.world_a.a_BII((sbyte)hint, subject + (hint % 3 == 2 ? 50 : 0), hint + 3);
                        return g.b_ap == hint ? 1 : 0;
                    })),
                    s => string.Concat(Enumerable.Range(0, Hints).Select(hint =>
                    {
                        if (freeChannels)
                        {
                            for (int i = 0; i < HintState.Channels; i++)
                            {
                                s.World.Hints.SetCooldown(i, HintState.Free);
                            }
                        }
                        int before = s.PendingEvents.Count;
                        s.Hints.Offer(hint, subject + (hint % 3 == 2 ? 50 : 0), hint + 3);
                        shown += s.PendingEvents.Count - before;
                        return s.PendingEvents.Count > before ? 1 : 0;
                    })));
            }
        }
        output.WriteLine(run.ToString() + $", {shown} shown");
        run.AssertAgrees(minimum: 60);
        Assert.True(shown > 0, "no hint was shown");
    }

    [Fact]
    public void Show_MatchesTheOriginal()
    {
        var run = new ParityRun();
        int shown = 0;
        foreach (LegacySample sample in LegacySamples.All)
        {
            run.Check(sample, "Show",
                g => string.Concat(Enumerable.Range(0, Hints).Select(hint =>
                {
                    g.b_ap = -1;
                    g.a_BBB((sbyte)hint, (sbyte)(hint + 1), (sbyte)(hint * 5));
                    return g.b_ap == hint ? 1 : 0;
                })),
                s => string.Concat(Enumerable.Range(0, Hints).Select(hint => Count(s.Hints.Show(hint, hint + 1, hint * 5)))));
        }
        output.WriteLine(run.ToString() + $", {shown} shown");
        run.AssertAgrees(minimum: 30);
        Assert.True(shown > LegacySamples.All.Count * 3, "too few hints shown");

        int Count(bool wasShown)
        {
            shown += wasShown ? 1 : 0;
            return wasShown ? 1 : 0;
        }
    }

    [Fact]
    public void Notice_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All)
        {
            run.Check(sample, "Notice",
                g => string.Concat(new[] { 6, 9, 100, 12 }.Select(kind =>
                {
                    Array.Clear(g.bArr_u);
                    g.a_BBBB((sbyte)kind, 3, 4, 5);
                    return g.bArr_u[0] == kind ? 1 : 0;
                })),
                s => string.Concat(new[] { 6, 9, 100, 12 }.Select(kind =>
                {
                    int before = s.PendingEvents.Count;
                    s.Hints.Notice(kind, 3, 4, 5);
                    return s.PendingEvents.Count > before ? 1 : 0;
                })));
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 30);
    }

    [Fact]
    public void Offer_ShowsAnOutOfSightHintWithoutQuiet()
    {
        GameWorld world = TestWorlds.Empty();
        var systems = new RuleSystems(world);
        world.FramesSinceCombat = 0;

        systems.Hints.Offer(1, 3, 3);
        systems.Hints.Offer(4, 3, 9);
        systems.Hints.Offer(5, 3, 9);

        HintEvent shown = Assert.IsType<HintEvent>(Assert.Single(systems.TakeEvents()));
        Assert.Equal(new HintEvent(4, 3, 9), shown);
        Assert.True(HintEvent.PausesGame);
        Assert.Equal(0, world.Hints.Cooldown(world.Content.Rules.Story.Hints[4].Channel));
    }

    [Fact]
    public void Show_CompulsoryHintsIgnoreTheOption()
    {
        GameWorld world = TestWorlds.Empty();
        var systems = new RuleSystems(world);
        world.Hints.Enabled = false;

        Assert.False(systems.Hints.Show(3, 1, 1));
        Assert.True(systems.Hints.Show(18, 2, 2));
        Assert.Equal(2, world.Script.FocusUnit);
        world.Hints.Enabled = true;
        world.Script.AwaitingNewChief = true;
        Assert.False(systems.Hints.Show(3, 1, 1));
        world.Frame = 0;
        systems.Hints.Notice(6, 1, 1, 0);
        Assert.Single(systems.TakeEvents());
    }
}
