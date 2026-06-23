// Copyright (c) Heribert Gasparoli Private. All rights reserved.

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
