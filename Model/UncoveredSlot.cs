// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleRecovery.Model;

/// <summary>
/// A demand the engine deliberately left uncovered: either a locked slot of the absent agent (manual
/// review) or a non-critical slot whose minimal-perturbation optimum is to stay empty.
/// </summary>
/// <param name="ShiftId">The uncovered shift</param>
/// <param name="Date">The day of the uncovered slot</param>
/// <param name="Reason">Short machine reason ("locked" / "no-eligible-candidate" / "non-critical")</param>
/// <param name="IsCritical">Whether the slot counted toward the coverage objective</param>
/// <param name="SourceWorkIds">Underlying Work ids of the uncovered assignment</param>
public sealed record UncoveredSlot(
    Guid? ShiftId,
    DateOnly Date,
    string Reason,
    bool IsCritical,
    IReadOnlyList<Guid> SourceWorkIds);
