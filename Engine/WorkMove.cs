// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleRecovery.Model;

namespace Klacks.ScheduleRecovery.Engine;

/// <summary>
/// One concrete grid mutation inside an option: place <see cref="Placed"/> on <see cref="ToAgentId"/> and,
/// when <see cref="Removed"/> is set, drop that exact work instance from <see cref="FromAgentId"/>. The
/// absent agent's works are removed once up front, so a cover hop leaves <see cref="Removed"/> null. Both
/// the grid commit and the emitted <see cref="CellDelta"/> are derived from this single description, so
/// simulation and commit can never diverge.
/// </summary>
/// <param name="Placed">The work the receiving agent gains</param>
/// <param name="Removed">The exact work instance to remove from the source agent, or null if already vacated</param>
/// <param name="FromAgentId">The source agent (absent agent for a cover, displaced agent for a relocation)</param>
/// <param name="ToAgentId">The receiving agent</param>
/// <param name="Date">The anchor day of the move</param>
/// <param name="Tier">The escalation tier this hop belongs to</param>
internal sealed record WorkMove(
    RecoveryWork Placed,
    RecoveryWork? Removed,
    Guid FromAgentId,
    Guid ToAgentId,
    DateOnly Date,
    EscalationTier Tier)
{
    public CellDelta ToDelta()
        => new(
            Placed.ShiftId, Date, FromAgentId, ToAgentId, Placed.Category,
            Placed.StartAt, Placed.EndAt, Placed.Hours, Tier, Placed.WorkIds ?? []);
}
