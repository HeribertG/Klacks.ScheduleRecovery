// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleRecovery.Model;

/// <summary>
/// One reassignment of a shift on a day from one agent to another. A direct cover is a single delta;
/// a swap chain is an ordered list of deltas (the relocation hops first, the cover hop last). The Shell
/// translates each delta into a Replacement WorkChange under the scenario token.
/// </summary>
/// <param name="ShiftId">The shift being moved</param>
/// <param name="Date">The day of the assignment</param>
/// <param name="FromAgentId">The agent that previously held the shift (the absent agent, or a displaced agent in a swap)</param>
/// <param name="ToAgentId">The agent that now receives the shift</param>
/// <param name="Category">Shift category, carried for re-evaluation and persistence</param>
/// <param name="StartAt">Start instant of the assignment</param>
/// <param name="EndAt">End instant of the assignment</param>
/// <param name="Hours">Paid hours of the assignment</param>
/// <param name="Tier">The escalation tier this hop belongs to (drives the perturbation weight)</param>
/// <param name="SourceWorkIds">Underlying Work ids of the moved assignment, for persistence provenance</param>
public sealed record CellDelta(
    Guid? ShiftId,
    DateOnly Date,
    Guid FromAgentId,
    Guid ToAgentId,
    ShiftCategory Category,
    DateTime StartAt,
    DateTime EndAt,
    decimal Hours,
    EscalationTier Tier,
    IReadOnlyList<Guid> SourceWorkIds);
