// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleRecovery.Model;

/// <summary>
/// Tunable parameters of the repair search. The perturbation weights realise the lexicographic
/// w_type ordering "in-group-on-call &lt; in-group-free &lt; cross-group-on-call &lt; in-group-swap &lt;
/// cross-group-free &lt; cross-group-swap" as integers so the objective never compares floats in its
/// decision path. Defaults are chosen so an on-call cover beats the free cover of the same group scope,
/// any direct cover except a cross-group free one is cheaper than a swap (a swap option costs two
/// relocation hops), and any in-group move is cheaper than a cross-group free or swap move.
/// </summary>
/// <param name="MaxSwapChainDepth">
/// Enables swap chains when &gt;= 2 (O2 default 2). v1 performs exactly one depth-2 chain (one relocation +
/// one cover); a value &gt; 2 does NOT deepen the search yet — deeper chains are a v2 extension.
/// </param>
/// <param name="WeightInGroupFree">Perturbation weight of a direct in-group reassignment hop</param>
/// <param name="WeightInGroupSwap">Perturbation weight of an in-group swap relocation hop</param>
/// <param name="WeightCrossGroupFree">Perturbation weight of a cross-group direct hop (R4)</param>
/// <param name="WeightCrossGroupSwap">Perturbation weight of a cross-group swap hop (R4)</param>
/// <param name="WeightInGroupOnCall">Perturbation weight of a direct in-group hop to an agent on call that day</param>
/// <param name="WeightCrossGroupOnCall">Perturbation weight of a direct cross-group hop to an agent on call that day</param>
public sealed record Ruleset(
    int MaxSwapChainDepth = RulesetDefaults.MaxSwapChainDepth,
    int WeightInGroupFree = RulesetDefaults.WeightInGroupFree,
    int WeightInGroupSwap = RulesetDefaults.WeightInGroupSwap,
    int WeightCrossGroupFree = RulesetDefaults.WeightCrossGroupFree,
    int WeightCrossGroupSwap = RulesetDefaults.WeightCrossGroupSwap,
    int WeightInGroupOnCall = RulesetDefaults.WeightInGroupOnCall,
    int WeightCrossGroupOnCall = RulesetDefaults.WeightCrossGroupOnCall)
{
    public static readonly Ruleset Default = new();

    /// <summary>Maps an escalation tier to the per-hop perturbation weight used by the objective.</summary>
    public int WeightOf(EscalationTier tier) => tier switch
    {
        EscalationTier.InGroupOnCall => WeightInGroupOnCall,
        EscalationTier.InGroupFree => WeightInGroupFree,
        EscalationTier.CrossGroupOnCall => WeightCrossGroupOnCall,
        EscalationTier.InGroupSwap => WeightInGroupSwap,
        EscalationTier.CrossGroupFree => WeightCrossGroupFree,
        EscalationTier.CrossGroupSwap => WeightCrossGroupSwap,
        _ => 0
    };
}
