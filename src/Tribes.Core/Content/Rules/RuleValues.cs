using Tribes.Core.World;

namespace Tribes.Core.Content.Rules;

/// <summary>
/// A number a table gives either directly (<see cref="Constant"/>) or as the value of a property of
/// the acting unit (<see cref="Property"/>); exactly one of the two is set.
/// </summary>
/// <param name="Constant">The number itself, or null when it is read from <see cref="Property"/>.</param>
/// <param name="Property">The unit property holding the number, or null for a constant.</param>
public readonly record struct ValueSource(int? Constant, EntityProperty? Property)
{
    /// <summary>A fixed number.</summary>
    public static ValueSource Of(int constant) => new(constant, null);

    /// <summary>The value of <paramref name="property"/> of the acting unit.</summary>
    public static ValueSource From(EntityProperty property) => new(null, property);
}

/// <summary>
/// Where the per-step rate of a working unit goes: a property of the unit itself, or a counter of
/// its tribe; exactly one of the two is set.
/// </summary>
/// <param name="Property">The unit property the rate is added to, or null.</param>
/// <param name="Counter">The tribe counter the rate is added to, or null.</param>
public readonly record struct RateTarget(EntityProperty? Property, TribeCounter? Counter);

/// <summary>An amount of one tribe counter (a cost, a stock, a delivery).</summary>
/// <param name="Counter">The counter.</param>
/// <param name="Amount">How much of it.</param>
public readonly record struct CounterAmount(TribeCounter Counter, int Amount);

/// <summary>A rectangle of map cells; both corners are inside it.</summary>
/// <param name="Left">Smallest column.</param>
/// <param name="Top">Smallest row.</param>
/// <param name="Right">Largest column.</param>
/// <param name="Bottom">Largest row.</param>
public readonly record struct CellRect(int Left, int Top, int Right, int Bottom)
{
    /// <summary>Whether <paramref name="cell"/> lies in the rectangle.</summary>
    public bool Contains(GridPoint cell) =>
        cell.X >= Left && cell.X <= Right && cell.Y >= Top && cell.Y <= Bottom;
}

/// <summary>A cell offset from a reference cell.</summary>
/// <param name="Dx">Columns to the right.</param>
/// <param name="Dy">Rows down.</param>
public readonly record struct CellOffset(int Dx, int Dy);
