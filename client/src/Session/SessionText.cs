using System.Globalization;
using Tribes.Client.Ui.Text;
using Tribes.Session;

namespace Tribes.Client.Session;

/// <summary>The player-facing texts of the session layer (keys "session.*" in data/client/strings/ui.*.json).</summary>
public static class SessionText
{
    /// <summary>A line for a notice, or empty when the notice is not worth showing.</summary>
    public static string Describe(MatchNotice notice)
    {
        string name = notice?.Player?.Name ?? "";
        return notice?.Kind switch
        {
            MatchNoticeKind.PlayerJoined => UiText.F("session.notice.joined", name),
            MatchNoticeKind.PlayerAbsent => UiText.F("session.notice.absent", name),
            MatchNoticeKind.PlayerReturned => UiText.F("session.notice.returned", name),
            MatchNoticeKind.PlayerForfeited => UiText.F("session.notice.forfeited", name),
            MatchNoticeKind.PlayerOut => notice.Player?.IsComputer == true ? UiText.T("session.notice.computer_out") : UiText.F("session.notice.out", name),
            MatchNoticeKind.Reconnecting when notice.Detail == nameof(Tribes.Net.ClientPhase.Migrating) => UiText.T("session.notice.migrating"),
            MatchNoticeKind.Reconnecting => UiText.T("session.notice.reconnecting"),
            MatchNoticeKind.HostChanged => UiText.F("session.notice.host_changed", name),
            MatchNoticeKind.Autosaved => UiText.T("session.notice.autosaved"),
            MatchNoticeKind.SaveFailed => UiText.F("session.notice.save_failed", notice.Detail),
            MatchNoticeKind.RequestDenied => UiText.T("session.notice.request_denied"),
            MatchNoticeKind.OrderDropped => UiText.T("session.notice.order_dropped"),
            _ => "",
        };
    }

    /// <summary>Why the match stopped, for a dialog.</summary>
    public static string Describe(MatchStop stop)
    {
        if (stop is null)
        {
            return "";
        }
        return stop.Reason switch
        {
            StopReason.Quit => UiText.T("session.stop.quit"),
            StopReason.HostLeft => UiText.T("session.stop.host_left"),
            StopReason.ConnectionLost => UiText.T("session.stop.connection_lost"),
            StopReason.Kicked => UiText.T("session.stop.kicked"),
            StopReason.Desync when stop.Desync is { } d => UiText.F("session.stop.desync",
                d.Tick.ToString(CultureInfo.InvariantCulture), Hex(d.ExpectedHash), Hex(d.ReportedHash)),
            _ => UiText.F("session.stop.error", stop.Message),
        };
    }

    /// <summary>Why a save cannot be used, for a dialog.</summary>
    public static string SaveProblem(SaveFileException error) => error?.Problem switch
    {
        Tribes.Session.SaveProblem.NotASave => UiText.T("session.save.not_a_save"),
        Tribes.Session.SaveProblem.NewerFormat => UiText.T("session.save.newer"),
        Tribes.Session.SaveProblem.OtherVersion => UiText.T("session.save.other_version"),
        Tribes.Session.SaveProblem.NotTheHost => UiText.T("session.save.not_host"),
        Tribes.Session.SaveProblem.CannotHost => UiText.T("session.save.cannot_host"),
        _ => UiText.T("session.save.damaged"),
    };

    /// <summary>A participant's state, as the player lists show it (「離線」 for absent players).</summary>
    public static string State(PlayerEntry player) => player switch
    {
        null => "",
        { IsComputer: true, State: not PlayerState.Out } => UiText.T("session.state.computer"),
        { State: PlayerState.Absent } => UiText.T("session.state.absent"),
        { State: PlayerState.Forfeited } => UiText.T("session.state.forfeited"),
        { State: PlayerState.Out } => UiText.T("session.state.out"),
        { State: PlayerState.Spectator } => UiText.T("session.state.spectator"),
        _ => UiText.T("session.state.playing"),
    };

    /// <summary>What the local player is doing when not playing, or empty while playing.</summary>
    public static string Role(LocalRole role) => role switch
    {
        LocalRole.Spectator => UiText.T("session.role.spectator"),
        LocalRole.Forfeited => UiText.T("session.role.forfeited"),
        LocalRole.Out => UiText.T("session.role.out"),
        _ => "",
    };

    private static string Hex(uint hash) => "0x" + hash.ToString("x8", CultureInfo.InvariantCulture);
}
