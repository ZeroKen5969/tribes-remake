using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;

namespace Tribes.Core.Systems.Endless;

/// <summary>
/// The content an endless world plays with: the game's content with the endless mode's building
/// kinds added after the original ones (<see cref="AllianceBuilding.Kind"/>). Other worlds keep the
/// game's content as it is.
/// </summary>
public static class EndlessContent
{
    private static readonly ConditionalWeakTable<GameContent, GameContent> Extended = new();

    /// <summary>Whether <paramref name="content"/> is an endless world's content already.</summary>
    public static bool IsEndless(GameContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return content.Rules.Buildings.Kinds.Count > AllianceBuilding.Kind
            && content.Rules.Buildings.Kinds[AllianceBuilding.Kind].Id == AllianceBuilding.Kind;
    }

    /// <summary>The endless content of <paramref name="content"/> (made once per content; itself when it is one).</summary>
    /// <exception cref="InvalidDataException">The content has no endless rules, or they do not fit it.</exception>
    public static GameContent For(GameContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return IsEndless(content) ? content : Extended.GetValue(content, Extend);
    }

    private static GameContent Extend(GameContent content)
    {
        EndlessRules endless = content.Endless ?? throw new InvalidDataException("the content has no endless rules (data/endless/endless.json)");
        GameRules r = content.Rules;
        BuildingKind hall = endless.Alliance.Building;
        if (r.Buildings.Kinds.Count != AllianceBuilding.Kind || hall.Id != AllianceBuilding.Kind)
        {
            throw new InvalidDataException($"endless rules: the alliance building must be kind {AllianceBuilding.Kind}, after the {r.Buildings.Kinds.Count} kinds of the rules");
        }
        var buildings = r.Buildings with { Kinds = r.Buildings.Kinds.Append(hall).ToArray() };
        var rules = new GameRules(r.Movement, r.Actions, r.Skills, buildings, r.Weapons, r.Armoury, r.Animals, r.Animations,
            r.Targets, r.Units, r.Missions, r.Story, r.Interface);
        return new GameContent(content.Missions.Values, content.Maps.Values, content.Terrain, content.ResourceNodeKinds.Values, rules)
        {
            Endless = endless,
        };
    }
}
