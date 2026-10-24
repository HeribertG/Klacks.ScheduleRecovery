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
/// The penalties themselves live in <see cref="RowRhythm"/>, which the planner's manual candidate search uses as well.
/// The measure is an integer penalty count over a short reach around the changed day, and an option is scored
/// by the change it causes (after minus before) on every row it touches, so a cover that repairs a broken
/// rhythm can even score negative. Pure and deterministic: it only reads the grid, no float, no clock, no
/// random source.
/// </para>
/// </summary>
internal static class RowHarmony
{
    /// <summary>
    /// The rhythm change on the agent's row caused by going from <paramref name="before"/> to
    /// <paramref name="after"/> around <paramref name="date"/>; lower is better, 0 means unchanged.
    /// </summary>
    public static int Delta(WorkingGrid before, WorkingGrid after, Guid agentId, DateOnly date)
    {
        var from = date.AddDays(-RowRhythm.Reach);
        var to = date.AddDays(RowRhythm.Reach);
        return Cost(after, agentId, from, to) - Cost(before, agentId, from, to);
    }

    /// <summary>The rhythm penalty of the agent's row on the days from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static int Cost(WorkingGrid grid, Guid agentId, DateOnly from, DateOnly to)
        => RowRhythm.Cost(day => KindOf(grid, agentId, day), from, to);

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
