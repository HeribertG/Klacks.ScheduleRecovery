// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleRecovery.Model;

namespace Klacks.ScheduleRecovery.Engine;

/// <summary>
/// The deterministic in-group local-repair engine (escalation tiers 0, 1 and the uncovered fallback).
/// It collects every working, non-locked work the absent agent held (split shifts included) as a demand,
/// taken in a total order (date, start, shift id). For each demand it enumerates every direct cover and
/// every depth-bounded swap chain over the Guid-ordered candidate pool plus the leave-uncovered option,
/// then commits the single lexicographically optimal option (<see cref="OptionKey"/>). Collision is judged
/// by time-interval overlap, so a candidate free in a different window of the same day is a valid direct
/// cover. The search clones the working grid before trying any move, never mutates the snapshot, uses no
/// clock or random source and never iterates a hash-ordered collection in the decision path — so equal
/// inputs always yield an equal proposal. Coverage is a penalty, never a hard constraint, so the engine
/// always returns a proposal.
/// </summary>
/// <remarks>
/// Non-critical slots: by design the minimal-perturbation optimum leaves a non-critical slot uncovered
/// (coverage cost 0, perturbation 0) rather than spend any reassignment on it. In v1 every slot is marked
/// critical (see <see cref="Ruleset"/> / O3), so this path is dormant; a future per-slot criticality
/// policy can revisit whether a free clean candidate should still fill a non-critical slot.
/// </remarks>
public sealed class LocalRepairEngine : IRecoveryEngine
{
    private static readonly Guid MaxGuid = new("ffffffff-ffff-ffff-ffff-ffffffffffff");

    public RecoveryProposal Repair(RecoverySnapshot snapshot, AbsenceEvent absence, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(absence);
        ArgumentNullException.ThrowIfNull(ruleset);

        var grid = WorkingGrid.From(snapshot);
        var uncovered = new List<UncoveredSlot>();
        var demands = CollectDemands(snapshot, absence, grid, uncovered);

        if (demands.Count == 0 && uncovered.Count == 0)
        {
            return RecoveryProposal.Empty;
        }

        var deltas = new List<CellDelta>();
        var memberships = new List<MembershipDelta>();
        var introducedViolations = 0;
        foreach (var demand in demands)
        {
            var option = SelectBestOption(snapshot, grid, absence.AgentId, demand, ruleset);
            Commit(grid, option);

            if (option.Covers)
            {
                foreach (var move in option.Moves)
                {
                    deltas.Add(move.ToDelta());
                }
                introducedViolations += option.IntroducedViolations;
                if (option.Membership is { } membership)
                {
                    memberships.Add(membership);
                }
            }
            else
            {
                uncovered.Add(new UncoveredSlot(
                    demand.ShiftId, demand.Date, option.UncoveredReason, demand.IsCritical, WorkIdsOf(demand.Work)));
            }
        }

        var objective = Aggregate(deltas, uncovered, ruleset, absence.AgentId, introducedViolations);
        var highestTier = HighestTier(deltas, uncovered);
        return new RecoveryProposal(deltas, MergeMemberships(memberships), uncovered, objective, highestTier);
    }

    private static List<DemandSlot> CollectDemands(
        RecoverySnapshot snapshot,
        AbsenceEvent absence,
        WorkingGrid grid,
        List<UncoveredSlot> uncovered)
    {
        var demands = new List<DemandSlot>();
        foreach (var date in absence.Dates.OrderBy(d => d.DayNumber))
        {
            var critical = snapshot.IsCritical(absence.AgentId, date);
            foreach (var work in snapshot.GetWorks(absence.AgentId, date))
            {
                if (!work.IsWorking)
                {
                    continue;
                }
                if (work.IsLocked)
                {
                    uncovered.Add(new UncoveredSlot(
                        work.ShiftId, date, RecoveryReasons.Locked, critical, WorkIdsOf(work)));
                    continue;
                }

                demands.Add(new DemandSlot(absence.AgentId, date, work, critical));
                grid.RemoveWork(absence.AgentId, date, work);
            }
        }

        demands.Sort(CompareDemands);
        return demands;
    }

    private static int CompareDemands(DemandSlot a, DemandSlot b)
    {
        var byDate = a.Date.DayNumber.CompareTo(b.Date.DayNumber);
        if (byDate != 0)
        {
            return byDate;
        }
        var byStart = a.Work.StartAt.CompareTo(b.Work.StartAt);
        if (byStart != 0)
        {
            return byStart;
        }
        return (a.ShiftId ?? Guid.Empty).CompareTo(b.ShiftId ?? Guid.Empty);
    }

