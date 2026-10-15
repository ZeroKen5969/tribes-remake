using Tribes.Client.Ui.Dev;
using Tribes.Net;

namespace Tribes.Client.Ui.Net;

/// <summary>Development start of a LAN room: "--lan-host" hosts one, "--lan-join=ADDRESS" joins one; both open the lobby.</summary>
public static class LobbyDev
{
    /// <summary>Hosts or joins as the options say, then shows the lobby.</summary>
    public static void Start()
    {
        int colour = DevOptions.GetInt("colour", -1);
        string? address = DevOptions.Get("lan-join");
        if (!string.IsNullOrEmpty(address))
        {
            LobbyConnection.JoinLan(address, colour);
        }
        else
        {
            LobbyConnection.HostLan(DevOptions.Get("lan-mode") == "endless" ? RoomMode.Endless : RoomMode.Versus, colour);
        }
        UiScreens.Go(UiScreens.Lobby);
    }
}
