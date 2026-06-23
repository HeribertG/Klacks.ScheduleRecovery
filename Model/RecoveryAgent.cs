// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleRecovery.Model;

/// <summary>
/// An employee that may receive a reassignment. Carries the contract constraints lifted from Wizard 1
/// (MaxWeeklyHours / MaxConsecutiveDays / MinPauseHours) plus the fairness signals ported from the
/// find_replacement ranking (preferred shifts, period target-hours deficit) and the blacklist.
/// All collections are pre-frozen by the snapshot builder; the engine never mutates them.
/// </summary>
/// <param name="Id">Agent identifier (matches the Klacks Client.Id)</param>
/// <param name="DisplayName">Human-readable name, used as a stable secondary tie-break and for diagnostics</param>
/// <param name="MaxWeeklyHours">Maximum weekly hours from the active contract; 0 = unconstrained</param>
/// <param name="MaxConsecutiveDays">Maximum consecutive working days from the active contract; 0 = unconstrained</param>
/// <param name="MinPauseHours">Minimum rest hours between two work spans from the active contract; 0 = unconstrained</param>
/// <param name="TargetHoursDeficit">Period guaranteed hours minus already-assigned hours; a higher deficit ranks higher (fairness)</param>
/// <param name="PreferredShiftIds">Shift ids the agent prefers (ClientShiftPreference Preferred)</param>
/// <param name="BlacklistedShiftIds">Shift ids the agent must never be assigned to (ClientShiftPreference Blacklist)</param>
/// <param name="IsInGroup">True if the agent is a member of the receiving group; false marks a borrowable cross-group candidate</param>
public sealed record RecoveryAgent(
    Guid Id,
    string DisplayName,
    decimal MaxWeeklyHours,
    int MaxConsecutiveDays,
    decimal MinPauseHours,
    decimal TargetHoursDeficit,
    IReadOnlySet<Guid> PreferredShiftIds,
    IReadOnlySet<Guid> BlacklistedShiftIds,
    bool IsInGroup = true);
