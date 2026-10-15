using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Tribes.Core.Content;

namespace Tribes.Core.Commands;

/// <summary>
/// The payload of a game command: an order in a compact binary form. A payload is the format
/// version, the order's code, then its fields — numbers as zig-zag LEB128 varints, flags and
/// choices as one byte, cells as two numbers, unit sets as a count and the slots. The tribe is not
/// part of it: the receiver knows which slot sent the payload. Decoding is a pure function of the
/// bytes, so every peer gets the same order from the same payload; anything that is not exactly
/// an order of this format is refused.
/// </summary>
public static class GameOrderCodec
{
    /// <summary>The format version written first; payloads of other versions are refused.</summary>
    public const byte FormatVersion = 1;

    /// <summary>Longest payload (the network layer's command limit).</summary>
    public const int MaxPayloadBytes = 1024;

    private const int VarintGroupBits = 7, VarintMore = 0x80, VarintGroup = 0x7F, MaxVarintBytes = 5;

    /// <summary>Order codes on the wire. Never renumber; add new codes at the end.</summary>
    private enum Code : byte
    {
        MoveUnits = 1,
        PlaceBuilding = 2,
        PlaceTrap = 3,
        DemolishBuilding = 4,
        OrderBuildingWork = 5,
        EvictUnit = 6,
        CallOutUnit = 7,
        AppointChief = 8,
        CallArmy = 9,
        CancelArmyCall = 10,
        SetArmouryOrder = 11,
        FetchWeapon = 12,
        DeliverToStore = 13,
        CastSpell = 14,
        CastSpellAt = 15,
        TendNeed = 16,
        SetHints = 17,
        ViewedHelp = 18,
        AdvanceScene = 19,
        InviteAlly = 20,
        AnswerAlliance = 21,
        LeaveAlliance = 22,
        TransferGoods = 23,
    }

    /// <summary>The payload of <paramref name="order"/> (its tribe is left out).</summary>
    /// <exception cref="ArgumentException">The order is of an unknown type or longer than <see cref="MaxPayloadBytes"/>.</exception>
    /// <exception cref="OverflowException">A choice of the order is outside 0..255 (no order of the game has such a value).</exception>
    public static byte[] Encode(GameOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        var w = new Writer();
        w.Byte(FormatVersion);
        switch (order)
        {
            case MoveUnits o: w.Code(Code.MoveUnits).Units(o.Units).Cell(o.Cell).Int(o.Target).Flag(o.Hurry); break;
            case PlaceBuilding o: w.Code(Code.PlaceBuilding).Int(o.Kind).Cell(o.Origin); break;
            case PlaceTrap o: w.Code(Code.PlaceTrap).Int(o.Armoury).Int(o.TrapKind).Cell(o.Cell); break;
            case DemolishBuilding o: w.Code(Code.DemolishBuilding).Int(o.Building); break;
            case OrderBuildingWork o: w.Code(Code.OrderBuildingWork).Int(o.Building).Choice(o.Work); break;
            case EvictUnit o: w.Code(Code.EvictUnit).Int(o.Building).Int(o.Unit); break;
            case CallOutUnit o: w.Code(Code.CallOutUnit).Int(o.Unit); break;
            case AppointChief o: w.Code(Code.AppointChief).Int(o.Unit); break;
            case CallArmy o: w.Code(Code.CallArmy).Choice(o.Call).Cell(o.Point); break;
            case CancelArmyCall o: w.Code(Code.CancelArmyCall).Choice(o.Call); break;
            case SetArmouryOrder o: w.Code(Code.SetArmouryOrder).Int(o.Armoury).Int(o.Product).Int(o.Count); break;
            case FetchWeapon o: w.Code(Code.FetchWeapon).Units(o.Units).Int(o.Store).Int(o.Weapon).Cell(o.Cell); break;
            case DeliverToStore o: w.Code(Code.DeliverToStore).Units(o.Units).Int(o.Store).Cell(o.Cell); break;
            case CastSpell o: w.Code(Code.CastSpell).Int(o.Sanctuary).Int(o.Spell).Int(o.Unit); break;
            case CastSpellAt o: w.Code(Code.CastSpellAt).Int(o.Sanctuary).Int(o.Spell).Cell(o.Cell); break;
            case TendNeed o: w.Code(Code.TendNeed).Int(o.Unit).Choice(o.Need); break;
            case SetHints o: w.Code(Code.SetHints).Flag(o.Enabled); break;
            case ViewedHelp: w.Code(Code.ViewedHelp); break;
            case AdvanceScene: w.Code(Code.AdvanceScene); break;
            case InviteAlly o: w.Code(Code.InviteAlly).Int(o.Invitee); break;
            case AnswerAlliance o: w.Code(Code.AnswerAlliance).Int(o.Inviter).Flag(o.Accept); break;
            case LeaveAlliance o: w.Code(Code.LeaveAlliance).Int(o.Ally); break;
            case TransferGoods o: w.Code(Code.TransferGoods).Int(o.Ally).Choice(o.Good).Int(o.Amount); break;
            default: throw new ArgumentException($"{order.GetType().Name} is not an order the codec knows", nameof(order));
        }
        byte[] payload = w.ToArray();
        if (payload.Length > MaxPayloadBytes)
        {
            throw new ArgumentException($"the order needs {payload.Length} bytes, more than {MaxPayloadBytes}", nameof(order));
        }
        return payload;
    }

