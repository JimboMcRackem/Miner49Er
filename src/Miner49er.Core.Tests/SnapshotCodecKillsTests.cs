using System.Collections.Generic;
using Miner49er.Core;
using Miner49er.Core.Net;
using Xunit;

public class SnapshotCodecKillsTests
{
    [Fact]
    public void Round_trips_the_miner_kill_count()
    {
        var update = new TickUpdate(
            new WorldSnapshot(
                Tick: 1,
                Miners: new List<MinerSnapshot>
                {
                    new(1, 0, 0, 0, true, 0, 0, 0.0, 0.1, 5, -1, Kills: 7),
                    new(2, 1, 1, 0, true, 0, 0, 0.0, 0.1, 5, -1, Kills: 0),
                },
                Charges: new List<ChargeSnapshot>(),
                Items: new List<ItemSnapshot>(),
                Molds: new List<MoldSnapshot>(),
                Monsters: new List<MonsterSnapshot>(),
                SecondsRemaining: 10f,
                EscapeOpen: false),
            TileChanges: new List<TileChange>());

        TickUpdate back = SnapshotCodec.Read(SnapshotCodec.Write(update));

        Assert.Equal(7, back.Snapshot.Miners[0].Kills);
        Assert.Equal(0, back.Snapshot.Miners[1].Kills);
    }
}
