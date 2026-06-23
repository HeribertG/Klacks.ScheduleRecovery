// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleRecovery.Model;

/// <summary>
/// The disruption to repair: one agent becomes unavailable across a set of days inside the snapshot
/// window. Every working, non-locked cell of this agent on these days turns into an uncovered demand;
/// locked cells are surfaced for manual review instead of being reassigned.
/// </summary>
/// <param name="AgentId">The agent that fell out (sick / no-show)</param>
/// <param name="Dates">The days inside the snapshot window on which the agent is unavailable</param>
public sealed record AbsenceEvent(Guid AgentId, IReadOnlyList<DateOnly> Dates);