    /// <summary>
    /// Reads an order of <paramref name="tribe"/> from <paramref name="payload"/>; false for anything
    /// that is not exactly one order of this format (wrong version, unknown code, a field missing or
    /// out of its range, bytes left over).
    /// </summary>
    public static bool TryDecode(ReadOnlySpan<byte> payload, int tribe, [NotNullWhen(true)] out GameOrder? order)
    {
        order = null;
        if (payload.Length < 2 || payload.Length > MaxPayloadBytes || payload[0] != FormatVersion)
        {
            return false;
        }
        var r = new Reader(payload[1..]);
        GameOrder? read = Read(ref r, tribe);
        if (read == null || r.Failed || !r.AtEnd)
        {
            return false;
        }
        order = read;
        return true;
    }

    private static GameOrder? Read(ref Reader r, int tribe) => (Code)r.Byte() switch
    {
        Code.MoveUnits => ReadMove(ref r, tribe),
        Code.PlaceBuilding => new PlaceBuilding(tribe, r.Int(), r.Cell()),
        Code.PlaceTrap => new PlaceTrap(tribe, r.Int(), r.Int(), r.Cell()),
        Code.DemolishBuilding => new DemolishBuilding(tribe, r.Int()),
        Code.OrderBuildingWork => new OrderBuildingWork(tribe, r.Int(), r.Choice<BuildingWork>()),
        Code.EvictUnit => new EvictUnit(tribe, r.Int(), r.Int()),
        Code.CallOutUnit => new CallOutUnit(tribe, r.Int()),
        Code.AppointChief => new AppointChief(tribe, r.Int()),
        Code.CallArmy => new CallArmy(tribe, r.Choice<ArmyCall>(), r.Cell()),
        Code.CancelArmyCall => new CancelArmyCall(tribe, r.Choice<ArmyCall>()),
        Code.SetArmouryOrder => new SetArmouryOrder(tribe, r.Int(), r.Int(), r.Int()),
        Code.FetchWeapon => ReadFetch(ref r, tribe),
        Code.DeliverToStore => ReadDeliver(ref r, tribe),
        Code.CastSpell => new CastSpell(tribe, r.Int(), r.Int(), r.Int()),
        Code.CastSpellAt => new CastSpellAt(tribe, r.Int(), r.Int(), r.Cell()),
        Code.TendNeed => new TendNeed(tribe, r.Int(), r.Choice<UnitNeed>()),
        Code.SetHints => new SetHints(tribe, r.Flag()),
        Code.ViewedHelp => new ViewedHelp(tribe),
        Code.AdvanceScene => new AdvanceScene(tribe),
        Code.InviteAlly => new InviteAlly(tribe, r.Int()),
        Code.AnswerAlliance => new AnswerAlliance(tribe, r.Int(), r.Flag()),
        Code.LeaveAlliance => new LeaveAlliance(tribe, r.Int()),
        Code.TransferGoods => new TransferGoods(tribe, r.Int(), r.Choice<World.TribeCounter>(), r.Int()),
        _ => null,
    };

