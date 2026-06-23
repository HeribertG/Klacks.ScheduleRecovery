// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleRecovery.Model;

/// <summary>
/// A temporary cross-group membership the Shell must materialise as a scenario-scoped group_item so a
/// borrowed agent can cover a slot in the receiving group. Not produced by the in-group MVP engine;
/// the type exists so the cross-group escalation (R4/R5b) is purely additive.
/// </summary>
/// <param name="AgentId">The borrowed agent</param>
/// <param name="GroupId">The receiving group the agent is temporarily added to</param>
/// <param name="ValidFrom">First day of the temporary membership (derived from the absence window)</param>
/// <param name="ValidUntil">Last day of the temporary membership (derived from the absence window)</param>
public sealed record MembershipDelta(
    Guid AgentId,
    Guid GroupId,
    DateOnly ValidFrom,
    DateOnly ValidUntil);
