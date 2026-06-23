// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleRecovery.Model;

/// <summary>
/// The immutable, I/O-free input to the recovery engine: the windowed plan (works per agent and day), the
/// candidate pool with their constraints, the per-day availability, the eligibility gate and the
/// criticality of slots. The builder freezes a total order on agents and days (by Guid / ascending date)
/// and a total order on each day's works (by start, then shift id) before construction, so the "pure"
/// engine can never inherit database-iteration non-determinism. Boundary days outside <see cref="Days"/>
/// may be present as read-only context for the cross-window rest/consecutive/weekly checks; the engine
/// never proposes deltas on them.
/// </summary>
public sealed class RecoverySnapshot
{
    private static readonly IReadOnlyList<RecoveryWork> NoWorks = [];

    private readonly IReadOnlyDictionary<CellKey, IReadOnlyList<RecoveryWork>> _works;
    private readonly IReadOnlyDictionary<CellKey, DayAvailability> _availability;
    private readonly IReadOnlySet<IneligibleKey> _ineligible;
    private readonly IReadOnlySet<CellKey> _nonCriticalSlots;
    private readonly IReadOnlyDictionary<Guid, RecoveryAgent> _agentsById;

    /// <param name="days">The window the repair operates on, sorted ascending</param>
    /// <param name="agents">The candidate pool; frozen into Guid order for a stable total enumeration</param>
    /// <param name="works">Works keyed by (agent, date), including read-only boundary days</param>
    /// <param name="availability">Per (agent, date) availability; a missing key means always available</param>
    /// <param name="ineligibleAssignments">(agent, shift, date) combinations that are hard-gated</param>
    /// <param name="nonCriticalSlots">(agent, date) keys whose coverage is optional; missing means critical</param>
    /// <param name="receivingGroupId">The group being repaired; a cross-group cover joins a borrowed agent to it</param>
    public RecoverySnapshot(
        IReadOnlyList<DateOnly> days,
        IReadOnlyList<RecoveryAgent> agents,
        IReadOnlyDictionary<CellKey, IReadOnlyList<RecoveryWork>> works,
        IReadOnlyDictionary<CellKey, DayAvailability>? availability = null,
        IReadOnlySet<IneligibleKey>? ineligibleAssignments = null,
        IReadOnlySet<CellKey>? nonCriticalSlots = null,
        Guid receivingGroupId = default)
    {
        Days = days.OrderBy(d => d.DayNumber).ToList();
        Agents = agents.OrderBy(a => a.Id).ToList();
        _agentsById = Agents.ToDictionary(a => a.Id);
        _works = FreezeWorkOrder(works);
        _availability = availability ?? new Dictionary<CellKey, DayAvailability>();
        _ineligible = ineligibleAssignments ?? new HashSet<IneligibleKey>();
        _nonCriticalSlots = nonCriticalSlots ?? new HashSet<CellKey>();
        ReceivingGroupId = receivingGroupId;
    }

    /// <summary>The group being repaired; a cross-group cover materialises a temporary membership in it.</summary>
    public Guid ReceivingGroupId { get; }

    /// <summary>The window the repair operates on, sorted ascending.</summary>
    public IReadOnlyList<DateOnly> Days { get; }

    /// <summary>The candidate pool in stable Guid order.</summary>
    public IReadOnlyList<RecoveryAgent> Agents { get; }

    /// <summary>The works at (agent, date) in stable order; an empty list when the agent is free.</summary>
    public IReadOnlyList<RecoveryWork> GetWorks(Guid agentId, DateOnly date)
        => _works.TryGetValue(new CellKey(agentId, date), out var works) ? works : NoWorks;

    /// <summary>The availability of (agent, date); <see cref="DayAvailability.AlwaysAvailable"/> when unspecified.</summary>
    public DayAvailability GetAvailability(Guid agentId, DateOnly date)
        => _availability.TryGetValue(new CellKey(agentId, date), out var availability)
            ? availability
            : DayAvailability.AlwaysAvailable;

    /// <summary>True if the agent is not qualified for the shift on the date (hard gate).</summary>
    public bool IsIneligible(Guid agentId, Guid? shiftId, DateOnly date)
        => shiftId is Guid id && id != Guid.Empty && _ineligible.Contains(new IneligibleKey(agentId, id, date));

    /// <summary>True if leaving the absent agent's slot uncovered costs a coverage point.</summary>
    public bool IsCritical(Guid absentAgentId, DateOnly date)
        => !_nonCriticalSlots.Contains(new CellKey(absentAgentId, date));

    /// <summary>Resolves an agent by id, or null when it is not part of the candidate pool.</summary>
    public RecoveryAgent? FindAgent(Guid agentId)
        => _agentsById.TryGetValue(agentId, out var agent) ? agent : null;

    private static IReadOnlyDictionary<CellKey, IReadOnlyList<RecoveryWork>> FreezeWorkOrder(
        IReadOnlyDictionary<CellKey, IReadOnlyList<RecoveryWork>> works)
    {
        var frozen = new Dictionary<CellKey, IReadOnlyList<RecoveryWork>>(works.Count);
        foreach (var (key, list) in works)
        {
            frozen[key] = list
                .OrderBy(w => w.StartAt)
                .ThenBy(w => w.ShiftId ?? Guid.Empty)
                .ToList();
        }
        return frozen;
    }
}
