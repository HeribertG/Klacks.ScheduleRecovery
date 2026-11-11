// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleRecovery.Model;

namespace Klacks.ScheduleRecovery.Engine;

/// <summary>
/// Searches the swap chains that cover one demand. A chain is a sequence of moves "a work goes from one agent to
/// another": the covering agent takes the demand and, because it could not take it as it stands, hands one of its
/// own works (the relief) to the next agent, who may in turn hand one on, until an agent takes its work without
/// giving anything up. The relief is any single movable work of the agent whose removal makes the incoming work
/// legal: a work at the same time (displacement), a work on the same or a neighbouring day that broke the rest
/// time or the daily cap, or a work on another day that broke the consecutive-days, weekly or period cap. A borrowed
/// agent only ever ends a chain, so its own works are never touched and one chain needs at most one temporary membership.
/// </summary>
/// <param name="snapshot">The immutable plan, pool and gates</param>
/// <param name="grid">The plan as committed so far; never mutated, every trial runs on a branch</param>
/// <param name="absentId">The absent agent, never part of a chain</param>
/// <param name="demand">The demand the chains cover</param>
/// <param name="ruleset">Depth, reach, budget and the perturbation weights</param>
/// <param name="firstReliefDay">First day on which a work may be taken from its holder</param>
/// <param name="addedPeriodHours">Period hours this repair already added per agent, for the period cap</param>
internal sealed class ChainSearch(
    RecoverySnapshot snapshot,
    WorkingGrid grid,
    Guid absentId,
    DemandSlot demand,
    Ruleset ruleset,
    DateOnly firstReliefDay,
    IReadOnlyDictionary<Guid, decimal> addedPeriodHours)
{
    /// <summary>Chains up to this length are searched in full; only longer ones spend the probe budget.</summary>
    public const int ShortChainMoves = 2;

    private bool _budgeted;
    private int _probesLeft;

    /// <summary>
    /// The best chain per covering agent among the chains of at most <paramref name="maxMoves"/> moves, in the stable
    /// order of the pool. A chain longer than <see cref="ShortChainMoves"/> is searched within the probe budget of the
    /// ruleset, shared evenly among the in-group agents so the first ones in the order cannot use it up; the pool and
    /// every relief are walked in a fixed order, so a spent share cuts the search at the same place on every run.
    /// </summary>
    /// <param name="maxMoves">Longest chain to build, the cover move included</param>
    public List<RepairOption> Options(int maxMoves)
    {
        _budgeted = maxMoves > ShortChainMoves;
        var share = ruleset.DeepChainProbeBudget / Math.Max(1, snapshot.Agents.Count(a => a.IsInGroup && a.Id != absentId));

        var demandWork = demand.AsAssignment();
        var options = new List<RepairOption>();
        foreach (var first in snapshot.Agents)
        {
            if (first.Id == absentId || !first.IsInGroup
                || LegalityEvaluator.IsGatedByContract(snapshot, first, demandWork, demand.Date)
                || Fits(grid, first, demand.Date, demandWork, 0m) is not null)
            {
                continue;
            }

            _probesLeft = share;

            var cover = new WorkMove(demandWork, null, absentId, first.Id, demand.Date, EscalationTier.InGroupSwap);
            Guid[] used = [absentId, first.Id];
            RepairOption? best = null;
            foreach (var relief in Reliefs(grid, first, demand.Date, demandWork))
            {
                foreach (var tail in Tails(relief, first, used, maxMoves - 1))
                {
                    best = LocalRepairEngine.Better(best, BuildOption(first, tail, cover));
                }
            }

            if (best is not null)
            {
                options.Add(best);
            }
        }

        return options;
    }

    /// <summary>
    /// Every way to hand the relief on within <paramref name="movesLeft"/> moves: an agent that takes it as it stands
    /// ends the chain, an in-group agent that cannot hands one of its own works on.
    /// </summary>
    private IEnumerable<ChainTail> Tails(Relief relief, RecoveryAgent from, Guid[] used, int movesLeft)
    {
        foreach (var recipient in snapshot.Agents)
        {
            if (Array.IndexOf(used, recipient.Id) >= 0)
            {
                continue;
            }
            if (!TakeProbe())
            {
                yield break;
            }
            if (LegalityEvaluator.IsGatedByContract(snapshot, recipient, relief.Work, relief.Date))
            {
                continue;
            }

            var tier = recipient.IsInGroup ? EscalationTier.InGroupSwap : EscalationTier.CrossGroupSwap;
            var move = new WorkMove(relief.Work, relief.Work, from.Id, recipient.Id, relief.Date, tier);
            var after = Fits(relief.Grid, recipient, relief.Date, relief.Work, 0m);
            if (after is not null)
            {
                var membership = recipient.IsInGroup
                    ? null
                    : new MembershipDelta(recipient.Id, snapshot.ReceivingGroupId, relief.Date, relief.Date);
                yield return new ChainTail([move], after, membership);
                continue;
            }

            if (movesLeft <= 1 || !recipient.IsInGroup)
            {
                continue;
            }

            Guid[] usedBelow = [.. used, recipient.Id];
            foreach (var next in Reliefs(relief.Grid, recipient, relief.Date, relief.Work))
            {
                foreach (var tail in Tails(next, recipient, usedBelow, movesLeft - 1))
                {
                    yield return new ChainTail([.. tail.Moves, move], tail.Grid, tail.Membership);
                }
            }
        }
    }

    /// <summary>
    /// The works of the agent whose removal alone lets it take <paramref name="incoming"/>, nearest day first (the
    /// day itself, then one day later, one day earlier, and so on), within the relief reach around the demand day and
    /// never before the first relief day or on a day sealed for the agent. Only a work the agent held before this
    /// repair counts: a work the repair placed on it stays where it is, so no work is moved twice.
    /// </summary>
    private IEnumerable<Relief> Reliefs(WorkingGrid current, RecoveryAgent agent, DateOnly date, RecoveryWork incoming)
    {
        var earliest = demand.Date.AddDays(-ruleset.ReliefReachDays);
        var latest = demand.Date.AddDays(ruleset.ReliefReachDays);
        if (earliest < firstReliefDay)
        {
            earliest = firstReliefDay;
        }

        // The incoming work lies within the reach of the demand day, so twice the reach on either side spans the range.
        for (var step = 0; step <= 4 * ruleset.ReliefReachDays; step++)
        {
            var day = date.AddDays(OffsetOf(step));
            if (day < earliest || day > latest || snapshot.GetAvailability(agent.Id, day).IsSealed)
            {
                continue;
            }

            var held = snapshot.GetWorks(agent.Id, day);
            foreach (var work in current.Get(agent.Id, day))
            {
                if (!work.IsWorking || work.IsLocked || work.IsSwapLocked || !Holds(held, work))
                {
                    continue;
                }
                if (!TakeProbe())
                {
                    yield break;
                }

                var probe = current.Branch();
                probe.RemoveWork(agent.Id, day, work);
                var after = Fits(probe, agent, date, incoming, work.HoursGivenUp);
                if (after is not null)
                {
                    yield return new Relief(work, day, after);
                }
            }
        }
    }

    /// <summary>
    /// The grid with <paramref name="work"/> placed on the agent, or null when the placement collides, breaks the
    /// period cap or introduces a hard violation. The contract gate is checked by the caller.
    /// </summary>
    private WorkingGrid? Fits(WorkingGrid current, RecoveryAgent agent, DateOnly date, RecoveryWork work, decimal hoursGivenUp)
    {
        if (!LegalityEvaluator.TargetIsPlaceable(current, agent.Id, date, work)
            || LocalRepairEngine.ExceedsMaximumHours(
                agent, LocalRepairEngine.AddedOf(addedPeriodHours, agent.Id) - hoursGivenUp, work.PaidHours))
        {
            return null;
        }

        var branch = current.Branch();
        branch.AddWork(agent.Id, date, work);
        return LegalityEvaluator.CountViolations(branch, agent, date, work) > 0 ? null : branch;
    }

    private RepairOption BuildOption(RecoveryAgent first, ChainTail tail, WorkMove cover)
    {
        WorkMove[] moves = [.. tail.Moves, cover];
        var key = new OptionKey(
            Coverage: 0,
            Violations: 0,
            Perturbation: LocalRepairEngine.Perturbation(moves, ruleset),
            Harmony: HarmonyOf(moves, tail.Grid),
            PreferenceRank: LocalRepairEngine.IsPreferred(first, demand.ShiftId) ? 0 : 1,
            TargetHoursDeficit: first.TargetHoursDeficit,
            PrimaryGuid: first.Id,
            SecondaryGuid: tail.Moves[^1].ToAgentId);
        return new RepairOption(key, true, moves, 0, string.Empty, tail.Membership);
    }

    /// <summary>
    /// Rhythm change of the chain: for every agent that receives a work, the row cost after minus before over the
    /// days the chain touches on that row, widened by the rhythm reach. With one touched day this is the plain delta.
    /// </summary>
    private int HarmonyOf(WorkMove[] moves, WorkingGrid after)
    {
        var total = 0;
        foreach (var receiving in moves)
        {
            var agentId = receiving.ToAgentId;
            var first = receiving.Date;
            var last = receiving.Date;
            foreach (var move in moves)
            {
                if (move.FromAgentId != agentId)
                {
                    continue;
                }
                first = move.Date < first ? move.Date : first;
                last = move.Date > last ? move.Date : last;
            }

            var from = first.AddDays(-RowRhythm.Reach);
            var to = last.AddDays(RowRhythm.Reach);
            total += RowHarmony.Cost(after, agentId, from, to) - RowHarmony.Cost(grid, agentId, from, to);
        }
        return total;
    }

    private bool TakeProbe()
    {
        if (!_budgeted)
        {
            return true;
        }
        if (_probesLeft <= 0)
        {
            return false;
        }
        _probesLeft--;
        return true;
    }

    private static bool Holds(IReadOnlyList<RecoveryWork> held, RecoveryWork work)
    {
        foreach (var candidate in held)
        {
            if (ReferenceEquals(candidate, work))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Day offsets in the order 0, +1, -1, +2, -2, ...</summary>
    private static int OffsetOf(int step) => step % 2 == 1 ? (step + 1) / 2 : -(step / 2);

    /// <summary>A work its holder gives up, with the grid in which the holder already holds the incoming work instead.</summary>
    private sealed record Relief(RecoveryWork Work, DateOnly Date, WorkingGrid Grid);

    /// <summary>The moves that pass a relief on (deepest first) and the grid after all of them.</summary>
    private sealed record ChainTail(IReadOnlyList<WorkMove> Moves, WorkingGrid Grid, MembershipDelta? Membership);
}
