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
/// Longest swap chain in moves, the cover move included (default 3); below 2 no chain is built. Chains of two
/// moves are always searched. A longer chain is only searched for a demand that no direct cover and no shorter
/// chain covers, so fewer moves always win.
/// </param>
/// <param name="WeightInGroupFree">Perturbation weight of a direct in-group reassignment hop</param>
/// <param name="WeightInGroupSwap">Perturbation weight of an in-group swap relocation hop</param>
/// <param name="WeightCrossGroupFree">Perturbation weight of a cross-group direct hop (R4)</param>
/// <param name="WeightCrossGroupSwap">Perturbation weight of a cross-group swap hop (R4)</param>
/// <param name="WeightInGroupOnCall">Perturbation weight of a direct in-group hop to an agent on call that day</param>
/// <param name="WeightCrossGroupOnCall">Perturbation weight of a direct cross-group hop to an agent on call that day</param>
/// <param name="LookaheadBeamWidth">
/// Partial repairs carried along over the demands of one absence (default 8; 1 = the plain greedy repair, one demand after the
/// other). Above 1 the engine plans the whole absence: it keeps the best <c>LookaheadBeamWidth</c> partial repairs,
/// each extended by its best <see cref="LookaheadBranching"/> options per demand, and takes the lookahead result only
/// when it is strictly better as a whole than the greedy one (uncovered critical slots, violations, perturbation,
/// rhythm, shift wish - the same order as one option), so the result is never worse than the greedy repair.
/// </param>
/// <param name="LookaheadBranching">Options per demand and partial repair the lookahead tries (only with a beam &gt; 1).</param>
/// <param name="ReliefReachDays">
/// Days before and after the demand day in which an agent of a chain may give up one of its own works (default 7). Kept
/// well inside the context window of the snapshot, so the rest-time, consecutive-days and weekly checks around a
/// relief still see the neighbouring days.
/// </param>
/// <param name="DeepChainProbeBudget">
/// Trials (one agent or one relief each) a chain search of more than two moves may spend per demand, shared evenly
/// among the in-group agents that could cover it. A count, not a time limit, so the result does not depend on the machine.
/// </param>
public sealed record Ruleset(
    int MaxSwapChainDepth = RulesetDefaults.MaxSwapChainDepth,
    int WeightInGroupFree = RulesetDefaults.WeightInGroupFree,
    int WeightInGroupSwap = RulesetDefaults.WeightInGroupSwap,
    int WeightCrossGroupFree = RulesetDefaults.WeightCrossGroupFree,
    int WeightCrossGroupSwap = RulesetDefaults.WeightCrossGroupSwap,
    int WeightInGroupOnCall = RulesetDefaults.WeightInGroupOnCall,
    int WeightCrossGroupOnCall = RulesetDefaults.WeightCrossGroupOnCall,
    int LookaheadBeamWidth = RulesetDefaults.LookaheadBeamWidth,
    int LookaheadBranching = RulesetDefaults.LookaheadBranching,
    int ReliefReachDays = RulesetDefaults.ReliefReachDays,
    int DeepChainProbeBudget = RulesetDefaults.DeepChainProbeBudget)
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
