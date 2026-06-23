// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleRecovery.Model;

/// <summary>
/// Tunable parameters of the repair search. The perturbation weights realise the lexicographic
/// w_type ordering "in-group-free &lt; in-group-swap &lt; cross-group-free &lt; cross-group-swap" as
/// integers so the objective never compares floats in its decision path. Defaults are chosen so a
/// clean direct cover is always cheaper than any swap, and any in-group move is cheaper than any
/// cross-group move.
/// </summary>
/// <param name="MaxSwapChainDepth">
/// Enables swap chains when &gt;= 2 (O2 default 2). v1 performs exactly one depth-2 chain (one relocation +
/// one cover); a value &gt; 2 does NOT deepen the search yet — deeper chains are a v2 extension.
/// </param>
/// <param name="WeightInGroupFree">Perturbation weight of a direct in-group reassignment hop</param>
/// <param name="WeightInGroupSwap">Perturbation weight of an in-group swap relocation hop</param>
/// <param name="WeightCrossGroupFree">Perturbation weight of a cross-group direct hop (R4)</param>
/// <param name="WeightCrossGroupSwap">Perturbation weight of a cross-group swap hop (R4)</param>
public sealed record Ruleset(
    int MaxSwapChainDepth = RulesetDefaults.MaxSwapChainDepth,
    int WeightInGroupFree = RulesetDefaults.WeightInGroupFree,
    int WeightInGroupSwap = RulesetDefaults.WeightInGroupSwap,
    int WeightCrossGroupFree = RulesetDefaults.WeightCrossGroupFree,
    int WeightCrossGroupSwap = RulesetDefaults.WeightCrossGroupSwap)
{
    public static readonly Ruleset Default = new();

    /// <summary>Maps an escalation tier to the per-hop perturbation weight used by the objective.</summary>
    public int WeightOf(EscalationTier tier) => tier switch
    {
        EscalationTier.InGroupFree => WeightInGroupFree,
        EscalationTier.InGroupSwap => WeightInGroupSwap,
        EscalationTier.CrossGroupFree => WeightCrossGroupFree,
        EscalationTier.CrossGroupSwap => WeightCrossGroupSwap,
        _ => 0
    };
}
