// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleRecovery.Model;

namespace Klacks.ScheduleRecovery.Engine;

/// <summary>
/// An uncovered assignment the engine must try to re-cover: one working, non-locked work that the absent
/// agent held (an agent may hold several on a day — split shifts — so each becomes its own demand).
/// </summary>
/// <param name="OriginAgentId">The absent agent that previously held the work</param>
/// <param name="Date">The anchor day of the work</param>
/// <param name="Work">The vacated work (shift, interval, hours, work ids)</param>
/// <param name="IsCritical">True if leaving this slot uncovered costs a coverage point</param>
internal sealed record DemandSlot(Guid OriginAgentId, DateOnly Date, RecoveryWork Work, bool IsCritical)
{
    public Guid? ShiftId => Work.ShiftId;

    public ShiftCategory Category => Work.Category;

    /// <summary>The same work transferred to a new owner (lock cleared — a replacement is editable).</summary>
    public RecoveryWork AsAssignment() => Work with { IsLocked = false };
}
