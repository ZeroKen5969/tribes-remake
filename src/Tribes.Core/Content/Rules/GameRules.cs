using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tribes.Core.Content.Rules;

/// <summary>
/// The rule tables of the game: everything the rules look up instead of computing (actions,
/// buildings, weapons, missions...). They live as JSON files in one directory, one file per
/// table group, written by tools/Tribes.DataExport from the original game's data. The table
/// records compare lists by reference; compare two rule sets with <see cref="ToJson"/>.
/// </summary>
public sealed class GameRules
{
    private const string MovementFile = "movement.json";
    private const string ActionsFile = "actions.json";
    private const string SkillsFile = "skills.json";
    private const string BuildingsFile = "buildings.json";
    private const string WeaponsFile = "weapons.json";
    private const string ArmouryFile = "armoury.json";
    private const string AnimalsFile = "animals.json";
    private const string AnimationsFile = "animations.json";
    private const string TargetsFile = "targets.json";
    private const string UnitsFile = "units.json";
    private const string MissionsFile = "missions-rules.json";
    private const string StoryFile = "story.json";
    private const string InterfaceFile = "interface.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    /// <summary>Creates the rules from their table groups and checks that they fit together.</summary>
    /// <exception cref="InvalidDataException">A table is missing, misnumbered or refers to something missing.</exception>
    public GameRules(MovementTables movement, ActionTables actions, SkillTables skills, BuildingTables buildings,
        WeaponTables weapons, ArmouryTables armoury, AnimalTables animals, AnimationTables animations,
        TargetTables targets, UnitTables units, MissionRuleTables missions, StoryTables story,
        InterfaceTables userInterface)
    {
        Movement = movement ?? throw Missing(MovementFile);
        Actions = actions ?? throw Missing(ActionsFile);
        Skills = skills ?? throw Missing(SkillsFile);
        Buildings = buildings ?? throw Missing(BuildingsFile);
        Weapons = weapons ?? throw Missing(WeaponsFile);
        Armoury = armoury ?? throw Missing(ArmouryFile);
        Animals = animals ?? throw Missing(AnimalsFile);
        Animations = animations ?? throw Missing(AnimationsFile);
        Targets = targets ?? throw Missing(TargetsFile);
        Units = units ?? throw Missing(UnitsFile);
        Missions = missions ?? throw Missing(MissionsFile);
        Story = story ?? throw Missing(StoryFile);
        Interface = userInterface ?? throw Missing(InterfaceFile);
        RuleValidation.Check(this);
    }

    /// <summary>Rules without any tables, for content that only needs maps (tests of map building).</summary>
    public static GameRules Empty { get; } = new(
        new MovementTables([], [], [], [], [], [], [], []),
        new ActionTables([], [], [], []),
        new SkillTables([], [], []),
        new BuildingTables([]),
        new WeaponTables([], [], []),
        new ArmouryTables([], [], [], [], [], [], []),
        new AnimalTables([]),
        new AnimationTables([], []),
        new TargetTables([], new TargetActions(0, 0, [], 0, 0, []), [], [], [], [], [], []),
        new UnitTables([], [], [], [], []),
        new MissionRuleTables([], [], []),
        new StoryTables([], [], [], [], [], [], [], [], [], [], 0),
        new InterfaceTables([], [], [], [], [], [], [], [], [], [], [], []));

    /// <summary>Directions, line steps, speed classes and distance tables (movement.json).</summary>
    public MovementTables Movement { get; }

    /// <summary>The action table and action categories (actions.json).</summary>
    public ActionTables Actions { get; }

    /// <summary>Trainable stats, skills and training places (skills.json).</summary>
    public SkillTables Skills { get; }

    /// <summary>Building kinds (buildings.json).</summary>
    public BuildingTables Buildings { get; }

    /// <summary>Weapons, projectiles and melee damage (weapons.json).</summary>
    public WeaponTables Weapons { get; }

    /// <summary>Armoury products, stock recipes, equipment choices, spells and traps (armoury.json).</summary>
    public ArmouryTables Armoury { get; }

    /// <summary>Animal kinds (animals.json).</summary>
    public AnimalTables Animals { get; }

    /// <summary>Animation timing (animations.json).</summary>
    public AnimationTables Animations { get; }

    /// <summary>Cell categories, target actions and threat search (targets.json).</summary>
    public TargetTables Targets { get; }

