// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleRecovery.Model;

/// <summary>
/// Default values of the repair search, kept in one place so the <see cref="Ruleset"/> record defaults and
/// any external caller reference the same constants instead of magic numbers.
/// </summary>
public static class RulesetDefaults
{
    public const int MaxSwapChainDepth = 2;
    public const int WeightInGroupFree = 1;
    public const int WeightInGroupSwap = 4;
    public const int WeightCrossGroupFree = 16;
    public const int WeightCrossGroupSwap = 64;
}
