// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleRecovery.Model;

namespace Klacks.ScheduleRecovery.Engine;

/// <summary>
/// A mutable view over the immutable snapshot that the engine evolves as it applies moves. Each (agent,
/// date) holds a list of works (split shifts). Untouched keys (including read-only boundary days) read
/// straight through to the snapshot; the first mutation of a key copies that day's list into an overlay,
/// so <see cref="Branch"/> can copy just the overlay lists and a candidate move can be simulated without
/// disturbing the committed state. Work records are immutable, so list copies safely share their elements
/// and reference identity stays valid for <see cref="RemoveWork"/>.
/// </summary>
internal sealed class WorkingGrid
{
    private readonly RecoverySnapshot _snapshot;
    private readonly Dictionary<CellKey, List<RecoveryWork>> _overlay;

    private WorkingGrid(RecoverySnapshot snapshot, Dictionary<CellKey, List<RecoveryWork>> overlay)
    {
        _snapshot = snapshot;
        _overlay = overlay;
    }

    public static WorkingGrid From(RecoverySnapshot snapshot)
        => new(snapshot, new Dictionary<CellKey, List<RecoveryWork>>());

    public IReadOnlyList<RecoveryWork> Get(Guid agentId, DateOnly date)
    {
        var key = new CellKey(agentId, date);
        return _overlay.TryGetValue(key, out var works)
            ? works
            : _snapshot.GetWorks(agentId, date);
    }

    public void AddWork(Guid agentId, DateOnly date, RecoveryWork work)
        => Mutable(new CellKey(agentId, date)).Add(work);

    public void RemoveWork(Guid agentId, DateOnly date, RecoveryWork work)
        => Mutable(new CellKey(agentId, date)).RemoveAll(w => ReferenceEquals(w, work));

    /// <summary>Returns an independent copy so a candidate placement can be tried without committing it.</summary>
    public WorkingGrid Branch()
    {
        var copy = new Dictionary<CellKey, List<RecoveryWork>>(_overlay.Count);
        foreach (var (key, list) in _overlay)
        {
            copy[key] = [.. list];
        }
        return new WorkingGrid(_snapshot, copy);
    }

    private List<RecoveryWork> Mutable(CellKey key)
    {
        if (!_overlay.TryGetValue(key, out var list))
        {
            list = [.. _snapshot.GetWorks(key.AgentId, key.Date)];
            _overlay[key] = list;
        }
        return list;
    }
}
