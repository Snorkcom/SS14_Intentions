using System.Collections.Immutable;
using System.Linq;
using Content.Server.GameTicking.Presets;
using Content.Server.Holiday;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Roles;
using Content.Shared.Traits;
using Robust.Shared.Prototypes;

namespace Content.Server.Intentions.Debug;

/// <summary>
/// Collects stable dictionary values used when authoring Intentions predicates.
/// </summary>
public sealed class IntentionsPredicateDictionaryService
{
    private static readonly ImmutableArray<string> KnownDictionaries =
    [
        "GameMode",
        "EventTags",
        "Job",
        "Department",
        "Species",
        "Sex",
        "Traits",
        "AntagRoles",
        "AntagObjectiveTypes",
    ];

    private readonly IPrototypeManager _prototypes;
    private readonly Func<IEnumerable<string>> _objectiveIds;

    /// <summary>
    /// Initializes a read-only dictionary collector backed by loaded prototypes.
    /// </summary>
    public IntentionsPredicateDictionaryService(
        IPrototypeManager prototypes,
        Func<IEnumerable<string>>? objectiveIds = null)
    {
        _prototypes = prototypes;
        _objectiveIds = objectiveIds ?? (() => []);
    }

    /// <summary>
    /// All dictionary names supported by the debug tooling.
    /// </summary>
    public static IReadOnlyList<string> DictionaryNames => KnownDictionaries;

    /// <summary>
    /// Resolves one named dictionary into a stable set of predicate-ready ids.
    /// </summary>
    public bool TryGetDictionary(string name, out IntentionsPredicateDictionary dictionary)
    {
        dictionary = default!;

        var actualName = KnownDictionaries.FirstOrDefault(candidate =>
            string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase));
        if (actualName is null)
            return false;

        dictionary = actualName switch
        {
            "GameMode" => BuildPrototypeDictionary<GamePresetPrototype>(
                actualName,
                "Loaded gamePreset prototypes."),
            "EventTags" => BuildPrototypeDictionary<HolidayPrototype>(
                actualName,
                "Loaded holiday prototypes.",
                "Snapshot preview includes only the holidays that are active right now."),
            "Job" => BuildPrototypeDictionary<JobPrototype>(
                actualName,
                "Loaded job prototypes."),
            "Department" => BuildPrototypeDictionary<DepartmentPrototype>(
                actualName,
                "Loaded department prototypes."),
            "Species" => BuildPrototypeDictionary<SpeciesPrototype>(
                actualName,
                "Loaded species prototypes."),
            "Sex" => BuildSexDictionary(),
            "Traits" => BuildPrototypeDictionary<TraitPrototype>(
                actualName,
                "Loaded trait prototypes."),
            "AntagRoles" => BuildPrototypeDictionary<AntagPrototype>(
                actualName,
                "Loaded antag prototypes."),
            "AntagObjectiveTypes" => BuildObjectiveDictionary(),
            _ => throw new InvalidOperationException($"Unsupported predicate dictionary '{actualName}'."),
        };

        return true;
    }

    /// <summary>
    /// Builds one dictionary directly from prototype ids.
    /// </summary>
    private IntentionsPredicateDictionary BuildPrototypeDictionary<TPrototype>(
        string name,
        string source,
        string? note = null)
        where TPrototype : class, IPrototype
    {
        var values = _prototypes.EnumeratePrototypes<TPrototype>()
            .Select(prototype => prototype.ID)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToImmutableArray();

        return new IntentionsPredicateDictionary(name, source, values, note);
    }

    /// <summary>
    /// Builds the sex dictionary from the fixed shared enum values.
    /// </summary>
    private static IntentionsPredicateDictionary BuildSexDictionary()
    {
        var values = Enum.GetNames<Sex>()
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToImmutableArray();

        return new IntentionsPredicateDictionary(
            "Sex",
            "Shared Sex enum values.",
            values);
    }

    /// <summary>
    /// Builds the objective-type dictionary from the objective system's prototype enumeration.
    /// </summary>
    private IntentionsPredicateDictionary BuildObjectiveDictionary()
    {
        var values = _objectiveIds()
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToImmutableArray();

        return new IntentionsPredicateDictionary(
            "AntagObjectiveTypes",
            "Objective entity prototypes with ObjectiveComponent.",
            values);
    }
}

/// <summary>
/// One named set of stable ids exposed by the predicate dictionary tooling.
/// </summary>
public sealed record IntentionsPredicateDictionary(
    string Name,
    string Source,
    ImmutableArray<string> Values,
    string? Note = null);
