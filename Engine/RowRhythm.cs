// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleRecovery.Model;

namespace Klacks.ScheduleRecovery.Engine;

/// <summary>
/// The rhythm penalties of one row, shared by the repair engine and the planner's manual candidate search, so both
/// judge "does this cover fit the row" the same way: a work day with free days on both sides (a lonely shift),
/// two consecutive work days of different shift kinds (mixed block), a step against the forward rotation
/// early -> late -> night (backward rotation) and a single free day between two work days. Pure integer counts,
/// no clock, no random source.
/// </summary>
public static class RowRhythm
{
    /// <summary>Days on each side of a changed day whose rhythm a single change can affect.</summary>
    public const int Reach = 3;

    /// <summary>A work day with free days on both sides: one shift that tears a free block apart.</summary>
    public const int IsolatedWorkDay = 2;

    /// <summary>Two consecutive work days of different shift kinds.</summary>
    public const int MixedBlock = 1;

    /// <summary>Two consecutive work days that rotate backwards (later kind before an earlier one).</summary>
    public const int BackwardRotation = 1;

    /// <summary>A single free day between two work days: no real rest.</summary>
    public const int SingleFreeDay = 1;

    /// <summary>
    /// The rhythm change on a row when a cover of kind <paramref name="placed"/> is added on <paramref name="date"/>:
    /// after minus before over <see cref="Reach"/> days on each side; negative = the cover closes or extends a block,
    /// 0 = neutral, positive = it breaks the rhythm. A day that already carries work keeps its kind (a second,
    /// non-overlapping shift does not change the row's rhythm), so the change is 0 there.
    /// </summary>
    /// <param name="kindOf">The row's kind per day: the category of the day's earliest working shift, Free when none</param>
    /// <param name="date">Day of the cover</param>
    /// <param name="placed">Shift kind of the cover</param>
    public static int CoverDelta(Func<DateOnly, ShiftCategory> kindOf, DateOnly date, ShiftCategory placed)
    {
        ArgumentNullException.ThrowIfNull(kindOf);
        if (kindOf(date) != ShiftCategory.Free || placed is ShiftCategory.Free or ShiftCategory.Break)
        {
            return 0;
        }

        var from = date.AddDays(-Reach);
        var to = date.AddDays(Reach);
        return Cost(day => day == date ? placed : kindOf(day), from, to) - Cost(kindOf, from, to);
    }

    /// <summary>The rhythm penalty of a row on the days from <paramref name="from"/> to <paramref name="to"/>.</summary>
    /// <param name="kindOf">The row's kind per day, Free when the row does not work that day</param>
    /// <param name="from">First day scored</param>
    /// <param name="to">Last day scored</param>
    public static int Cost(Func<DateOnly, ShiftCategory> kindOf, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(kindOf);
        var cost = 0;
        var previous = kindOf(from.AddDays(-1));
        var current = kindOf(from);
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var next = kindOf(day.AddDays(1));
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
}
