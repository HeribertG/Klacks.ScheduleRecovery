// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleRecovery.Model;

public enum EscalationTier
{
    InGroupFree = 0,
    InGroupSwap = 1,
    CrossGroupFree = 2,
    CrossGroupSwap = 3,
    Uncovered = 4
}
