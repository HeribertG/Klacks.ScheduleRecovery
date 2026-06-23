// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleRecovery.Model;

/// <summary>
/// Identifies an (agent, shift, date) combination the agent is not qualified for. Membership in the
/// snapshot's ineligible set is a hard gate, mirroring the mandatory-qualification rule of the
/// EligibilityMatcher and the find_replacement handler.
/// </summary>
/// <param name="AgentId">The agent lacking the qualification</param>
/// <param name="ShiftId">The shift whose mandatory qualification is missing/expired/below level</param>
/// <param name="Date">The day the eligibility was evaluated for</param>
public readonly record struct IneligibleKey(Guid AgentId, Guid ShiftId, DateOnly Date);
