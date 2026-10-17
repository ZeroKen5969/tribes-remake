using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace Tribes.Client.Ui.Dev;

/// <summary>
/// Development options after "--" on the command line, for automated checks of the menus:
/// <list type="table">
/// <item><term>--ui-screen=NAME</term><description>open a screen instead of the main menu (main_menu, settings, campaign, custom_game, multiplayer, lobby, endless, load_game, update, credits)</description></item>
/// <item><term>--ui-capture=PATH</term><description>save a screenshot of that screen once it has settled, then quit</description></item>
/// <item><term>--ui-capture-delay=N</term><description>frames to wait after settling (default 20)</description></item>
/// <item><term>--resize-to=WxH</term><description>resize the window while the screen is shown, then capture</description></item>
/// <item><term>--boot-hold</term><description>stay on the boot screen after loading</description></item>
/// <item><term>--ui-capture-linger=SECONDS</term><description>keep running that long after the screenshot</description></item>
/// <item><term>--ui-window=WxH</term><description>borderless window of exactly that size (portrait captures taller than the desktop)</description></item>
/// <item><term>--ui-lang=CODE</term><description>show this language without saving it</description></item>
/// <item><term>--ui-switch-lang=CODE</term><description>settings: switch to this language after a second, without saving</description></item>
/// <item><term>--ui-popup</term><description>settings: open the language list (to check the popup style)</description></item>
/// <item><term>--ui-mission=N</term><description>campaign: select the N-th chapter of the list</description></item>
/// <item><term>--ui-progress=M,M..</term><description>campaign: show the list as if these missions were won (not saved)</description></item>
/// <item><term>--ui-saves=FOLDER</term><description>load screen: list the saves of this folder instead of user://saves</description></item>
/// <item><term>--ui-tab=NAME</term><description>multiplayer: open the "lan" or "internet" tab</description></item>
/// <item><term>--lan-host</term><description>host a LAN room at start and open its lobby</description></item>
/// <item><term>--lan-join=ADDRESS</term><description>join the LAN room at ADDRESS at start</description></item>
/// <item><term>--lan-autostart</term><description>lobby host: add computer tribes (--lan-computers=N, default 1) and start the match once --lan-expect people are in</description></item>
/// <item><term>--lan-mode=endless</term><description>with --lan-host: host an endless room instead of a versus one</description></item>
/// <item><term>--session-dev-halls</term><description>endless room host: every person's tribe starts with a finished alliance hall (scripted checks)</description></item>
/// <item><term>--lan-snapshot-limit=BYTES</term><description>debug builds, LAN host: send no world larger than this (to see what players are told when it is too large)</description></item>
/// <item><term>--lan-ready</term><description>lobby (not the host): mark ready a second after getting a slot</description></item>
/// <item><term>--lan-expect=N</term><description>lobby: count as settled once N people are in the room</description></item>
/// <item><term>--lan-quit-after=SECONDS</term><description>quit that many seconds after the lobby opened</description></item>
/// <item><term>--player-name=NAME</term><description>player name for this run (not saved)</description></item>
/// <item><term>--player-id=new|GUID</term><description>a fresh or given player key for this run (several instances on one PC)</description></item>
/// <item><term>--colour=N</term><description>tribe colour to pick in the lobby</description></item>
/// <item><term>--lan-recolour=N</term><description>lobby: change to colour N two seconds after entering</description></item>
/// <item><term>--update-check</term><description>update screen: start checking at once</description></item>
/// </list>
/// </summary>
public static class DevOptions
{
    private static Dictionary<string, string>? values;

    private static Dictionary<string, string> Values
    {
        get
        {
            if (values is null)
            {
                values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string arg in OS.GetCmdlineUserArgs())
                {
                    if (!arg.StartsWith("--", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    int equals = arg.IndexOf('=', StringComparison.Ordinal);
                    string key = equals < 0 ? arg[2..] : arg[2..equals];
                    values[key] = equals < 0 ? "" : arg[(equals + 1)..];
                }
            }
            return values;
        }
    }

    /// <summary>Whether option <paramref name="name"/> was given.</summary>
    public static bool Has(string name) => Values.ContainsKey(name);

    /// <summary>The value of option <paramref name="name"/>, or null.</summary>
    public static string? Get(string name) => Values.TryGetValue(name, out string? value) ? value : null;

    /// <summary>The whole-number value of option <paramref name="name"/>, or <paramref name="fallback"/>.</summary>
    public static int GetInt(string name, int fallback) =>
        int.TryParse(Get(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;
}
