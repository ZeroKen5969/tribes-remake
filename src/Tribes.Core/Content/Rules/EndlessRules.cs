using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tribes.Core.World;

namespace Tribes.Core.Content.Rules;

/// <summary>How the land of an endless world is made around the tribes.</summary>
/// <param name="ReachCells">Land is made this far (in cells) around every unit and building.</param>
/// <param name="CheckEveryFrames">How often the tribes' surroundings are checked for land not made yet.</param>
/// <param name="StartAreaCells">The default side of the land made around the start.</param>
/// <param name="LeadFrames">
/// A chunk found missing is made off the frame and joins the world this many frames later (at
/// most one chunk joins on a frame), so making land never holds a frame up.
/// </param>
public sealed record EndlessLandRules(int ReachCells, int CheckEveryFrames, int StartAreaCells, int LeadFrames);

/// <summary>Where camps go in an endless world.</summary>
/// <param name="MinimumDistance">The least distance in cells between two camps' origins.</param>
/// <param name="SiteMargin">Cells of open ground kept around a camp's buildings and units.</param>
/// <param name="CandidateStep">Camp origins are tried every this many cells.</param>
/// <param name="PlacedAtStart">
/// At most this many of the computer tribes a world starts with get their camps at the start; the
/// others wait and come one by one into land the people's tribes uncover (so any number starts at once).
/// </param>
public sealed record EndlessCampRules(int MinimumDistance, int SiteMargin, int CandidateStep, int PlacedAtStart);

/// <summary>New computer tribes in newly made land.</summary>
/// <param name="ChunksPerTribe">By the density setting (0 none .. 3 many): one new tribe for about this many chunks the people's tribes uncover (0: none).</param>
/// <param name="DistanceFromPeople">A new tribe's camp is at least this far (cells) from every unit of a human-ruled tribe.</param>
/// <param name="DistanceFromPeopleCamps">A new tribe's camp is at least this far (cells) from every human-ruled tribe's camp.</param>
public sealed record EndlessNewTribeRules(IReadOnlyList<int> ChunksPerTribe, int DistanceFromPeople, int DistanceFromPeopleCamps);

/// <summary>Wild animals of an endless world.</summary>
/// <param name="HabitatChancePercent">Habitats a new chunk holds on average, in hundredths (each whole hundred one, the rest the chance of one more); at most 400.</param>
/// <param name="AnimalsPerHabitat">Animals a habitat holds at most.</param>
/// <param name="ActiveDistance">Animals are born only in habitats this near (cells) a tribe's camp.</param>
/// <param name="FierceKind">The animal kind that hunts people (the tigers).</param>
/// <param name="FierceSharePercent">In a match or an endless world, the share (percent) of its odds the fierce kind keeps; the rest go to the other kinds.</param>
public sealed record EndlessWildlifeRules(int HabitatChancePercent, int AnimalsPerHabitat, int ActiveDistance, int FierceKind, int FierceSharePercent);

/// <summary>When computer tribes attack in an endless world.</summary>
/// <param name="OpeningGraceFrames">No computer wave goes for a human-ruled tribe before this frame of the world.</param>
/// <param name="NewcomerGraceFrames">A tribe that came later is not attacked for this many frames, and a new computer tribe sends no wave for as long.</param>
/// <param name="AttackReachCells">An enemy farther than this (cells, camp to camp) is not attacked.</param>
/// <param name="AttackersPerTarget">At most this many computer tribes send waves at the same tribe at once.</param>
/// <param name="StrengthPercent">A computer tribe attacks an enemy only while the enemy has at most this percentage of its own living adults.</param>
/// <param name="HumanReachCells">A human-ruled enemy at most this far (cells, camp to camp) is a computer's first choice, however near its other enemies are; 0 turns this off.</param>
public sealed record EndlessPacingRules(int OpeningGraceFrames, int NewcomerGraceFrames, int AttackReachCells, int AttackersPerTarget,
    int StrengthPercent, int HumanReachCells);

