using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Ai;
using Tribes.Legacy;

namespace Tribes.Core.Tests.Systems;

/// <summary>Sampled states and frames for the computer player's parity tests.</summary>
internal static class AiSamples
{
    /// <summary>The computer's tribe.</summary>
    public const int Computer = 1;

    /// <summary>Played states of the missions with a computer player (missions 1 and later), and of the monkey games.</summary>
    public static IEnumerable<LegacySample> States(int everyNth) =>
        MissionSamples.All.Where((_, i) => i % everyNth == 0).Concat(LegacySamples.All.Where((_, i) => i % (everyNth * 4) == 0));

    /// <summary>The computer player's settings of the sample's mission.</summary>
    public static MissionAi Ai(LegacySample sample) =>
        LegacySamples.Content.Rules.Missions.Missions[ScenarioSamples.MissionOf(sample)].Ai;

    /// <summary>The sample's ramp-up time.</summary>
    public static int RampUp(LegacySample sample) => Ai(sample).RampUpUnits * ComputerPlayer.RampUpUnit;

    /// <summary>
    /// Frames near the sample's own that hit the computer's periodic checks: repairs, intruders,
    /// waves, armoury orders, spells and the next attack.
    /// </summary>
    public static int[] Frames(LegacySample sample)
    {
        GameCanvas game = sample.Restore();
        int frame = game.i_c;
        MissionAi ai = Ai(sample);
        int armoury = ai.ArmouryOrderInterval * 50, spells = ai.SpellInterval * 50;
        return new[]
        {
            frame, frame & ~31, (frame & ~63) + 25, frame & ~15, ((frame / armoury) * armoury) + 50,
            ((frame / spells) * spells) + 50, game.world_a.i_aQ, ((frame / (ai.BuildPlanCheckInterval * 10)) + 1) * ai.BuildPlanCheckInterval * 10,
        }.Where(f => f > 0).Distinct().ToArray();
    }

    /// <summary>Sets the registers the original's computer step sets before its parts run.</summary>
    public static void Registers(GameCanvas game, LegacySample sample)
    {
        game.world_a.i_u = Computer;
        game.world_a.b_M = (sbyte)Ai(sample).Posts.Count;
        game.world_a.i_A = RampUp(sample);
    }
}
