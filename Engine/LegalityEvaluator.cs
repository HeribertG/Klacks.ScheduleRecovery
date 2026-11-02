// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using Klacks.ScheduleRecovery.Model;

namespace Klacks.ScheduleRecovery.Engine;

/// <summary>
/// Ports the hard-constraint logic of the Wizard-2 DomainAwareReplaceValidator and the find_replacement
/// exclusions onto the recovery model, without referencing the optimizer, and lifts it from one-cell-per-day
/// to interval-based so split shifts are handled. All hard rules are treated as <b>gates</b> that exclude a
/// candidate (matching the Wizard Stage-0 gate, so a repaired plan never carries a hard violation):
/// <see cref="IsGatedByContract"/> covers the contract/availability rules (availability, keyword, blacklist,
/// missing qualification, shift-rotation), <see cref="TargetIsPlaceable"/> the time-overlap collision, and
/// <see cref="CountViolations"/> the count-based caps (min-pause, max-consecutive-days, max-weekly-hours,
/// max-daily-hours) — any non-zero count excludes the candidate. Boundary-day context is read straight
/// through the working grid.
/// </summary>
internal static class LegalityEvaluator
{
    private const int WeekScanRadiusDays = 6;

    /// <summary>
    /// True if the contract/availability rules forbid the agent from taking a shift of the given category
    /// on the date, independent of the current grid occupancy. When the incoming work is given, an absence
    /// (or sealed day) on any other day its lead, interval or trail reaches gates the agent as well: a night
    /// running into an absent morning is not a legal cover.
    /// </summary>
    /// <param name="incoming">The work the agent would take over, or null when only the anchor day is judged</param>
    public static bool IsGatedByContract(
        RecoverySnapshot snapshot,
        RecoveryAgent agent,
        ShiftCategory category,
        Guid? shiftId,
        DateOnly date,
        RecoveryWork? incoming = null)
    {
        var availability = snapshot.GetAvailability(agent.Id, date);
        if (!availability.IsAvailable)
        {
            return true;
        }
        if (availability.RequiredCategory is { } required && category != required)
        {
            return true;
        }
        if (availability.ForbiddenCategory is { } forbidden && category == forbidden)
        {
            return true;
        }
        if (!agent.PerformsShiftWork && category != ShiftCategory.Early && category != ShiftCategory.Free)
        {
            return true;
        }
        if (shiftId is Guid id && id != Guid.Empty && agent.BlacklistedShiftIds.Contains(id))
        {
            return true;
        }
        return snapshot.IsIneligible(agent.Id, shiftId, date)
            || (incoming is not null && ReachesBlockedDay(snapshot, agent.Id, incoming, date));
    }

