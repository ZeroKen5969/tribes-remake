using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Commands;
using Tribes.Core.Content;
using Xunit;

namespace Tribes.Core.Tests.Commands;

public sealed class GameOrderCodecTests
{
    private const int Tribe = 2;

    /// <summary>One order of every kind, with values across their ranges.</summary>
    public static IEnumerable<object[]> EveryOrder()
    {
        var cell = new GridPoint(17, 93);
        yield return new object[] { new MoveUnits(Tribe, UnitSet.Of(3, 1, 50), cell, -45, true) };
        yield return new object[] { new MoveUnits(Tribe, UnitSet.Of(), new GridPoint(0, 0), 0, false) };
        yield return new object[] { new PlaceBuilding(Tribe, 11, cell) };
        yield return new object[] { new PlaceTrap(Tribe, 31, 22, cell) };
        yield return new object[] { new DemolishBuilding(Tribe, 60) };
        yield return new object[] { new OrderBuildingWork(Tribe, 4, BuildingWork.StopRepair) };
        yield return new object[] { new EvictUnit(Tribe, 5, 99) };
        yield return new object[] { new CallOutUnit(Tribe, 7) };
        yield return new object[] { new AppointChief(Tribe, 8) };
        yield return new object[] { new CallArmy(Tribe, ArmyCall.AllOut, cell) };
        yield return new object[] { new CancelArmyCall(Tribe, ArmyCall.Rally) };
        yield return new object[] { new SetArmouryOrder(Tribe, 9, 5, 9) };
        yield return new object[] { new FetchWeapon(Tribe, UnitSet.Of(12), 10, 1, cell) };
        yield return new object[] { new DeliverToStore(Tribe, UnitSet.Of(12, 13), 10, cell) };
        yield return new object[] { new CastSpell(Tribe, 3, 2, 40) };
        yield return new object[] { new CastSpellAt(Tribe, 3, 3, cell) };
        yield return new object[] { new TendNeed(Tribe, 6, UnitNeed.Rest) };
        yield return new object[] { new SetHints(Tribe, false) };
        yield return new object[] { new ViewedHelp(Tribe) };
        yield return new object[] { new AdvanceScene(Tribe) };
        yield return new object[] { new PlaceBuilding(Tribe, int.MinValue, new GridPoint(int.MaxValue, -1)) };
    }

    [Theory]
    [MemberData(nameof(EveryOrder))]
    public void Decode_GivesBackTheEncodedOrder(GameOrder order)
    {
        byte[] payload = GameOrderCodec.Encode(order);

        Assert.True(GameOrderCodec.TryDecode(payload, Tribe, out GameOrder? decoded));
        Assert.Equal(order, decoded);
        Assert.Equal(GameOrderCodec.FormatVersion, payload[0]);
    }

    [Theory]
    [MemberData(nameof(EveryOrder))]
    public void Decode_RefusesEveryShortenedOrLengthenedPayload(GameOrder order)
    {
        byte[] payload = GameOrderCodec.Encode(order);

        for (int length = 0; length < payload.Length; length++)
        {
            Assert.False(GameOrderCodec.TryDecode(payload.AsSpan(0, length), Tribe, out _), $"prefix of {length} bytes");
        }
        Assert.False(GameOrderCodec.TryDecode(payload.Append((byte)0).ToArray(), Tribe, out _));
    }

    [Fact]
    public void Decode_TakesTheTribeFromTheSender()
    {
        byte[] payload = GameOrderCodec.Encode(new DemolishBuilding(0, 12));

        Assert.True(GameOrderCodec.TryDecode(payload, 3, out GameOrder? decoded));
        Assert.Equal(new DemolishBuilding(3, 12), decoded);
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 1 })]
    [InlineData(new byte[] { 2, 18 })]
    [InlineData(new byte[] { 0, 18 })]
    [InlineData(new byte[] { 1, 0 })]
    [InlineData(new byte[] { 1, 20 })]
    [InlineData(new byte[] { 1, 255 })]
    [InlineData(new byte[] { 1, 17, 2 })]
    [InlineData(new byte[] { 1, 10, 2 })]
    [InlineData(new byte[] { 1, 5, 8, 4 })]
    [InlineData(new byte[] { 1, 16, 2, 2 })]
    [InlineData(new byte[] { 1, 4, 0x80, 0x00 })]
    [InlineData(new byte[] { 1, 4, 0x80, 0x80, 0x80, 0x80, 0x10 })]
    [InlineData(new byte[] { 1, 4, 0x80, 0x80, 0x80, 0x80, 0x80, 0x01 })]
    [InlineData(new byte[] { 1, 1, 1, 2, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 1, 1, 4, 2, 2, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 1, 13, 0x80, 0x04, 0, 0, 0 })]
    public void Decode_RefusesPayloadsThatAreNotOrders(byte[] payload)
    {
        Assert.False(GameOrderCodec.TryDecode(payload, Tribe, out GameOrder? order));
        Assert.Null(order);
    }

    [Fact]
    public void Decode_RefusesPayloadsOverTheLimit()
    {
        byte[] payload = GameOrderCodec.Encode(new ViewedHelp(Tribe)).Concat(new byte[GameOrderCodec.MaxPayloadBytes]).ToArray();

        Assert.False(GameOrderCodec.TryDecode(payload, Tribe, out _));
    }

    [Fact]
    public void Encode_RefusesOrdersItDoesNotKnowOrThatAreTooLong()
    {
        Assert.Throws<ArgumentException>(() => GameOrderCodec.Encode(new UnknownOrder(Tribe)));
        Assert.Throws<ArgumentNullException>(() => GameOrderCodec.Encode(null!));
        var many = new UnitSet(Enumerable.Range(0, UnitSet.MaxCount).Select(i => int.MaxValue - i));
        Assert.Throws<ArgumentException>(() => GameOrderCodec.Encode(new MoveUnits(Tribe, many, default, 0, false)));
    }

    [Fact]
    public void RandomPayloads_NeverThrow_AndWhatDecodesEncodesTheSame()
    {
        var random = new Random(20261013);
        int decoded = 0;
        for (int i = 0; i < 200_000; i++)
        {
            var payload = new byte[random.Next(0, 24)];
            random.NextBytes(payload);
            if (payload.Length > 0 && random.Next(2) == 0)
            {
                payload[0] = GameOrderCodec.FormatVersion;
            }
            if (GameOrderCodec.TryDecode(payload, Tribe, out GameOrder? order))
            {
                decoded++;
                Assert.Equal(payload, GameOrderCodec.Encode(order));
            }
        }
        Assert.True(decoded > 20, $"only {decoded} random payloads decoded");
    }

    [Fact]
    public void ChangedPayloads_NeverThrow_AndWhatDecodesEncodesTheSame()
    {
        var random = new Random(4);
        byte[][] valid = EveryOrder().Select(o => GameOrderCodec.Encode((GameOrder)o[0])).ToArray();
        for (int i = 0; i < 100_000; i++)
        {
            byte[] payload = (byte[])valid[random.Next(valid.Length)].Clone();
            int changes = random.Next(1, 4);
            for (int c = 0; c < changes && payload.Length > 0; c++)
            {
                payload[random.Next(payload.Length)] = (byte)random.Next(256);
            }
            if (GameOrderCodec.TryDecode(payload, Tribe, out GameOrder? order))
            {
                Assert.Equal(payload, GameOrderCodec.Encode(order));
            }
        }
    }

    private sealed record UnknownOrder(int Tribe) : GameOrder(Tribe);
}