    /// <summary>Thoughts, aging, portraits and council scenes (units.json).</summary>
    public UnitTables Units { get; }

    /// <summary>Per-mission setup, computer player and story steps (missions-rules.json).</summary>
    public MissionRuleTables Missions { get; }

    /// <summary>Story scenes, dialogues, tutorial and hints (story.json).</summary>
    public StoryTables Story { get; }

    /// <summary>Tables only the user interface uses (interface.json).</summary>
    public InterfaceTables Interface { get; }

    /// <summary>Reads the rule files in <paramref name="directory"/>.</summary>
    /// <exception cref="InvalidDataException">
    /// A file is empty or not valid JSON for its tables (an unknown property, an enum value given by
    /// number...), or the tables do not fit together.
    /// </exception>
    /// <exception cref="FileNotFoundException">A rule file is missing.</exception>
    public static GameRules Load(string directory) => new(
        Read<MovementTables>(directory, MovementFile),
        Read<ActionTables>(directory, ActionsFile),
        Read<SkillTables>(directory, SkillsFile),
        Read<BuildingTables>(directory, BuildingsFile),
        Read<WeaponTables>(directory, WeaponsFile),
        Read<ArmouryTables>(directory, ArmouryFile),
        Read<AnimalTables>(directory, AnimalsFile),
        Read<AnimationTables>(directory, AnimationsFile),
        Read<TargetTables>(directory, TargetsFile),
        Read<UnitTables>(directory, UnitsFile),
        Read<MissionRuleTables>(directory, MissionsFile),
        Read<StoryTables>(directory, StoryFile),
        Read<InterfaceTables>(directory, InterfaceFile));

    /// <summary>Writes the rule files to <paramref name="directory"/>.</summary>
    public void Save(string directory)
    {
        Directory.CreateDirectory(directory);
        Write(directory, MovementFile, Movement);
        Write(directory, ActionsFile, Actions);
        Write(directory, SkillsFile, Skills);
        Write(directory, BuildingsFile, Buildings);
        Write(directory, WeaponsFile, Weapons);
        Write(directory, ArmouryFile, Armoury);
        Write(directory, AnimalsFile, Animals);
        Write(directory, AnimationsFile, Animations);
        Write(directory, TargetsFile, Targets);
        Write(directory, UnitsFile, Units);
        Write(directory, MissionsFile, Missions);
        Write(directory, StoryFile, Story);
        Write(directory, InterfaceFile, Interface);
    }

    /// <summary>The rule files' names, in the order they are written.</summary>
    public static IReadOnlyList<string> FileNames { get; } = Array.AsReadOnly(new[]
    {
        MovementFile, ActionsFile, SkillsFile, BuildingsFile, WeaponsFile, ArmouryFile, AnimalsFile,
        AnimationsFile, TargetsFile, UnitsFile, MissionsFile, StoryFile, InterfaceFile,
    });

    /// <summary>The rules as the JSON text of each file, by file name (for comparing rule sets).</summary>
    public IReadOnlyDictionary<string, string> ToJson() => new Dictionary<string, string>
    {
        [MovementFile] = Serialize(Movement),
        [ActionsFile] = Serialize(Actions),
        [SkillsFile] = Serialize(Skills),
        [BuildingsFile] = Serialize(Buildings),
        [WeaponsFile] = Serialize(Weapons),
        [ArmouryFile] = Serialize(Armoury),
        [AnimalsFile] = Serialize(Animals),
        [AnimationsFile] = Serialize(Animations),
        [TargetsFile] = Serialize(Targets),
        [UnitsFile] = Serialize(Units),
        [MissionsFile] = Serialize(Missions),
        [StoryFile] = Serialize(Story),
        [InterfaceFile] = Serialize(Interface),
    };

    private static InvalidDataException Missing(string file) => new($"rules: {file} is missing");

    private static T Read<T>(string directory, string file)
    {
        string path = Path.Combine(directory, file);
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json)
                ?? throw new InvalidDataException($"{path} is empty");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException($"{path}: {error.Message}", error);
        }
    }

    // Line ends are fixed so that the files are the same whatever system writes them.
    private static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, Json).ReplaceLineEndings("\n") + "\n";

    private static void Write<T>(string directory, string file, T value) =>
        File.WriteAllText(Path.Combine(directory, file), Serialize(value));
}
