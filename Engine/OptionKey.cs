// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleRecovery.Engine;

/// <summary>
/// The total order the engine uses to pick the single best option for one demand. It encodes the
/// lexicographic objective (Coverage &gt; Legality &gt; Perturbation) followed by the ported
/// find_replacement ranking (preferred first, then larger target-hours deficit) and a final stable Guid
/// tie-break, so the choice is fully deterministic. Every numeric field except the decimal deficit is an
/// integer; the deficit is exact (decimal, not float) and never the last key. Smaller compares first on
/// every field except the deficit, which prefers the larger value. Hard constraints (min-pause,
/// consecutive, weekly) fold into <see cref="Violations"/>; there is no separate soft-conflict signal,
/// because the engine has no source for one distinct from the committable hard violations.
/// </summary>
/// <param name="Coverage">1 if the option leaves a critical slot uncovered, else 0 (dominant)</param>
/// <param name="Violations">Count of newly introduced committable hard violations</param>
/// <param name="Perturbation">Weighted reassignment-hop cost (w_type aggregated)</param>
/// <param name="PreferenceRank">0 if the receiving agent prefers the shift, else 1</param>
/// <param name="TargetHoursDeficit">Receiving agent's period deficit; larger ranks higher (fairness)</param>
/// <param name="PrimaryGuid">Receiving agent id (stable tie-break)</param>
/// <param name="SecondaryGuid">Secondary agent id, e.g. the swap recipient (stable tie-break)</param>
internal readonly record struct OptionKey(
    int Coverage,
    int Violations,
    int Perturbation,
    int PreferenceRank,
    decimal TargetHoursDeficit,
    Guid PrimaryGuid,
    Guid SecondaryGuid) : IComparable<OptionKey>
{
    public int CompareTo(OptionKey other)
    {
        var coverage = Coverage.CompareTo(other.Coverage);
        if (coverage != 0)
        {
            return coverage;
        }
        var violations = Violations.CompareTo(other.Violations);
        if (violations != 0)
        {
            return violations;
        }
        var perturbation = Perturbation.CompareTo(other.Perturbation);
        if (perturbation != 0)
        {
            return perturbation;
        }
        var preference = PreferenceRank.CompareTo(other.PreferenceRank);
        if (preference != 0)
        {
            return preference;
        }
        var deficit = other.TargetHoursDeficit.CompareTo(TargetHoursDeficit);
        if (deficit != 0)
        {
            return deficit;
        }
        var primary = PrimaryGuid.CompareTo(other.PrimaryGuid);
        if (primary != 0)
        {
            return primary;
        }
        return SecondaryGuid.CompareTo(other.SecondaryGuid);
    }
}
