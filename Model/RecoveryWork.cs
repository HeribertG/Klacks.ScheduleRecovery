// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleRecovery.Model;

/// <summary>
/// One work assignment. An (agent, day) can hold several of these — Klacks supports split shifts — so the
/// engine reasons about works by their real time interval, not one cell per day. The absence of any work
/// means the agent is free in that window. <see cref="StartAt"/>/<see cref="EndAt"/> carry the real,
/// cross-midnight-aware interval used by the ported domain checks (overlap, min-pause, consecutive run).
/// </summary>
/// <param name="Category">Shift category (Early / Late / Night / Other) or Break for an absence span</param>
/// <param name="ShiftId">Reference to the shift definition; null on Break</param>
/// <param name="IsLocked">True if the work must not be moved (Work.LockLevel != None, or any Break)</param>
/// <param name="StartAt">Start instant of the work</param>
/// <param name="EndAt">End instant of the work</param>
/// <param name="Hours">Paid hours; Break.WorkTime for a Break, contributing to the period target only</param>
/// <param name="WorkIds">Underlying Work ids that materialise this assignment, for delta provenance</param>
/// <param name="LeadHours">Briefing, start correction and start travel time directly before <paramref name="StartAt"/>; occupied time, no paid hours of its own</param>
/// <param name="TrailHours">Debriefing, end correction and end travel time directly after <paramref name="EndAt"/>; occupied time, no paid hours of its own</param>
/// <param name="MovesAsWhole">True when a reassignment of this work moves it with everything on it, so its extras go along</param>
/// <param name="ExtraPaidHours">Hours of travel and corrections on top of <paramref name="Hours"/>; a briefing or debriefing adds none</param>
public sealed record RecoveryWork(
    ShiftCategory Category,
    Guid? ShiftId,
    bool IsLocked,
    DateTime StartAt = default,
    DateTime EndAt = default,
    decimal Hours = 0m,
    IReadOnlyList<Guid>? WorkIds = null,
    decimal LeadHours = 0m,
    decimal TrailHours = 0m,
    decimal ExtraPaidHours = 0m,
    bool MovesAsWhole = false)
{
    /// <summary>True when the assignment is an actual shift (not a Break).</summary>
    public bool IsWorking => Category != ShiftCategory.Break;

    /// <summary>True when the work's interval ends on a later calendar day than it starts.</summary>
    public bool CrossesMidnight => HasInterval && EndAt.Date > StartAt.Date;

    /// <summary>True when the work carries a real, non-default time interval.</summary>
    public bool HasInterval => StartAt != default && EndAt != default;

    /// <summary>First instant the work occupies: <see cref="StartAt"/> minus the lead.</summary>
    public DateTime OccupiedStart => HasInterval ? StartAt - HoursSpan(LeadHours) : StartAt;

    /// <summary>Last instant the work occupies: <see cref="EndAt"/> plus the trail.</summary>
    public DateTime OccupiedEnd => HasInterval ? EndAt + HoursSpan(TrailHours) : EndAt;

    /// <summary>Hours the work counts towards the hour caps: its interval plus the extra paid hours.</summary>
    public decimal PaidHours => Hours + ExtraPaidHours;

    /// <summary>Hours its owner loses when the work is reassigned.</summary>
    public decimal HoursGivenUp => MovesAsWhole ? PaidHours : Hours;

    /// <summary>True when the lead, the work and the trail of this work overlap those of <paramref name="other"/>.</summary>
    public bool OccupiedOverlaps(RecoveryWork other)
        => HasInterval && other.HasInterval && OccupiedStart < other.OccupiedEnd && other.OccupiedStart < OccupiedEnd;

    /// <summary>True when this work's interval overlaps the given interval (half-open comparison).</summary>
    public bool OverlapsInterval(DateTime start, DateTime end)
        => HasInterval && start != default && end != default && StartAt < end && start < EndAt;

    private static TimeSpan HoursSpan(decimal hours) => TimeSpan.FromTicks((long)(hours * TimeSpan.TicksPerHour));
}
