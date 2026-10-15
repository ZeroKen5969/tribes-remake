using System;
using System.IO;
using System.Text;
using Tribes.Net;
using Tribes.Session.Tests.Support;
using Xunit;

namespace Tribes.Session.Tests;

public sealed class PlayerTableTests
{
    private static PlayerEntry Entry(int slot, string name, PlayerState state = PlayerState.Playing, int tribe = -1) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Slot = slot,
        Tribe = tribe,
        Colour = slot < 0 ? -1 : slot,
        Team = Math.Max(0, slot),
        State = state,
    };

    [Fact]
    public void Of_SortsSlotHoldersBySlot_ThenOthersInOrder()
    {
        PlayerEntry watcher = Entry(-1, "Watcher", PlayerState.Spectator), b = Entry(2, "B"), a = Entry(0, "A"), late = Entry(-1, "Late", PlayerState.Spectator);
        PlayerTable table = PlayerTable.Of([watcher, b, a, late]);

        Assert.Equal(["A", "B", "Watcher", "Late"], [table[0].Name, table[1].Name, table[2].Name, table[3].Name]);
        Assert.Equal(4, table.Count);
        Assert.Same(b, table.BySlot(2));
        Assert.Null(table.BySlot(1));
        Assert.Same(late, table.ById(late.Id));
        Assert.Null(table.ById(Guid.NewGuid()));
    }

    [Fact]
    public void Of_RejectsDuplicateIdsAndSlots()
    {
        PlayerEntry a = Entry(0, "A");
        Assert.Throws<ArgumentException>(() => PlayerTable.Of([a, a with { Slot = 1 }]));
        Assert.Throws<ArgumentException>(() => PlayerTable.Of([a, Entry(0, "B")]));
        Assert.Throws<ArgumentNullException>(() => PlayerTable.Of(null!));
        Assert.Throws<ArgumentNullException>(() => PlayerTable.Of([null!]));
    }

    [Fact]
    public void With_ReplacesById_OrAdds_WithoutChangingTheOriginal()
    {
        PlayerEntry a = Entry(0, "A", tribe: 0);
        PlayerTable table = PlayerTable.Of([a]);
        PlayerTable absent = table.With(a with { State = PlayerState.Absent });
        PlayerTable joined = absent.With(Entry(1, "B", tribe: 2));

        Assert.Equal(PlayerState.Playing, table.BySlot(0)!.State);
        Assert.Equal(PlayerState.Absent, absent.BySlot(0)!.State);
        Assert.Equal(2, joined.Count);
        Assert.Same(joined.BySlot(1), joined.ByTribe(2));
        Assert.Null(joined.ByTribe(-1));
        Assert.Equal(0, joined.FirstHumanTribe);
        Assert.Equal(-1, PlayerTable.Empty.FirstHumanTribe);
        Assert.Throws<ArgumentNullException>(() => table.With(null!));
    }

    [Fact]
    public void Entry_Flags()
    {
        Assert.True(Entry(0, "A", tribe: 0).TakesOrders);
        Assert.False(Entry(0, "A", PlayerState.Absent, 0).TakesOrders);
        Assert.False(Entry(0, "A").TakesOrders);
        Assert.False((Entry(0, "A", tribe: 0) with { IsComputer = true }).TakesOrders);
        Assert.True(Entry(0, "A", PlayerState.Forfeited).IsFinished);
        Assert.True(Entry(0, "A", PlayerState.Out).IsFinished);
        Assert.False(Entry(0, "A", PlayerState.Absent).IsFinished);
        Assert.False(Entry(-1, "W", PlayerState.Spectator).HasSlot);
    }

    [Fact]
    public void From_CopiesTheRoomPlayer()
    {
        PlayerInfo cpu = TestContent.Computer(3, level: 1);
        PlayerEntry entry = PlayerEntry.From(cpu, 1);
        Assert.Equal((cpu.Id, 3, 1, true, 1, PlayerState.Playing), (entry.Id, entry.Slot, entry.Tribe, entry.IsComputer, entry.AiLevel, entry.State));
        Assert.Equal(PlayerState.Absent, PlayerEntry.From(TestContent.Human(1, "A", presence: PlayerPresence.Absent), 0).State);
        Assert.Equal(PlayerState.Forfeited, PlayerEntry.From(TestContent.Human(1, "A", presence: PlayerPresence.Forfeited), 0).State);
        PlayerEntry watcher = PlayerEntry.From(TestContent.Spectator("W"), 2);
        Assert.Equal((PlayerState.Spectator, -1), (watcher.State, watcher.Tribe));
        Assert.Throws<ArgumentNullException>(() => PlayerEntry.From(null!, 0));
    }

    [Fact]
    public void WriteRead_RoundTrips()
    {
        PlayerTable table = PlayerTable.Of(
        [
            Entry(0, "Ånne 🦴", tribe: 0),
            Entry(1, "B", PlayerState.Out, 1) with { IsComputer = true, AiLevel = 2 },
            Entry(-1, "W", PlayerState.Spectator),
        ]);
        PlayerTable read = Read(Write(table));
        Assert.Equal(table.Count, read.Count);
        for (int i = 0; i < table.Count; i++)
        {
            Assert.Equal(table[i], read[i]);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1025)]
    [InlineData(int.MaxValue)]
    public void Read_RejectsCountsTheDataCannotHold(int count)
    {
        byte[] data = BitConverter.GetBytes(count);
        Assert.Throws<InvalidDataException>(() => Read(data));
    }

    [Fact]
    public void Table_HoldsAnyNumberOfComputerTribes()
    {
        const int Tribes = 5000;
        var entries = new PlayerEntry[Tribes + 1];
        entries[0] = Entry(0, "A", tribe: 0);
        for (int t = 1; t <= Tribes; t++)
        {
            entries[t] = new PlayerEntry { Id = Guid.NewGuid(), Name = "", Tribe = t, IsComputer = true };
        }
        PlayerTable table = PlayerTable.Of(entries);
        PlayerTable read = Read(Write(table));

        Assert.Equal(Tribes + 1, read.Count);
        Assert.Equal(entries[4321], read.ByTribe(4321));
        Assert.Equal(entries[17].Id, read.ById(entries[17].Id)!.Id);
        Assert.True(read.HasTribe(Tribes));
        Assert.False(read.HasTribe(Tribes + 1));
        Assert.Equal(table.Digest, read.Digest);
        Assert.NotEqual(table.Digest, table.With(entries[9] with { State = PlayerState.Out }).Digest);
    }

    [Fact]
    public void WithAll_ReplacesSeveralEntriesAtOnce()
    {
        PlayerEntry a = Entry(0, "A", tribe: 0), b = Entry(1, "B", tribe: 1);
        PlayerTable table = PlayerTable.Of([a, b]);

        PlayerTable both = table.WithAll([a with { State = PlayerState.Out }, b with { State = PlayerState.Absent }, Entry(2, "C", tribe: 2)]);

        Assert.Equal(PlayerState.Out, both.BySlot(0)!.State);
        Assert.Equal(PlayerState.Absent, both.BySlot(1)!.State);
        Assert.Equal(3, both.Count);
        Assert.Same(table, table.WithAll([]));
        Assert.Equal(PlayerState.Playing, table.BySlot(0)!.State);
    }

    [Fact]
    public void Read_RejectsOutOfRangeEntries()
    {
        foreach (PlayerEntry bad in new[]
        {
            Entry(0, "A") with { Slot = 4 },
            Entry(0, "A") with { Tribe = -2 },
            Entry(0, "A") with { Colour = 8 },
            Entry(0, "A") with { State = (PlayerState)9 },
            Entry(0, new string('x', 300)),
        })
        {
            Assert.Throws<InvalidDataException>(() => Read(Write(new[] { bad })));
        }
        PlayerEntry twin = Entry(0, "A");
        Assert.Throws<InvalidDataException>(() => Read(Write(new[] { twin, twin with { Slot = 1 } })));
    }

    private static byte[] Write(PlayerTable table)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            table.Write(writer);
        }
        return stream.ToArray();
    }

    /// <summary>Writes entries without the table's checks (to make bad data).</summary>
    private static byte[] Write(PlayerEntry[] entries)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(entries.Length);
            foreach (PlayerEntry e in entries)
            {
                writer.Write(e.Id.ToByteArray());
                writer.Write(e.Name);
                writer.Write(e.Slot);
                writer.Write(e.Tribe);
                writer.Write(e.Colour);
                writer.Write(e.Team);
                writer.Write(e.IsComputer);
                writer.Write(e.AiLevel);
                writer.Write((byte)e.State);
            }
        }
        return stream.ToArray();
    }

    private static PlayerTable Read(byte[] data)
    {
        using var reader = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
        return PlayerTable.Read(reader);
    }
}
