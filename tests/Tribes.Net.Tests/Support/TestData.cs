using Tribes.Net.Protocol;

namespace Tribes.Net.Tests.Support;

internal static class TestData
{
    /// <summary>A ledger with no slot holders and no commands run.</summary>
    public static readonly LedgerState NoLedger = new([], [0, 0, 0, 0]);
}
