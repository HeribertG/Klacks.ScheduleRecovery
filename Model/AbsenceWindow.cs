// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleRecovery.Model;

/// <summary>
/// The real time span of one blocking absence (not an on-call duty, not a sealed day). A service that only
/// reaches a neighbouring day through midnight, lead or trail is blocked there only when it overlaps such a span.
/// </summary>
/// <param name="Start">Start instant of the absence</param>
/// <param name="End">End instant of the absence</param>
public sealed record AbsenceWindow(DateTime Start, DateTime End);
