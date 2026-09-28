// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleRecovery.Model;

/// <summary>
/// Stable machine reasons attached to an <see cref="UncoveredSlot"/>. Kept as constants so callers and
/// tests never hard-code the strings.
/// </summary>
public static class RecoveryReasons
{
    public const string Locked = "locked";
    public const string NoEligibleCandidate = "no-eligible-candidate";
    public const string NonCritical = "non-critical";
}
