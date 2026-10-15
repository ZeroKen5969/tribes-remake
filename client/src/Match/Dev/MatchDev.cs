using Godot;
using Tribes.Client.Ui.Dev;

namespace Tribes.Client.MatchPlay.Dev;

/// <summary>
/// Development options of the match screen (after "--" on the command line):
/// <list type="table">
/// <item><term>--match-mission=N</term><description>open the match screen of campaign mission N straight away (no menus)</description></item>
/// <item><term>--match-difficulty=N</term><description>difficulty for --match-mission (0 easy, 1 hard)</description></item>
/// <item><term>--match-skip-scenes</term><description>tap through story scenes by itself (a script can switch it: skipscenes on|off)</description></item>
/// <item><term>--match-exact-mission</term><description>start --match-mission as it is, also a chapter's later part (1, 6), which the campaign never starts alone</description></item>
/// <item><term>--match-script=PATH</term><description>play the scripted interface actions in PATH (see <see cref="MatchScript"/>)</description></item>
/// <item><term>--match-touch</term><description>lay the interface out for touch on a PC</description></item>
/// <item><term>--match-touch-dpi=N</term><description>size the touch buttons as on a screen of N dots per inch (a phone's, to see its layout on a PC)</description></item>
/// <item><term>--match-speed=P</term><description>start at P percent speed</description></item>
/// <item><term>--match-fps-log</term><description>print MATCH_FPS lines every two seconds</description></item>
/// <item><term>--site-check</term><description>draw construction sites at many places, zooms and camera offsets and check each is drawn whole</description></item>
/// </list>
/// Cheats (win or lose the mission at once) exist in debug builds only.
/// </summary>
public static class MatchDev
{
    /// <summary>Whether the interface is laid out for touch although the screen has none.</summary>
    public static bool ForceTouch => DevOptions.Has("match-touch");

    /// <summary>The screen density the touch buttons are sized for instead of the real one (0: the real one).</summary>
    public static int ForcedDpi => DevOptions.GetInt("match-touch-dpi", 0);

    /// <summary>Whether development cheats are allowed (debug builds, single player).</summary>
    public static bool CheatsAllowed => OS.IsDebugBuild();

    /// <summary>Whether story scenes are tapped through by themselves (--match-skip-scenes; scripts may switch it).</summary>
    public static bool SkipScenes
    {
        get => skipScenes ??= DevOptions.Has("match-skip-scenes");
        set => skipScenes = value;
    }

    private static bool? skipScenes;

    /// <summary>Prints a line for scripts and logcat (debug builds only; release builds stay quiet).</summary>
    public static void Log(string line)
    {
        if (OS.IsDebugBuild())
        {
            GD.Print(line);
        }
    }
}
