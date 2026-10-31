// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleRecovery.Model;

namespace Klacks.ScheduleRecovery.Engine;

/// <summary>
/// The deterministic local-repair engine (direct on-call / free covers, in-group swaps, cross-group direct
/// covers and the uncovered fallback; an agent on call on the demand day is the preferred direct cover).
/// It collects every working, non-locked work the absent agent held (split shifts included) as a demand,
/// taken in a total order (date, start, shift id). For each demand it enumerates every direct cover and
/// every depth-bounded swap chain over the Guid-ordered candidate pool plus the leave-uncovered option,
/// then commits the single lexicographically optimal option (<see cref="OptionKey"/>). Among options of equal
/// coverage, legality and perturbation the one that keeps the rhythm of the touched rows wins
/// (<see cref="RowHarmony"/>), so a cover extends a fitting block instead of tearing a free block apart. Collision is judged
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

        // Greedy repair: one demand after the other, each committing its single best option.
        var chosen = Search(snapshot, absence.AgentId, demands, grid, ruleset, beamWidth: 1, branching: 1);
        if (ruleset.LookaheadBeamWidth > 1 && demands.Count > 1)
        {
            // Lookahead over the whole absence; it replaces the greedy repair only when strictly better as a whole.
            var lookahead = Search(
                snapshot, absence.AgentId, demands, grid, ruleset, ruleset.LookaheadBeamWidth, Math.Max(1, ruleset.LookaheadBranching));
            if (lookahead.Key.CompareTo(chosen.Key) < 0)
            {
                chosen = lookahead;
            }
        }

        return BuildProposal(chosen.Options, demands, uncovered, ruleset, absence.AgentId);
    }

    /// <summary>
    /// Beam search over the demands in their total order. Every partial repair (path) is extended by its best
    /// <paramref name="branching"/> options for the next demand; the best <paramref name="beamWidth"/> paths survive,
    /// ranked by <see cref="PathKey"/>. Width 1 and branching 1 is exactly the greedy repair.
    /// </summary>
    private static RepairPath Search(
        RecoverySnapshot snapshot,
        Guid absentId,
        IReadOnlyList<DemandSlot> demands,
        WorkingGrid grid,
        Ruleset ruleset,
        int beamWidth,
        int branching)
    {
        var beam = new List<RepairPath> { RepairPath.Start(grid.Branch()) };
        foreach (var demand in demands)
        {
            var next = new List<RepairPath>(beam.Count * branching);
            foreach (var path in beam)
            {
                var options = RankedOptions(snapshot, path.Grid, absentId, demand, ruleset, path.AddedPeriodHours);
                for (var rank = 0; rank < options.Count && rank < branching; rank++)
                {
                    next.Add(path.Extend(options[rank], rank, demand, beamWidth == 1 && branching == 1));
                }
            }

            beam = next
                .OrderBy(p => p.Key)
                .Take(beamWidth)
                .ToList();
        }

        return beam[0];
    }

    private static RecoveryProposal BuildProposal(
        IReadOnlyList<RepairOption> options,
        IReadOnlyList<DemandSlot> demands,
        List<UncoveredSlot> uncovered,
        Ruleset ruleset,
        Guid absentId)
    {
        var deltas = new List<CellDelta>();
        var memberships = new List<MembershipDelta>();
        var introducedViolations = 0;
        var harmonyDelta = 0;

        // Every demand yields at most one committed option; the counter groups that option's hops so a
        // swap chain travels through the caller's compliance gate as one unit.
        for (var optionId = 0; optionId < demands.Count; optionId++)
        {
            var option = options[optionId];
            var demand = demands[optionId];
            if (option.Covers)
            {
                foreach (var move in option.Moves)
                {
                    deltas.Add(move.ToDelta(optionId));
                }
                introducedViolations += option.IntroducedViolations;
                harmonyDelta += option.Key.Harmony;
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

        var objective = Aggregate(deltas, uncovered, ruleset, absentId, introducedViolations);
        var highestTier = HighestTier(deltas, uncovered);
        return new RecoveryProposal(deltas, MergeMemberships(memberships), uncovered, objective, highestTier)
        {
            HarmonyDelta = harmonyDelta,
        };
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

    /// <summary>
    /// Every option for one demand, best first: the direct covers, the best swap per blocked agent and the
    /// leave-uncovered option. The first entry is the option the greedy repair commits (stable order on ties).
    /// </summary>
    private static List<RepairOption> RankedOptions(
        RecoverySnapshot snapshot,
        WorkingGrid grid,
        Guid absentId,
        DemandSlot demand,
        Ruleset ruleset,
        IReadOnlyDictionary<Guid, decimal> addedPeriodHours)
    {
        var options = new List<RepairOption>();
        var anyCover = false;

        foreach (var candidate in snapshot.Agents)
        {
            var direct = BuildDirectOption(snapshot, grid, absentId, demand, ruleset, candidate, addedPeriodHours);
            if (direct is not null)
            {
                anyCover = true;
                options.Add(direct);
            }
        }

        if (ruleset.MaxSwapChainDepth >= 2)
        {
            foreach (var blocked in snapshot.Agents)
            {
                var swap = BuildSwapOption(snapshot, grid, absentId, demand, ruleset, blocked, addedPeriodHours);
                if (swap is not null)
                {
                    anyCover = true;
                    options.Add(swap);
                }
            }
        }

        options.Add(BuildUncoveredOption(demand, anyCover));
        return options.OrderBy(o => o.Key).ToList();
    }

    private static RepairOption? BuildDirectOption(
        RecoverySnapshot snapshot,
        WorkingGrid grid,
        Guid absentId,
        DemandSlot demand,
        Ruleset ruleset,
        RecoveryAgent candidate,
        IReadOnlyDictionary<Guid, decimal> addedPeriodHours)
    {
        var placed = demand.AsAssignment();
        if (candidate.Id == absentId
            || !LegalityEvaluator.TargetIsPlaceable(grid, candidate.Id, demand.Date, placed)
            || LegalityEvaluator.IsGatedByContract(snapshot, candidate, demand.Category, demand.ShiftId, demand.Date)
            || ExceedsMaximumHours(candidate, AddedOf(addedPeriodHours, candidate.Id), placed.Hours))
        {
            return null;
        }

        var branch = grid.Branch();
        branch.AddWork(candidate.Id, demand.Date, placed);
        var violations = LegalityEvaluator.CountViolations(branch, candidate, demand.Date, placed);
        if (violations > 0)
        {
            // Hard-reject: a candidate that would introduce any hard violation (min-pause, consecutive,
            // weekly or daily hours) is not a legal cover and is excluded — never committed. This keeps
            // the recovery output in agreement with the Wizard Stage-0 gate.
            return null;
        }

        var tier = DirectTierOf(candidate, snapshot.GetAvailability(candidate.Id, demand.Date).IsOnCall);
        var move = new WorkMove(placed, null, absentId, candidate.Id, demand.Date, tier);
        WorkMove[] moves = [move];

        var key = new OptionKey(
            Coverage: 0,
            Violations: violations,
            Perturbation: Perturbation(moves, ruleset),
            Harmony: RowHarmony.Delta(grid, branch, candidate.Id, demand.Date),
            PreferenceRank: IsPreferred(candidate, demand.ShiftId) ? 0 : 1,
            TargetHoursDeficit: candidate.TargetHoursDeficit,
            PrimaryGuid: candidate.Id,
            SecondaryGuid: Guid.Empty);

        var membership = candidate.IsInGroup
            ? null
            : new MembershipDelta(candidate.Id, snapshot.ReceivingGroupId, demand.Date, demand.Date);
        return new RepairOption(key, true, moves, violations, string.Empty, membership);
    }

    /// <summary>
    /// Tier of a direct cover: an agent on call that day is the preferred replacement within its group
    /// scope, ahead of a merely free agent of the same scope.
    /// </summary>
    private static EscalationTier DirectTierOf(RecoveryAgent candidate, bool isOnCall) => (candidate.IsInGroup, isOnCall) switch
    {
        (true, true) => EscalationTier.InGroupOnCall,
        (true, false) => EscalationTier.InGroupFree,
        (false, true) => EscalationTier.CrossGroupOnCall,
        (false, false) => EscalationTier.CrossGroupFree,
    };

    private static RepairOption? BuildSwapOption(
        RecoverySnapshot snapshot,
        WorkingGrid grid,
        Guid absentId,
        DemandSlot demand,
        Ruleset ruleset,
        RecoveryAgent blocked,
        IReadOnlyDictionary<Guid, decimal> addedPeriodHours)
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

        // Once `displaced` is relocated, `blocked` takes over the demand work. Verify on a branch with
        // `displaced` removed that `blocked` can actually host the demand without a residual collision —
        // e.g. a cross-midnight neighbour on an adjacent day that the single-day `displaced` scan never
        // sees. Without this gate the swap would double-book `blocked` (a physically impossible plan that
        // CountViolations does not catch, mirroring the time-overlap gate the direct path already applies).
        var blockedProbe = grid.Branch();
        blockedProbe.RemoveWork(blocked.Id, demand.Date, displaced);
        if (!LegalityEvaluator.TargetIsPlaceable(blockedProbe, blocked.Id, demand.Date, demandWork))
        {
            return null;
        }

        // blocked gives up `displaced` (already in its period baseline) and takes the demand work: gate the
        // net period-hours change against its MaximumHours cap.
        if (ExceedsMaximumHours(blocked, AddedOf(addedPeriodHours, blocked.Id) - displaced.Hours, demandWork.Hours))
        {
            return null;
        }

        RepairOption? best = null;
        foreach (var recipient in snapshot.Agents)
        {
            if (recipient.Id == absentId || recipient.Id == blocked.Id || !recipient.IsInGroup
                || !LegalityEvaluator.TargetIsPlaceable(grid, recipient.Id, demand.Date, displaced)
                || LegalityEvaluator.IsGatedByContract(snapshot, recipient, displaced.Category, displaced.ShiftId, demand.Date)
                || ExceedsMaximumHours(recipient, AddedOf(addedPeriodHours, recipient.Id), displaced.Hours))
            {
                continue;
            }

            var branch = grid.Branch();
            branch.RemoveWork(blocked.Id, demand.Date, displaced);
            branch.AddWork(recipient.Id, demand.Date, displaced);
            branch.AddWork(blocked.Id, demand.Date, demandWork);

            var violations = LegalityEvaluator.CountViolations(branch, recipient, demand.Date, displaced)
                + LegalityEvaluator.CountViolations(branch, blocked, demand.Date, demandWork);
            if (violations > 0)
            {
                // Hard-reject: neither half of the swap may introduce a hard violation.
                continue;
            }

            var relocation = new WorkMove(
                displaced, displaced, blocked.Id, recipient.Id, demand.Date, EscalationTier.InGroupSwap);
            var cover = new WorkMove(
                demandWork, null, absentId, blocked.Id, demand.Date, EscalationTier.InGroupSwap);
            WorkMove[] moves = [relocation, cover];

            var key = new OptionKey(
                Coverage: 0,
                Violations: violations,
                Perturbation: Perturbation(moves, ruleset),
                Harmony: RowHarmony.Delta(grid, branch, blocked.Id, demand.Date)
                    + RowHarmony.Delta(grid, branch, recipient.Id, demand.Date),
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
            Harmony: 0,
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

    private static decimal AddedOf(IReadOnlyDictionary<Guid, decimal> added, Guid agentId)
        => added.TryGetValue(agentId, out var hours) ? hours : 0m;

    /// <summary>
    /// True when committing <paramref name="extraHours"/> to the agent would push its planned period hours
    /// (baseline + what this repair already added) past the contract MaximumHours cap. 0 = unconstrained.
    /// </summary>
    private static bool ExceedsMaximumHours(RecoveryAgent agent, decimal addedSoFar, decimal extraHours)
        => agent.MaximumHours > 0
            && agent.CurrentPeriodHours + addedSoFar + extraHours > agent.MaximumHours;

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
        return EscalationTierSeverity.Highest(deltas.Select(d => d.Tier), uncovered.Count > 0);
    }

    private static bool IsPreferred(RecoveryAgent agent, Guid? shiftId)
        => shiftId is Guid id && agent.PreferredShiftIds.Contains(id);

    private static IReadOnlyList<Guid> WorkIdsOf(RecoveryWork work)
        => work.WorkIds ?? [];

    /// <summary>
    /// Whole-repair ranking, the per-option order summed over the absence: uncovered critical slots, introduced
    /// violations, perturbation, rhythm delta (the per-option deltas add up, each is measured on the grid its option
    /// was committed to), shift-wish misses; then the option ranks taken, so among equal repairs the one closer to
    /// the greedy choices wins.
    /// </summary>
    private readonly record struct PathKey(int Uncovered, int Violations, int Perturbation, int Harmony, int PreferenceMisses, string Ranks)
        : IComparable<PathKey>
    {
        public int CompareTo(PathKey other)
        {
            var c = Uncovered.CompareTo(other.Uncovered);
            if (c != 0)
            {
                return c;
            }
            c = Violations.CompareTo(other.Violations);
            if (c != 0)
            {
                return c;
            }
            c = Perturbation.CompareTo(other.Perturbation);
            if (c != 0)
            {
                return c;
            }
            c = Harmony.CompareTo(other.Harmony);
            if (c != 0)
            {
                return c;
            }
            c = PreferenceMisses.CompareTo(other.PreferenceMisses);
            if (c != 0)
            {
                return c;
            }
            return string.CompareOrdinal(Ranks, other.Ranks);
        }
    }

    /// <summary>One partial repair: its own grid, the period hours it added so far and the options it committed.</summary>
    private sealed class RepairPath
    {
        private RepairPath(WorkingGrid grid, Dictionary<Guid, decimal> added, List<RepairOption> options, PathKey key)
        {
            Grid = grid;
            AddedPeriodHours = added;
            Options = options;
            Key = key;
        }

        public WorkingGrid Grid { get; }

        public Dictionary<Guid, decimal> AddedPeriodHours { get; }

        public List<RepairOption> Options { get; }

        public PathKey Key { get; }

        public static RepairPath Start(WorkingGrid grid) => new(grid, [], [], new PathKey(0, 0, 0, 0, 0, string.Empty));

        /// <summary>
        /// Commits <paramref name="option"/> on a copy of this path (or in place when <paramref name="inPlace"/>, the
        /// greedy case, which never needs this path again).
        /// </summary>
        public RepairPath Extend(RepairOption option, int rank, DemandSlot demand, bool inPlace)
        {
            var grid = inPlace ? Grid : Grid.Branch();
            var added = inPlace ? AddedPeriodHours : new Dictionary<Guid, decimal>(AddedPeriodHours);
            Commit(grid, option);
            if (option.Covers)
            {
                // Running period-hours added per agent by this repair so far. The MaximumHours gate compares each
                // candidate's baseline (CurrentPeriodHours) plus what earlier covers already added against the cap,
                // so multiple covers to the same agent across the absence cannot cumulatively breach it.
                foreach (var move in option.Moves)
                {
                    added[move.ToAgentId] = AddedOf(added, move.ToAgentId) + move.Placed.Hours;
                    if (move.Removed is not null)
                    {
                        added[move.FromAgentId] = AddedOf(added, move.FromAgentId) - move.Removed.Hours;
                    }
                }
            }

            var options = inPlace ? Options : [.. Options];
            options.Add(option);
            var k = option.Key;
            var key = new PathKey(
                Key.Uncovered + (option.Covers ? 0 : (demand.IsCritical ? 1 : 0)),
                Key.Violations + option.IntroducedViolations,
                Key.Perturbation + (option.Covers ? k.Perturbation : 0),
                Key.Harmony + (option.Covers ? k.Harmony : 0),
                Key.PreferenceMisses + (option.Covers ? k.PreferenceRank : 0),
                Key.Ranks + (char)('a' + Math.Min(rank, 25)));
            return new RepairPath(grid, added, options, key);
        }
    }
}