    private static MoveUnits? ReadMove(ref Reader r, int tribe)
    {
        UnitSet? units = r.Units();
        GridPoint cell = r.Cell();
        int target = r.Int();
        bool hurry = r.Flag();
        return units == null ? null : new MoveUnits(tribe, units, cell, target, hurry);
    }

    private static FetchWeapon? ReadFetch(ref Reader r, int tribe)
    {
        UnitSet? units = r.Units();
        int store = r.Int();
        int weapon = r.Int();
        GridPoint cell = r.Cell();
        return units == null ? null : new FetchWeapon(tribe, units, store, weapon, cell);
    }

    private static DeliverToStore? ReadDeliver(ref Reader r, int tribe)
    {
        UnitSet? units = r.Units();
        int store = r.Int();
        GridPoint cell = r.Cell();
        return units == null ? null : new DeliverToStore(tribe, units, store, cell);
    }

    private sealed class Writer
    {
        private readonly List<byte> bytes = new();

        public Writer Byte(byte value)
        {
            bytes.Add(value);
            return this;
        }

        public Writer Code(Code code) => Byte((byte)code);

        public Writer Flag(bool value) => Byte(value ? (byte)1 : (byte)0);

        public Writer Choice<T>(T value)
            where T : struct, Enum => Byte(Convert.ToByte(value, System.Globalization.CultureInfo.InvariantCulture));

        public Writer Int(int value)
        {
            uint zigzag = (uint)((value << 1) ^ (value >> 31));
            while (zigzag >= VarintMore)
            {
                bytes.Add((byte)((zigzag & VarintGroup) | VarintMore));
                zigzag >>= VarintGroupBits;
            }
            bytes.Add((byte)zigzag);
            return this;
        }

        public Writer Cell(GridPoint cell) => Int(cell.X).Int(cell.Y);

        public Writer Units(UnitSet units)
        {
            ArgumentNullException.ThrowIfNull(units);
            Int(units.Count);
            foreach (int unit in units)
            {
                Int(unit);
            }
            return this;
        }

        public byte[] ToArray() => bytes.ToArray();
    }

    /// <summary>Reads fields; a missing or bad field sets <see cref="Failed"/> and reads as 0.</summary>
    private ref struct Reader
    {
        private readonly ReadOnlySpan<byte> bytes;
        private int position;

        public Reader(ReadOnlySpan<byte> bytes)
        {
            this.bytes = bytes;
            position = 0;
            Failed = false;
        }

        public bool Failed { get; private set; }

        public readonly bool AtEnd => position == bytes.Length;

        public byte Byte()
        {
            if (position >= bytes.Length)
            {
                Failed = true;
                return 0;
            }
            return bytes[position++];
        }

        public bool Flag()
        {
            byte value = Byte();
            Failed |= value > 1;
            return value == 1;
        }

        public T Choice<T>()
            where T : struct, Enum
        {
            T value = (T)Enum.ToObject(typeof(T), Byte());
            Failed |= !Enum.IsDefined(value);
            return value;
        }

        public int Int()
        {
            uint zigzag = 0;
            for (int i = 0; i < MaxVarintBytes; i++)
            {
                uint group = Byte();
                if (i == MaxVarintBytes - 1 && group > (uint.MaxValue >> (VarintGroupBits * i)))
                {
                    Failed = true;
                    return 0;
                }
                zigzag |= (group & VarintGroup) << (VarintGroupBits * i);
                if ((group & VarintMore) == 0)
                {
                    // A shorter encoding exists when a later group is zero: refuse it, so each order has one payload.
                    Failed |= i > 0 && group == 0;
                    return (int)(zigzag >> 1) ^ -(int)(zigzag & 1);
                }
            }
            Failed = true;
            return 0;
        }

        public GridPoint Cell()
        {
            int x = Int();
            return new GridPoint(x, Int());
        }

        public UnitSet? Units()
        {
            int count = Int();
            if (count < 0 || count > UnitSet.MaxCount)
            {
                Failed = true;
                return null;
            }
            var units = new int[count];
            for (int i = 0; i < count && !Failed; i++)
            {
                units[i] = Int();
            }
            if (Failed || !UnitSet.IsValid(units))
            {
                Failed = true;
                return null;
            }
            return new UnitSet(units);
        }
    }
}
