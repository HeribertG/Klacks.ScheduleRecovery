// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleRecovery.Model;

/// <summary>
/// How far the repair search had to go for one hop. The integer values leave the engine (API result
/// fields and UI label keys), so new values are only ever appended; the escalation order is NOT the
/// numeric order and is defined by <see cref="EscalationTierSeverity"/>.
/// </summary>
public enum EscalationTier
{
    InGroupFree = 0,
    InGroupSwap = 1,
    CrossGroupFree = 2,
    CrossGroupSwap = 3,
    Uncovered = 4,
    InGroupOnCall = 5,
    CrossGroupOnCall = 6
}
