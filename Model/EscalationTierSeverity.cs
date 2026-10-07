// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleRecovery.Model;

/// <summary>
/// The escalation order of <see cref="EscalationTier"/>, independent of its append-only integer values:
/// in-group on-call &lt; in-group free &lt; cross-group on-call &lt; in-group swap &lt; cross-group free &lt;
/// cross-group swap &lt; uncovered. Every "highest tier reached" decision must compare through this rank.
/// </summary>
public static class EscalationTierSeverity
{
    private const int InGroupOnCallRank = 0;
    private const int InGroupFreeRank = 1;
    private const int CrossGroupOnCallRank = 2;
    private const int InGroupSwapRank = 3;
    private const int CrossGroupFreeRank = 4;
    private const int CrossGroupSwapRank = 5;
    private const int UncoveredRank = 6;

    /// <summary>Rank of a tier in the escalation order; larger means the search had to go further.</summary>
    /// <param name="tier">The tier to rank</param>
    public static int Rank(EscalationTier tier) => tier switch
    {
        EscalationTier.InGroupOnCall => InGroupOnCallRank,
        EscalationTier.InGroupFree => InGroupFreeRank,
        EscalationTier.CrossGroupOnCall => CrossGroupOnCallRank,
        EscalationTier.InGroupSwap => InGroupSwapRank,
        EscalationTier.CrossGroupFree => CrossGroupFreeRank,
        EscalationTier.CrossGroupSwap => CrossGroupSwapRank,
        EscalationTier.Uncovered => UncoveredRank,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown escalation tier.")
    };

    /// <summary>The tier of the two that lies further along the escalation order.</summary>
    /// <param name="a">First tier</param>
    /// <param name="b">Second tier</param>
    public static EscalationTier Max(EscalationTier a, EscalationTier b) => Rank(b) > Rank(a) ? b : a;

    /// <summary>
    /// The tier a repair result reached: Uncovered when any slot stayed open, otherwise the tier furthest along
    /// the escalation order among the hops, and InGroupFree when there are none.
    /// </summary>
    /// <param name="tiers">Tiers of the accepted hops</param>
    /// <param name="anyUncovered">True when at least one slot stayed uncovered</param>
    public static EscalationTier Highest(IEnumerable<EscalationTier> tiers, bool anyUncovered)
        => anyUncovered
            ? EscalationTier.Uncovered
            : tiers.Aggregate((EscalationTier?)null, (current, tier) => current is { } c ? Max(c, tier) : tier)
              ?? EscalationTier.InGroupFree;
}