    private static RepairOption SelectBestOption(
        RecoverySnapshot snapshot,
        WorkingGrid grid,
        Guid absentId,
        DemandSlot demand,
        Ruleset ruleset)
    {
        RepairOption? best = null;
        var anyCover = false;

        foreach (var candidate in snapshot.Agents)
        {
            var direct = BuildDirectOption(snapshot, grid, absentId, demand, ruleset, candidate);
            if (direct is not null)
            {
                anyCover = true;
                best = Better(best, direct);
            }
        }

        if (ruleset.MaxSwapChainDepth >= 2)
        {
            foreach (var blocked in snapshot.Agents)
            {
                var swap = BuildSwapOption(snapshot, grid, absentId, demand, ruleset, blocked);
                if (swap is not null)
                {
                    anyCover = true;
                    best = Better(best, swap);
                }
            }
        }

        best = Better(best, BuildUncoveredOption(demand, anyCover));
        return best!;
    }

    private static RepairOption? BuildDirectOption(
        RecoverySnapshot snapshot,
        WorkingGrid grid,
        Guid absentId,
        DemandSlot demand,
        Ruleset ruleset,
        RecoveryAgent candidate)
    {
        var placed = demand.AsAssignment();
        if (candidate.Id == absentId
            || !LegalityEvaluator.TargetIsPlaceable(grid, candidate.Id, demand.Date, placed)
            || LegalityEvaluator.IsGatedByContract(snapshot, candidate, demand.Category, demand.ShiftId, demand.Date))
        {
            return null;
        }

        var branch = grid.Branch();
        branch.AddWork(candidate.Id, demand.Date, placed);
        var violations = LegalityEvaluator.CountViolations(branch, candidate, demand.Date, placed);

        var tier = candidate.IsInGroup ? EscalationTier.InGroupFree : EscalationTier.CrossGroupFree;
        var move = new WorkMove(placed, null, absentId, candidate.Id, demand.Date, tier);
        WorkMove[] moves = [move];

        var key = new OptionKey(
            Coverage: 0,
            Violations: violations,
            Perturbation: Perturbation(moves, ruleset),
            PreferenceRank: IsPreferred(candidate, demand.ShiftId) ? 0 : 1,
            TargetHoursDeficit: candidate.TargetHoursDeficit,
            PrimaryGuid: candidate.Id,
            SecondaryGuid: Guid.Empty);

        var membership = candidate.IsInGroup
            ? null
            : new MembershipDelta(candidate.Id, snapshot.ReceivingGroupId, demand.Date, demand.Date);
        return new RepairOption(key, true, moves, violations, string.Empty, membership);
    }

    private static RepairOption? BuildSwapOption(
        RecoverySnapshot snapshot,
        WorkingGrid grid,
        Guid absentId,
        DemandSlot demand,
        Ruleset ruleset,
        RecoveryAgent blocked)
    {
        var demandWork = demand.AsAssignment();
        // v1 swap chains stay in-group: a cross-group swap would need two temporary memberships in one
        // chain (tier 3, deferred). Cross-group is covered by the direct path (tier 2) only.
        if (blocked.Id == absentId || !blocked.IsInGroup
            || LegalityEvaluator.IsGatedByContract(snapshot, blocked, demand.Category, demand.ShiftId, demand.Date))
        {
            return null;
        }

        var displaced = SingleOverlappingMovableWork(grid, blocked.Id, demand);
        if (displaced is null)
        {
            return null;
        }

        RepairOption? best = null;
        foreach (var recipient in snapshot.Agents)
        {
            if (recipient.Id == absentId || recipient.Id == blocked.Id || !recipient.IsInGroup
                || !LegalityEvaluator.TargetIsPlaceable(grid, recipient.Id, demand.Date, displaced)
                || LegalityEvaluator.IsGatedByContract(snapshot, recipient, displaced.Category, displaced.ShiftId, demand.Date))
            {
                continue;
            }

            var branch = grid.Branch();
            branch.RemoveWork(blocked.Id, demand.Date, displaced);
            branch.AddWork(recipient.Id, demand.Date, displaced);
            branch.AddWork(blocked.Id, demand.Date, demandWork);

            var violations = LegalityEvaluator.CountViolations(branch, recipient, demand.Date, displaced)
                + LegalityEvaluator.CountViolations(branch, blocked, demand.Date, demandWork);

            var relocation = new WorkMove(
                displaced, displaced, blocked.Id, recipient.Id, demand.Date, EscalationTier.InGroupSwap);
            var cover = new WorkMove(
                demandWork, null, absentId, blocked.Id, demand.Date, EscalationTier.InGroupSwap);
            WorkMove[] moves = [relocation, cover];

            var key = new OptionKey(
                Coverage: 0,
                Violations: violations,
                Perturbation: Perturbation(moves, ruleset),
                PreferenceRank: IsPreferred(blocked, demand.ShiftId) ? 0 : 1,
                TargetHoursDeficit: blocked.TargetHoursDeficit,
                PrimaryGuid: blocked.Id,
                SecondaryGuid: recipient.Id);

            var option = new RepairOption(key, true, moves, violations, string.Empty);
            best = Better(best, option);
        }

        return best;
    }