    private static bool ReachesBlockedDay(RecoverySnapshot snapshot, Guid agentId, RecoveryWork incoming, DateOnly anchor)
    {
        if (!incoming.HasInterval)
        {
            return false;
        }

        var first = DateOnly.FromDateTime(incoming.OccupiedStart);
        var last = DateOnly.FromDateTime(incoming.OccupiedEnd.AddTicks(-1));
        for (var day = first; day <= last; day = day.AddDays(1))
        {
            if (day != anchor && snapshot.GetAvailability(agentId, day).HasBreakBlocker)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// True if the agent can host a work of the given interval on the date without a collision: no existing
    /// work (locked or not) on the adjacent days overlaps the interval, counting the lead and trail of both. A free window on a day the agent
    /// already partly works is therefore placeable, which a one-cell-per-day model would have rejected.
    /// </summary>
    public static bool TargetIsPlaceable(WorkingGrid grid, Guid agentId, DateOnly date, RecoveryWork incoming)
    {
        if (!incoming.HasInterval)
        {
            return grid.Get(agentId, date).Count == 0;
        }

        foreach (var probe in AdjacentDays(date))
        {
            foreach (var work in grid.Get(agentId, probe))
            {
                if (work.OccupiedOverlaps(incoming))
                {
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>
    /// Counts the committable hard violations introduced by placing <paramref name="incoming"/> at
    /// (agent, date): min-pause, max-consecutive-days and max-weekly-hours. The work is assumed already
    /// present in the grid. Zero means a perfectly clean placement.
    /// </summary>
    public static int CountViolations(WorkingGrid grid, RecoveryAgent agent, DateOnly date, RecoveryWork incoming)
    {
        var count = 0;
        if (ViolatesMinPause(grid, agent, date, incoming))
        {
            count++;
        }
        if (ViolatesMaxConsecutiveDays(grid, agent, date, incoming))
        {
            count++;
        }
        if (ViolatesMaxWeeklyHours(grid, agent, date, incoming))
        {
            count++;
        }
        if (ViolatesMaxDailyHours(grid, agent, date, incoming))
        {
            count++;
        }
        return count;
    }

    private static bool ViolatesMaxDailyHours(WorkingGrid grid, RecoveryAgent agent, DateOnly date, RecoveryWork incoming)
    {
        if (agent.MaxDailyHours <= 0 || !incoming.IsWorking)
        {
            return false;
        }

        var total = 0m;
        foreach (var work in grid.Get(agent.Id, date))
        {
            if (work.IsWorking)
            {
                total += work.PaidHours;
            }
        }

        return total > agent.MaxDailyHours;
    }

    private static bool ViolatesMinPause(WorkingGrid grid, RecoveryAgent agent, DateOnly date, RecoveryWork incoming)
    {
        if (agent.MinPauseHours <= 0 || !incoming.HasInterval)
        {
            return false;
        }

        DateTime? predecessorEnd = null;
        DateTime? successorStart = null;
        foreach (var probe in AdjacentDays(date))
        {
            foreach (var work in grid.Get(agent.Id, probe))
            {
                if (ReferenceEquals(work, incoming) || !work.IsWorking || !work.HasInterval)
                {
                    continue;
                }
                if (work.OccupiedEnd <= incoming.OccupiedStart
                    && (predecessorEnd is null || work.OccupiedEnd > predecessorEnd))
                {
                    predecessorEnd = work.OccupiedEnd;
                }
                if (work.OccupiedStart >= incoming.OccupiedEnd
                    && (successorStart is null || work.OccupiedStart < successorStart))
                {
                    successorStart = work.OccupiedStart;
                }
            }
        }

        // Integer/decimal tick arithmetic only — no double in the decision path, so the rest-gap compare
        // is exact at the cap boundary (gap.Ticks < MinPauseHours * ticks-per-hour is equivalent to the
        // former TotalHours form but never rounds).
        var minPauseTicks = agent.MinPauseHours * TimeSpan.TicksPerHour;
        if (predecessorEnd is { } prev && (incoming.OccupiedStart - prev).Ticks < minPauseTicks)
        {
            return true;
        }
        if (successorStart is { } next && (next - incoming.OccupiedEnd).Ticks < minPauseTicks)
        {
            return true;
        }
        return false;
    }

    private static bool ViolatesMaxConsecutiveDays(
        WorkingGrid grid, RecoveryAgent agent, DateOnly date, RecoveryWork incoming)
    {
        if (agent.MaxConsecutiveDays <= 0 || !incoming.IsWorking)
        {
            return false;
        }

        var run = 1;
        for (var probe = date.AddDays(-1); IsDayOccupied(grid, agent.Id, probe); probe = probe.AddDays(-1))
        {
            run++;
        }
        for (var probe = date.AddDays(1); IsDayOccupied(grid, agent.Id, probe); probe = probe.AddDays(1))
        {
            run++;
        }

        return run > agent.MaxConsecutiveDays;
    }

    private static bool IsDayOccupied(WorkingGrid grid, Guid agentId, DateOnly date)
    {
        foreach (var work in grid.Get(agentId, date))
        {
            if (work.IsWorking)
            {
                return true;
            }
        }
        foreach (var work in grid.Get(agentId, date.AddDays(-1)))
        {
            if (work.IsWorking && work.CrossesMidnight)
            {
                return true;
            }
        }
        return false;
    }

    private static bool ViolatesMaxWeeklyHours(
        WorkingGrid grid, RecoveryAgent agent, DateOnly date, RecoveryWork incoming)
    {
        if (agent.MaxWeeklyHours <= 0 || incoming.PaidHours <= 0)
        {
            return false;
        }

        var targetWeek = WeekOf(date);
        var total = 0m;
        for (var offset = -WeekScanRadiusDays; offset <= WeekScanRadiusDays; offset++)
        {
            var day = date.AddDays(offset);
            if (WeekOf(day) != targetWeek)
            {
                continue;
            }
            foreach (var work in grid.Get(agent.Id, day))
            {
                if (work.IsWorking)
                {
                    total += work.PaidHours;
                }
            }
        }

        return total > agent.MaxWeeklyHours;
    }

    private static IEnumerable<DateOnly> AdjacentDays(DateOnly date)
    {
        yield return date.AddDays(-1);
        yield return date;
        yield return date.AddDays(1);
    }

    private static (int Year, int Week) WeekOf(DateOnly date)
    {
        // ISOWeek keys by the ISO week-year, so a Mon-Sun week that straddles 31 Dec / 1 Jan stays one
        // key. This is intentionally more correct than the legacy DomainAwareReplaceValidator (which keyed
        // by calendar year and split such a week), so the weekly cap is not under-counted at the boundary.
        var dt = date.ToDateTime(TimeOnly.MinValue);
        return (ISOWeek.GetYear(dt), ISOWeek.GetWeekOfYear(dt));
    }
}
