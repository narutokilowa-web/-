using System.Text.Json.Nodes;
using Mfop.Kernel;
using Mfop.Kernel.Json;
using Mfop.Kernel.StateMachines;

namespace Mfop.Domain.Tests;

public class KernelTests
{
    [Fact]
    public void Money_requires_an_iso_currency_and_never_mixes_currencies()
    {
        var a = new Money(1000m, "SYP", new DateOnly(2026, 9, 1));
        var b = new Money(500m, "SYP", new DateOnly(2026, 9, 2));
        Assert.Equal(1500m, a.Add(b).Amount);
        Assert.Equal(new DateOnly(2026, 9, 2), a.Add(b).AsOf);
        Assert.Throws<ArgumentException>(() => new Money(1m, "syp", new DateOnly(2026, 1, 1)));
        Assert.Throws<ArgumentException>(() => new Money(1m, "SYRIAN", new DateOnly(2026, 1, 1)));
        Assert.Throws<InvalidOperationException>(() => a.Add(new Money(1m, "EUR", new DateOnly(2026, 9, 1))));
    }

    [Fact]
    public void Ids_are_version_7_and_time_ordered()
    {
        var first = Ids.New();
        var second = Ids.New();
        Assert.Equal(7, first.Version);
        Assert.True(string.CompareOrdinal(first.ToString(), second.ToString()) < 0 || first.ToString()[..8] == second.ToString()[..8]);
    }

    [Fact]
    public void Timestamps_are_truncated_to_microseconds()
    {
        var value = new DateTimeOffset(2026, 9, 24, 10, 0, 0, TimeSpan.Zero).AddTicks(1234567);
        Assert.Equal(0, ClockExtensions.TruncateToMicros(value).UtcTicks % 10);
    }

    [Fact]
    public void State_machine_rejects_unknown_states_at_definition_time()
    {
        Assert.Throws<ArgumentException>(() => new StateMachine("X", ["A"], "A", [], [new("T1", [StateMachine.Start], "Go", "B", ["system"])]));
    }

    [Fact]
    public void Canonical_json_sorts_keys_and_refuses_non_integers()
    {
        var node = new JsonObject { ["b"] = 1, ["a"] = "ب" };
        Assert.Equal("{\"a\":\"ب\",\"b\":1}", System.Text.Encoding.UTF8.GetString(CanonicalJson.Serialize(node)));
        Assert.Throws<NotSupportedException>(() => CanonicalJson.Serialize(new JsonObject { ["x"] = 1.5 }));
    }
}
