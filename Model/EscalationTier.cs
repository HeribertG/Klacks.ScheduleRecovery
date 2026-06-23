// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleRecovery.Model;

public enum EscalationTier
{
    InGroupFree = 0,
    InGroupSwap = 1,
    CrossGroupFree = 2,
    CrossGroupSwap = 3,
    Uncovered = 4
}
