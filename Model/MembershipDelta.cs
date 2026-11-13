// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleRecovery.Model;

/// <summary>
/// A temporary cross-group membership the Shell must materialise as a scenario-scoped group_item so a
/// borrowed agent can cover a slot in the receiving group. Not produced by the in-group MVP engine;
/// the type exists so the cross-group escalation (R4/R5b) is purely additive. A delta covers the days the agent is
/// borrowed on without a gap: directly consecutive days share one delta, days with a gap between them get one each.
/// </summary>
/// <param name="AgentId">The borrowed agent</param>
/// <param name="GroupId">The receiving group the agent is temporarily added to</param>
/// <param name="ValidFrom">First borrowed day of the delta</param>
/// <param name="ValidUntil">Last borrowed day of the delta</param>
public sealed record MembershipDelta(
    Guid AgentId,
    Guid GroupId,
    DateOnly ValidFrom,
    DateOnly ValidUntil);
