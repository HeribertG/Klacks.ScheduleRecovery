// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleRecovery.Model;

namespace Klacks.ScheduleRecovery.Engine;

/// <summary>
/// The pure, deterministic re-rostering core. Given a windowed plan, a single absence and a ruleset it
/// returns the minimal-perturbation repair as a proposal. The function is referentially transparent:
/// equal inputs always yield an equal proposal, with no I/O, no clock and no random source.
/// </summary>
public interface IRecoveryEngine
{
    /// <summary>
    /// Repairs the snapshot for the given absence under the ruleset and returns the lexicographically
    /// optimal proposal reachable by the in-group escalation ladder (tiers 0, 1 and the uncovered
    /// fallback). An absence that vacates no working cell yields <see cref="RecoveryProposal.Empty"/>.
    /// </summary>
    RecoveryProposal Repair(RecoverySnapshot snapshot, AbsenceEvent absence, Ruleset ruleset);
}