/// <summary>The alliance building and what allies may do.</summary>
/// <param name="SpriteId">The picture the client draws the finished building with.</param>
/// <param name="InvitationFrames">An invitation lapses after this many frames.</param>
/// <param name="GoodsCap">An ally receives goods up to this stock.</param>
/// <param name="Goods">The store counters allies may give each other.</param>
/// <param name="Building">The building kind (its number is <see cref="BuildingKind.Id"/>).</param>
public sealed record EndlessAllianceRules(string SpriteId, int InvitationFrames, int GoodsCap, IReadOnlyList<TribeCounter> Goods,
    BuildingKind Building);

/// <summary>
/// The rules of endless worlds (<c>data/endless/endless.json</c>): the custom mission whose rules
/// the world plays by, and the land, camps, new tribes, animals, pacing and alliances of the mode.
/// </summary>
public sealed record EndlessRules(int Mission, EndlessLandRules Land, EndlessCampRules Camps, EndlessNewTribeRules NewTribes,
    EndlessWildlifeRules Wildlife, EndlessPacingRules Pacing, EndlessAllianceRules Alliance)
{
    /// <summary>The file in the data directory.</summary>
    public const string FileName = "endless.json";

    /// <summary>The directory of the file in the data directory.</summary>
    public const string DirectoryName = "endless";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    /// <summary>The endless rules in data directory <paramref name="dataDirectory"/>, or null when it has none.</summary>
    /// <exception cref="InvalidDataException">The file is not valid.</exception>
    public static EndlessRules? Load(string dataDirectory)
    {
        string path = Path.Combine(dataDirectory, DirectoryName, FileName);
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            EndlessRules rules = JsonSerializer.Deserialize<EndlessRules>(File.ReadAllText(path), Json)
                ?? throw new InvalidDataException($"{path} is empty");
            return rules.Checked();
        }
        catch (JsonException error)
        {
            throw new InvalidDataException($"{path}: {error.Message}", error);
        }
    }

    /// <summary>Writes the rules into data directory <paramref name="dataDirectory"/>.</summary>
    public void Save(string dataDirectory)
    {
        string directory = Path.Combine(dataDirectory, DirectoryName);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, FileName), JsonSerializer.Serialize(this, Json).ReplaceLineEndings("\n") + "\n");
    }

    /// <summary>These rules when every number is within its range.</summary>
    /// <exception cref="InvalidDataException">A number is out of range.</exception>
    public EndlessRules Checked()
    {
        Require(Land.ReachCells > 0 && Land.CheckEveryFrames > 0 && Land.StartAreaCells > 0 && Land.LeadFrames >= 0, "land");
        Require(Camps.MinimumDistance > 0 && Camps.SiteMargin >= 0 && Camps.CandidateStep > 0 && Camps.PlacedAtStart >= 0, "camps");
        Require(NewTribes.ChunksPerTribe.Count > 0 && NewTribes.DistanceFromPeople >= 0 && NewTribes.DistanceFromPeopleCamps >= 0, "newTribes");
        Require(Wildlife.HabitatChancePercent is >= 0 and <= 400 && Wildlife.AnimalsPerHabitat > 0 && Wildlife.ActiveDistance > 0
            && Wildlife.FierceKind >= 0 && Wildlife.FierceSharePercent is >= 0 and <= 100, "wildlife");
        Require(Pacing.OpeningGraceFrames >= 0 && Pacing.NewcomerGraceFrames >= 0 && Pacing.AttackReachCells > 0
            && Pacing.AttackersPerTarget > 0 && Pacing.StrengthPercent > 0 && Pacing.HumanReachCells >= 0, "pacing");
        Require(Alliance.InvitationFrames > 0 && Alliance.GoodsCap > 0 && Alliance.Goods.Count > 0, "alliance");
        return this;
    }

    private static void Require(bool valid, string part)
    {
        if (!valid)
        {
            throw new InvalidDataException($"endless rules: a number of \"{part}\" is out of range");
        }
    }
}
