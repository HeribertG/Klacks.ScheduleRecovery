// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleRecovery.Model;

/// <summary>
/// The deterministic result of a repair: the ordered reassignment deltas, any temporary cross-group
/// memberships (R4+), the slots left uncovered, the aggregated lexicographic objective and the highest
/// escalation tier reached. Two repairs of the same snapshot/absence/ruleset produce an equal proposal.
/// </summary>
/// <param name="Deltas">Ordered reassignment hops; for a swap chain the relocation hops precede the cover hop</param>
/// <param name="MembershipDeltas">Temporary cross-group memberships to materialise (empty in the in-group MVP)</param>
/// <param name="Uncovered">Slots deliberately left uncovered (locked / non-critical / no candidate)</param>
/// <param name="Objective">The aggregated lexicographic objective value of this proposal</param>
/// <param name="HighestTier">The highest escalation tier any delta or uncovered marker reached</param>
public sealed record RecoveryProposal(
    IReadOnlyList<CellDelta> Deltas,
    IReadOnlyList<MembershipDelta> MembershipDeltas,
    IReadOnlyList<UncoveredSlot> Uncovered,
    RecoveryObjective Objective,
    EscalationTier HighestTier)
{
    public static readonly RecoveryProposal Empty = new(
        [], [], [], RecoveryObjective.Zero, EscalationTier.InGroupFree);
}
