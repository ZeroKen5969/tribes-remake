using System;
using System.IO;

namespace Tribes.Core.World;

/// <summary>
/// Moves every persistent value of the world in one fixed order: written to a save, read back from
/// one, or folded into a hash. Each model class lists its values once, in an <c>Exchange</c> method,
/// as <c>X = s.Value(X)</c>: a writer or hasher returns the value unchanged, a reader returns the
/// stored one.
/// </summary>
public abstract class StateExchange
{
    /// <summary>The first format that keeps each tribe's controller, team and player or computer state.</summary>
    public const int TribalFormat = 2;

    /// <summary>
    /// The first format that can hold an endless world: its chunks, the endless state, alarm marks of
    /// any tribe and animals' habitats.
    /// </summary>
    public const int EndlessFormat = 3;

    /// <summary>The first format that keeps the world's settings (<see cref="WorldSettings"/>).</summary>
    public const int SettingsFormat = 4;

    /// <summary>
    /// The first format that keeps the owner of every unit and building slot (a match's tribes get
    /// slots as they need them) and the length of every table that grows in a match.
    /// </summary>
    public const int SlotsFormat = 5;

    /// <summary>The first format that keeps each tribe's frames since it fought (a match's quiet time per tribe).</summary>
    public const int CombatFormat = 6;

    /// <summary>The first format that keeps the buildings each tribe remembers seeing (a match's land out of sight shows them as last seen).</summary>
    public const int MemoryFormat = 7;

    /// <summary>The format written now.</summary>
    public const int CurrentFormat = MemoryFormat;

    /// <summary>The format being read (older saves have fewer values); writers and hashers use the current one.</summary>
    public int Format { get; init; } = CurrentFormat;

    /// <summary>Whether values are read back from a save (the values passed in are then ignored).</summary>
    public virtual bool IsReading => false;

    /// <summary>Exchanges an integer.</summary>
    public abstract int Value(int value);

    /// <summary>Exchanges a 64-bit integer.</summary>
    public abstract long Value64(long value);

    /// <summary>Exchanges a flag.</summary>
    public bool Flag(bool value) => Value(value ? 1 : 0) != 0;

    /// <summary>Exchanges a block of bytes as they are (a reader fills <paramref name="data"/>).</summary>
    public abstract void Bytes(Span<byte> data);
}

/// <summary>Writes the exchanged values to a stream.</summary>
public sealed class StateWriter : StateExchange
{
    private readonly BinaryWriter writer;

    /// <summary>Writes to <paramref name="output"/>.</summary>
    public StateWriter(BinaryWriter output) => writer = output ?? throw new ArgumentNullException(nameof(output));

    /// <inheritdoc/>
    public override int Value(int value)
    {
        writer.Write7BitEncodedInt(value);
        return value;
    }

    /// <inheritdoc/>
    public override long Value64(long value)
    {
        writer.Write7BitEncodedInt64(value);
        return value;
    }

    /// <inheritdoc/>
    public override void Bytes(Span<byte> data) => writer.Write(data);
}

/// <summary>Reads the exchanged values from a stream written by <see cref="StateWriter"/>.</summary>
public sealed class StateReader : StateExchange
{
    private readonly BinaryReader reader;

    /// <summary>Reads from <paramref name="input"/>.</summary>
    public StateReader(BinaryReader input) => reader = input ?? throw new ArgumentNullException(nameof(input));

    /// <inheritdoc/>
    public override bool IsReading => true;

    /// <inheritdoc/>
    public override int Value(int value) => reader.Read7BitEncodedInt();

    /// <inheritdoc/>
    public override long Value64(long value) => reader.Read7BitEncodedInt64();

    /// <inheritdoc/>
    public override void Bytes(Span<byte> data)
    {
        if (reader.Read(data) != data.Length)
        {
            throw new EndOfStreamException("the saved world ends inside a block of bytes");
        }
    }
}

/// <summary>
/// Folds the exchanged values into a 64-bit hash (used to detect diverging games): each value is
/// mixed in with one multiply, and the result is mixed once more when it is read. Large parts of
/// the state that keep a running hash of their own (the map) give it with <see cref="Mix64"/>.
/// </summary>
public sealed class StateHasher : StateExchange
{
    private const ulong Seed = 0x9E3779B97F4A7C15UL;
    private const ulong Multiplier = 0xBF58476D1CE4E5B9UL;
    private const int Rotation = 23;

    private ulong state = Seed;

    /// <summary>The hash of everything exchanged so far.</summary>
    public ulong Hash => Finish(state);

    /// <inheritdoc/>
    public override int Value(int value)
    {
        Mix((uint)value);
        return value;
    }

    /// <inheritdoc/>
    public override long Value64(long value)
    {
        Mix((uint)value);
        Mix((uint)(value >> 32));
        return value;
    }

    /// <inheritdoc/>
    public override void Bytes(Span<byte> data)
    {
        foreach (byte value in data)
        {
            Mix(value);
        }
    }

    /// <summary>Mixes in a 64-bit summary of a part of the state.</summary>
    public void Mix64(ulong value)
    {
        Mix((uint)value);
        Mix((uint)(value >> 32));
    }

    /// <summary>Mixes in every value of <paramref name="values"/> (as <see cref="Value(int)"/> would, without the call per value).</summary>
    public void MixAll(ReadOnlySpan<int> values)
    {
        ulong hash = state;
        foreach (int value in values)
        {
            hash = (System.Numerics.BitOperations.RotateLeft(hash, Rotation) ^ (uint)value) * Multiplier;
        }
        state = hash;
    }

    /// <summary>Mixes in every word of <paramref name="words"/>.</summary>
    public void MixAll(ReadOnlySpan<ulong> words)
    {
        ulong hash = state;
        foreach (ulong word in words)
        {
            hash = (System.Numerics.BitOperations.RotateLeft(hash, Rotation) ^ word) * Multiplier;
        }
        state = hash;
    }

    /// <summary>A well-mixed 64-bit value of <paramref name="value"/> (the finaliser of SplitMix64).</summary>
    public static ulong Finish(ulong value)
    {
        value ^= value >> 30;
        value *= 0xBF58476D1CE4E5B9UL;
        value ^= value >> 27;
        value *= 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }

    private void Mix(uint value) => state = (System.Numerics.BitOperations.RotateLeft(state, Rotation) ^ value) * Multiplier;
}
