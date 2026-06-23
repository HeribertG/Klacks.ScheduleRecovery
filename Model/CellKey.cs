// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleRecovery.Model;

/// <summary>
/// Identifies a single cell in the recovery grid by the agent that owns the row and the calendar day.
/// Used as the dictionary key for both window and boundary cells.
/// </summary>
/// <param name="AgentId">Owning agent (matches <see cref="RecoveryAgent.Id"/>)</param>
/// <param name="Date">Calendar day the cell belongs to</param>
public readonly record struct CellKey(Guid AgentId, DateOnly Date);
