// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleRecovery.Model;

/// <summary>
/// Per (agent, date) availability, ported from the Wizard-2 domain validator. Mirrors the contract
/// WorksOnDay rule, FREE/keyword schedule commands and break blockers so the recovery engine applies
/// the exact same hard availability gate as the existing planners. An on-call day is not a blocker: the
/// agent is reachable and is preferred as a replacement (see <see cref="EscalationTier.InGroupOnCall"/>).
/// </summary>
/// <param name="WorksOnDay">True if the agent's contract permits work on this day-of-week, or the agent is on call that day</param>
/// <param name="HasFreeCommand">True if a schedule command with the FREE keyword blocks this date</param>
/// <param name="HasBreakBlocker">True if a non-on-call break or absence already overlaps this date</param>
/// <param name="RequiredCategory">When set, an assignment on this date must equal this category (OnlyEarly/OnlyLate/OnlyNight)</param>
/// <param name="ForbiddenCategory">When set, an assignment on this date must not equal this category (NoEarly/NoLate/NoNight)</param>
/// <param name="IsOnCall">True if the agent holds an on-call absence on this date and no blocking absence</param>
public sealed record DayAvailability(
    bool WorksOnDay,
    bool HasFreeCommand,
    bool HasBreakBlocker,
    ShiftCategory? RequiredCategory = null,
    ShiftCategory? ForbiddenCategory = null,
    bool IsOnCall = false)
{
    public static readonly DayAvailability AlwaysAvailable = new(true, false, false);

    public bool IsAvailable => WorksOnDay && !HasFreeCommand && !HasBreakBlocker;
}
