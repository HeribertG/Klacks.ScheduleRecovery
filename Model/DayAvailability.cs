// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleRecovery.Model;

/// <summary>
/// Per (agent, date) availability, ported from the Wizard-2 domain validator. Mirrors the contract
/// WorksOnDay rule, FREE/keyword schedule commands and break blockers so the recovery engine applies
/// the exact same hard availability gate as the existing planners.
/// </summary>
/// <param name="WorksOnDay">True if the agent's contract permits work on this day-of-week</param>
/// <param name="HasFreeCommand">True if a schedule command with the FREE keyword blocks this date</param>
/// <param name="HasBreakBlocker">True if a break or absence already overlaps this date</param>
/// <param name="RequiredCategory">When set, an assignment on this date must equal this category (OnlyEarly/OnlyLate/OnlyNight)</param>
/// <param name="ForbiddenCategory">When set, an assignment on this date must not equal this category (NoEarly/NoLate/NoNight)</param>
public sealed record DayAvailability(
    bool WorksOnDay,
    bool HasFreeCommand,
    bool HasBreakBlocker,
    ShiftCategory? RequiredCategory = null,
    ShiftCategory? ForbiddenCategory = null)
{
    public static readonly DayAvailability AlwaysAvailable = new(true, false, false);

    public bool IsAvailable => WorksOnDay && !HasFreeCommand && !HasBreakBlocker;
}
