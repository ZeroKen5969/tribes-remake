namespace Tribes.Core.World;

/// <summary>The state of a match as a whole (see <see cref="WorldMode.Match"/>); unused in the campaign.</summary>
public sealed class MatchState
{
    /// <summary>Whether the match ends once no two tribes still in it are enemies.</summary>
    public bool EndsWithLastSide { get; internal set; } = true;

    /// <summary>The frame the match was decided at (its last side known), or -1 while it goes on.</summary>
    public int DecidedAtFrame { get; internal set; } = -1;

    /// <summary>Whether the match is decided.</summary>
    public bool IsDecided => DecidedAtFrame >= 0;

    internal void Exchange(StateExchange s)
    {
        EndsWithLastSide = s.Flag(EndsWithLastSide);
        DecidedAtFrame = s.Value(DecidedAtFrame);
    }
}
