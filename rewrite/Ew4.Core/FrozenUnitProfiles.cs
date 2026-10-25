using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Ew4.Core;

// Certificates belong to exact engine-owned objects. Record clones and imported
// definitions cannot inherit them, even when their content IDs are identical.
internal static class FrozenUnitProfiles
{
    private sealed class Certificate { internal int Validated; }
    private static readonly ConditionalWeakTable<UnitProfile, Certificate> certificates = new();

    internal static UnitProfile Copy(UnitProfile source)
    {
        if (certificates.TryGetValue(source, out _)) return source;
        var copy = source with
        {
            SkillSlots = System.Array.AsReadOnly(source.SkillSlots.ToArray()),
            LegacySkillOverrides = System.Array.AsReadOnly(source.LegacySkillOverrides.ToArray()),
            AttackBands = System.Array.AsReadOnly(source.AttackBands.ToArray()),
            Traits = System.Array.AsReadOnly(source.Traits.ToArray()),
            DamageMultipliers = new ReadOnlyDictionary<string, float>(source.DamageMultipliers
                .ToDictionary(p => p.Key, p => p.Value, System.StringComparer.Ordinal))
        };
        certificates.Add(copy, new());
        return copy;
    }

    internal static bool IsValidated(UnitProfile profile) => certificates.TryGetValue(profile, out var certificate)
        && Volatile.Read(ref certificate.Validated) != 0;

    internal static void Validated(UnitProfile profile)
    {
        if (certificates.TryGetValue(profile, out var certificate)) Volatile.Write(ref certificate.Validated, 1);
    }
}
