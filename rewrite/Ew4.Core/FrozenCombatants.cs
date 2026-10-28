using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Ew4.Core;

// Only exact engine-owned copies with read-only nested collections carry a proof.
// A record clone or deserialized combatant starts without one.
internal static class FrozenCombatants
{
    private sealed class Certificate { internal int Validated; }
    private static readonly ConditionalWeakTable<Combatant, Certificate> certificates = new();

    internal static Combatant Copy(Combatant source)
    {
        if (certificates.TryGetValue(source, out _)) return source;
        var copy = source with
        {
            Skills = Array.AsReadOnly(source.Skills.ToArray()),
            SkillSlots = Array.AsReadOnly(source.SkillSlots.ToArray()),
            Statuses = Array.AsReadOnly(source.Statuses.ToArray()),
            Profile = FrozenUnitProfiles.Copy(source.Profile)
        };
        certificates.Add(copy, new());
        return copy;
    }

    internal static bool IsValidated(Combatant value) => certificates.TryGetValue(value, out var certificate)
        && Volatile.Read(ref certificate.Validated) != 0;

    internal static void Validated(Combatant value)
    {
        if (certificates.TryGetValue(value, out var certificate)) Volatile.Write(ref certificate.Validated, 1);
    }
}
