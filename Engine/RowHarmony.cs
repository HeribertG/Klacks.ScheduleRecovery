// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleRecovery.Model;

namespace Klacks.ScheduleRecovery.Engine;

/// <summary>
/// Second instance of the repair, next to the hard rules: how well a row keeps its rhythm. The legality gate
/// only says whether a cover is allowed; this measure says whether it fits the row it lands in - a cover that
/// extends the candidate's own block of the same shift kind keeps the beat, a lonely single shift in the middle
/// of free days, a block of mixed kinds, a backward step (late before early, night before late or early) or a
/// single free day squeezed between two work days breaks it.
/// <para>
/// The measure is an integer penalty count over a short reach around the changed day, and an option is scored
/// by the change it causes (after minus before) on every row it touches, so a cover that repairs a broken
/// rhythm can even score negative. Pure and deterministic: it only reads the grid, no float, no clock, no
/// random source.
/// </para>
/// </summary>
internal static class RowHarmony
{
    /// <summary>Days on each side of a changed day whose rhythm a single change can affect.</summary>
    internal const int Reach = 3;

    /// <summary>A work day with free days on both sides: one shift that tears a free block apart.</summary>
    internal const int IsolatedWorkDay = 2;

    /// <summary>Two consecutive work days of different shift kinds.</summary>
    internal const int MixedBlock = 1;

    /// <summary>Two consecutive work days that rotate backwards (later kind before an earlier one).</summary>
    internal const int BackwardRotation = 1;

    /// <summary>A single free day between two work days: no real rest.</summary>
    internal const int SingleFreeDay = 1;

    /// <summary>
    /// The rhythm change on the agent's row caused by going from <paramref name="before"/> to
    /// <paramref name="after"/> around <paramref name="date"/>; lower is better, 0 means unchanged.
    /// </summary>
    public static int Delta(WorkingGrid before, WorkingGrid after, Guid agentId, DateOnly date)
    {
        var from = date.AddDays(-Reach);
        var to = date.AddDays(Reach);
        return Cost(after, agentId, from, to) - Cost(before, agentId, from, to);
    }

    /// <summary>The rhythm penalty of the agent's row on the days from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static int Cost(WorkingGrid grid, Guid agentId, DateOnly from, DateOnly to)
    {
        var cost = 0;
        var previous = KindOf(grid, agentId, from.AddDays(-1));
        var current = KindOf(grid, agentId, from);
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var next = KindOf(grid, agentId, day.AddDays(1));
            cost += DayCost(previous, current, next);
            previous = current;
            current = next;
        }

        return cost;
    }

    private static int DayCost(ShiftCategory previous, ShiftCategory current, ShiftCategory next)
    {
        if (current == ShiftCategory.Free)
        {
            return previous != ShiftCategory.Free && next != ShiftCategory.Free ? SingleFreeDay : 0;
        }

        if (previous == ShiftCategory.Free)
        {
            return next == ShiftCategory.Free ? IsolatedWorkDay : 0;
        }

        if (previous == current)
        {
            return 0;
        }

        return MixedBlock + (RotatesBackwards(previous, current) ? BackwardRotation : 0);
    }

    /// <summary>Early -> late -> night is the forward rotation; a step against it is backwards. Other kinds are neutral.</summary>
    private static bool RotatesBackwards(ShiftCategory previous, ShiftCategory current)
        => RotationRank(previous) is int from && RotationRank(current) is int to && to < from;

    private static int? RotationRank(ShiftCategory category) => category switch
    {
        ShiftCategory.Early => 0,
        ShiftCategory.Late => 1,
        ShiftCategory.Night => 2,
        _ => null,
    };

    /// <summary>
    /// The kind of the row on a day: the category of its earliest working work (a placed cover is appended to
    /// the day's list, so the earliest is searched, not taken first), or <see cref="ShiftCategory.Free"/> when the
    /// agent does not work that day.
    /// </summary>
    private static ShiftCategory KindOf(WorkingGrid grid, Guid agentId, DateOnly date)
    {
        RecoveryWork? earliest = null;
        foreach (var work in grid.Get(agentId, date))
        {
            if (work.IsWorking && work.Category != ShiftCategory.Free
                && (earliest is null || work.StartAt < earliest.StartAt))
            {
                earliest = work;
            }
        }

        return earliest?.Category ?? ShiftCategory.Free;
    }
}
