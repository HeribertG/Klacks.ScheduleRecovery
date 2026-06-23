// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleRecovery.Model;

/// <summary>
/// The bespoke lexicographic, integer-dominant objective value of a proposal:
/// Coverage &gt; Legality &gt; Perturbation &gt; Fairness. Every field is an integer so comparisons never
/// touch a float; the final per-candidate tie-break (a Guid) lives in the engine's move comparison, not
/// here. Smaller is better on every field.
/// </summary>
/// <param name="UncoveredCritical">Number of critical slots left uncovered (the dominant key)</param>
/// <param name="NewHardViolations">Number of newly introduced hard violations (collision/rest/consecutive/weekly)</param>
/// <param name="Perturbation">Weighted count of reassignment hops (w_type aggregated)</param>
/// <param name="WorstOffPerturbation">Largest per-agent reassignment load (worst-off fairness)</param>
public readonly record struct RecoveryObjective(
    int UncoveredCritical,
    int NewHardViolations,
    int Perturbation,
    int WorstOffPerturbation) : IComparable<RecoveryObjective>
{
    public static readonly RecoveryObjective Zero = new(0, 0, 0, 0);

    public int CompareTo(RecoveryObjective other)
    {
        var coverage = UncoveredCritical.CompareTo(other.UncoveredCritical);
        if (coverage != 0)
        {
            return coverage;
        }

        var legality = NewHardViolations.CompareTo(other.NewHardViolations);
        if (legality != 0)
        {
            return legality;
        }

        var perturbation = Perturbation.CompareTo(other.Perturbation);
        if (perturbation != 0)
        {
            return perturbation;
        }

        return WorstOffPerturbation.CompareTo(other.WorstOffPerturbation);
    }
}