    /// <summary>
    /// Returns the single working, non-locked work of the blocked agent whose interval overlaps the demand,
    /// or null when there is none (agent is free, handled as a direct cover) or more than one (would need a
    /// deeper chain than the depth-2 bound).
    /// </summary>
    private static RecoveryWork? SingleOverlappingMovableWork(WorkingGrid grid, Guid blockedId, DemandSlot demand)
    {
        RecoveryWork? overlapping = null;
        foreach (var work in grid.Get(blockedId, demand.Date))
        {
            if (!work.OverlapsInterval(demand.Work.StartAt, demand.Work.EndAt))
            {
                continue;
            }
            if (!work.IsWorking || work.IsLocked || overlapping is not null)
            {
                return null;
            }
            overlapping = work;
        }
        return overlapping;
    }

    private static RepairOption BuildUncoveredOption(DemandSlot demand, bool anyCover)
    {
        var key = new OptionKey(
            Coverage: demand.IsCritical ? 1 : 0,
            Violations: 0,
            Perturbation: 0,
            PreferenceRank: int.MaxValue,
            TargetHoursDeficit: decimal.MinValue,
            PrimaryGuid: MaxGuid,
            SecondaryGuid: MaxGuid);

        var reason = anyCover ? RecoveryReasons.NonCritical : RecoveryReasons.NoEligibleCandidate;
        return new RepairOption(key, false, [], 0, reason);
    }

    private static RepairOption Better(RepairOption? current, RepairOption candidate)
        => current is null || candidate.Key.CompareTo(current.Key) < 0 ? candidate : current;

    private static int Perturbation(IReadOnlyList<WorkMove> moves, Ruleset ruleset)
        => moves.Sum(m => ruleset.WeightOf(m.Tier));

    /// <summary>
    /// Collapses per-slot cross-group memberships into one per (agent, group) spanning the full borrowing
    /// window (earliest ValidFrom .. latest ValidUntil), ordered by agent id for determinism.
    /// </summary>
    private static IReadOnlyList<MembershipDelta> MergeMemberships(IReadOnlyList<MembershipDelta> memberships)
    {
        if (memberships.Count == 0)
        {
            return [];
        }
        return memberships
            .GroupBy(m => (m.AgentId, m.GroupId))
            .Select(g => new MembershipDelta(
                g.Key.AgentId,
                g.Key.GroupId,
                g.Min(m => m.ValidFrom),
                g.Max(m => m.ValidUntil)))
            .OrderBy(m => m.AgentId)
            .ToList();
    }

    private static void Commit(WorkingGrid grid, RepairOption option)
    {
        if (!option.Covers)
        {
            return;
        }
        foreach (var move in option.Moves)
        {
            if (move.Removed is not null)
            {
                grid.RemoveWork(move.FromAgentId, move.Date, move.Removed);
            }
        }
        foreach (var move in option.Moves)
        {
            grid.AddWork(move.ToAgentId, move.Date, move.Placed);
        }
    }

    private static RecoveryObjective Aggregate(
        IReadOnlyList<CellDelta> deltas,
        IReadOnlyList<UncoveredSlot> uncovered,
        Ruleset ruleset,
        Guid absentId,
        int introducedViolations)
    {
        var uncoveredCritical = uncovered.Count(u => u.IsCritical);
        var perturbation = deltas.Sum(d => ruleset.WeightOf(d.Tier));

        var load = new Dictionary<Guid, int>();
        foreach (var delta in deltas)
        {
            Increment(load, delta.ToAgentId);
            if (delta.FromAgentId != absentId)
            {
                Increment(load, delta.FromAgentId);
            }
        }
        var worstOff = load.Count == 0 ? 0 : load.Values.Max();

        return new RecoveryObjective(uncoveredCritical, introducedViolations, perturbation, worstOff);
    }

    private static void Increment(Dictionary<Guid, int> load, Guid agentId)
        => load[agentId] = load.TryGetValue(agentId, out var current) ? current + 1 : 1;

    private static EscalationTier HighestTier(
        IReadOnlyList<CellDelta> deltas, IReadOnlyList<UncoveredSlot> uncovered)
    {
        var highest = EscalationTier.InGroupFree;
        foreach (var delta in deltas)
        {
            if (delta.Tier > highest)
            {
                highest = delta.Tier;
            }
        }
        if (uncovered.Count > 0 && EscalationTier.Uncovered > highest)
        {
            highest = EscalationTier.Uncovered;
        }
        return highest;
    }

    private static bool IsPreferred(RecoveryAgent agent, Guid? shiftId)
        => shiftId is Guid id && agent.PreferredShiftIds.Contains(id);

    private static IReadOnlyList<Guid> WorkIdsOf(RecoveryWork work)
        => work.WorkIds ?? [];
}
