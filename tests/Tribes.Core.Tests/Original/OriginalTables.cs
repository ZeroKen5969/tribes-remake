using System;
using System.IO;
using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Legacy;

namespace Tribes.Core.Tests;

/// <summary>
/// The original game's tables as the translated original holds them once it has loaded its data:
/// the byte blocks of its data file "a", the short blocks after them and the "pd0" tables. They
/// are copied, so later runs of the original do not change them.
/// </summary>
internal sealed class OriginalTables
{
    private static readonly Lazy<OriginalTables> Loaded = new(() => new OriginalTables());

    private readonly sbyte[] data;
    private readonly short[] starts;
    private readonly short[] shorts;
    private readonly short[] shortStarts;
    private readonly sbyte[][] pdBytes;
    private readonly short[][] pdShorts;

    private OriginalTables()
    {
        GameCanvas game = Tribes.Trace.Program.RunTo(Path.Combine(TestPaths.RepoRoot(), "original", "game", "res"), 300, 0L,
            int.MaxValue) ?? throw new InvalidOperationException("the original did not load");
        data = (sbyte[])game.bArr_a.Clone();
        starts = (short[])game.sArr_a.Clone();
        shorts = (short[])game.sArr_b.Clone();
        shortStarts = (short[])game.sArr_c.Clone();
        pdBytes = game.bArr2_j.Select(t => (sbyte[])t.Clone()).ToArray();
        pdShorts = game.sArr2_b.Select(t => (short[])t.Clone()).ToArray();
    }

    /// <summary>The tables (the original runs once per test run).</summary>
    public static OriginalTables Instance => Loaded.Value;

    /// <summary>The rules exported to data/.</summary>
    public static GameRules ExportedRules { get; } = GameContent.Load(Path.Combine(TestPaths.RepoRoot(), "data")).Rules;

    /// <summary>Length of byte block <paramref name="block"/>.</summary>
    public int Length(int block) => End(block) - starts[block];

    /// <summary>Entry <paramref name="i"/> of byte block <paramref name="block"/>, signed.</summary>
    public int B(int block, int i) =>
        i >= 0 && i < Length(block)
            ? data[starts[block] + i]
            : throw new ArgumentOutOfRangeException(nameof(i), i, $"block {block} has {Length(block)} entries");

    /// <summary>Entry <paramref name="i"/> of byte block <paramref name="block"/>, unsigned.</summary>
    public int U(int block, int i) => B(block, i) & 0xFF;

    /// <summary>All entries of byte block <paramref name="block"/>.</summary>
    public int[] Block(int block) => Enumerable.Range(0, Length(block)).Select(i => B(block, i)).ToArray();

    /// <summary>
    /// Entries <paramref name="from"/> .. <paramref name="to"/> - 1 of byte block <paramref name="block"/>; none when
    /// <paramref name="to"/> is below <paramref name="from"/> (the original's loops then run zero times).
    /// </summary>
    public int[] Range(int block, int from, int to) =>
        Enumerable.Range(from, Math.Max(0, to - from)).Select(i => B(block, i)).ToArray();

    /// <summary>Entry <paramref name="i"/> of short block <paramref name="k"/>.</summary>
    public int Short(int k, int i) => shorts[shortStarts[k] + i];

    /// <summary>Length of short block <paramref name="k"/> (the last one has no end marker).</summary>
    public int ShortLength(int k) => shortStarts[k + 1] - shortStarts[k];

    /// <summary>Entry <paramref name="i"/> of "pd0" byte table <paramref name="table"/>.</summary>
    public int PdByte(int table, int i) => pdBytes[table][i];

    /// <summary>Length of "pd0" byte table <paramref name="table"/>.</summary>
    public int PdByteLength(int table) => pdBytes[table].Length;

    /// <summary>Entry <paramref name="i"/> of "pd0" short table <paramref name="table"/>.</summary>
    public int PdShort(int table, int i) => pdShorts[table][i];

    private int End(int block) => block + 1 < starts.Length ? starts[block + 1] : data.Length;
}
