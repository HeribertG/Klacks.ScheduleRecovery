// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleRecovery.Model;

namespace Klacks.ScheduleRecovery.Engine;

/// <summary>
/// One candidate resolution for a single demand: either a covering move (one hop for a direct cover, two
/// for a depth-2 swap) or the deliberate decision to leave the slot uncovered. The <see cref="Key"/>
/// orders options; the lowest key wins. The escalation tier of each hop lives on its <see cref="WorkMove"/>,
/// so it is not duplicated here. A cross-group cover also carries the temporary <see cref="Membership"/>.
/// </summary>
/// <param name="Key">The total-order key used to select the best option</param>
/// <param name="Covers">True if the option covers the demand, false if it leaves it uncovered</param>
/// <param name="Moves">The grid mutations (relocation first, cover last); empty when not covering</param>
/// <param name="IntroducedViolations">Committable hard violations this option introduces</param>
/// <param name="UncoveredReason">Reason attached when the option does not cover</param>
/// <param name="Membership">The temporary cross-group membership to materialise, or null for an in-group cover</param>
internal sealed record RepairOption(
    OptionKey Key,
    bool Covers,
    IReadOnlyList<WorkMove> Moves,
    int IntroducedViolations,
    string UncoveredReason,
    MembershipDelta? Membership = null);
