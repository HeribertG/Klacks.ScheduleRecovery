// Copyright (c) Heribert Gasparoli Private. All rights reserved.

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
public sealed record RecoveryWork(
    ShiftCategory Category,
    Guid? ShiftId,
    bool IsLocked,
    DateTime StartAt = default,
    DateTime EndAt = default,
    decimal Hours = 0m,
    IReadOnlyList<Guid>? WorkIds = null)
{
    /// <summary>True when the assignment is an actual shift (not a Break).</summary>
    public bool IsWorking => Category != ShiftCategory.Break;

    /// <summary>True when the work's interval ends on a later calendar day than it starts.</summary>
    public bool CrossesMidnight => HasInterval && EndAt.Date > StartAt.Date;

    /// <summary>True when the work carries a real, non-default time interval.</summary>
    public bool HasInterval => StartAt != default && EndAt != default;

    /// <summary>True when this work's interval overlaps the given interval (half-open comparison).</summary>
    public bool OverlapsInterval(DateTime start, DateTime end)
        => HasInterval && start != default && end != default && StartAt < end && start < EndAt;
}
