using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Content.Server.Administration;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server.Objectives;
using Content.Server.Intentions.Runtime;
using Content.Server.Intentions.Snapshot;
using Content.Server.Intentions.UI;
using Content.Server.Intentions.Validation;
using Content.Server.Intentions.Waves;
using Content.Shared.Administration;
using Content.Shared.Intentions.Predicates;
using Content.Shared.Intentions.Runtime;
using Content.Shared.Intentions.Snapshot;
using Content.Shared.Intentions.UI;
using Content.Shared.Intentions.Validation;
using Content.Shared.Intentions.Waves;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Server.Player;

namespace Content.Server.Intentions.Debug;

[AdminCommand(AdminFlags.Debug)]
/// <summary>
/// Prints the current Intentions validation report.
/// </summary>
public sealed class IntentionsValidateCommand : IConsoleCommand
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public string Command => "intentions.validate";
    public string Description => "Runs Intentions content validation.";
    public string Help => "intentions.validate";

    /// <summary>
    /// Executes the validation report command.
    /// </summary>
    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 0)
        {
            shell.WriteError(Help);
            return;
        }

        var catalog = IntentionsDebugCommandHelpers.BuildCatalog(_prototypes);
        shell.WriteLine(IntentionsDebugFormatters.FormatValidation(catalog));
    }
}

[AdminCommand(AdminFlags.Debug)]
/// <summary>
/// Builds an immutable snapshot on demand and prints all captured facts.
/// </summary>
public sealed class IntentionsSnapshotPreviewCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entities = default!;

    public string Command => "intentions.snapshot.preview";
    public string Description => "Builds and prints an Intentions snapshot without running a wave.";
    public string Help => "intentions.snapshot.preview [start|refill] [waveId]";

    /// <summary>
    /// Executes the snapshot preview command.
    /// </summary>
    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length > 2)
        {
            shell.WriteError(Help);
            return;
        }

        var kind = "start";
        if (args.Length >= 1)
        {
            if (args[0] is not ("start" or "refill"))
            {
                shell.WriteError(Help);
                return;
            }

            kind = args[0];
        }

        var defaultWaveId = kind == "start" ? 0 : 1;
        if (!IntentionsDebugCommandHelpers.TryParseInt(args, 1, defaultWaveId, out var waveId))
        {
            shell.WriteError(Help);
            return;
        }

        var request = kind == "start"
            ? IntentionsSnapshotRequest.Start(waveId)
            : IntentionsSnapshotRequest.Refill(waveId);

        if (!IntentionsDebugCommandHelpers.TryBuildSnapshot(_entities, shell, request, out var snapshot))
            return;

        shell.WriteLine(IntentionsDebugFormatters.FormatSnapshotPreview(snapshot));
    }

    /// <summary>
    /// Provides shell completion for the snapshot kind argument.
    /// </summary>
    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(["start", "refill"], "<start|refill>")
            : CompletionResult.Empty;
    }
}

[AdminCommand(AdminFlags.Debug)]
/// <summary>
/// Prints one named dictionary of valid predicate values.
/// </summary>
public sealed class IntentionsDictionaryShowCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public string Command => "intentions.dictionary.show";
    public string Description => "Prints one Intentions predicate dictionary by name.";
    public string Help => "intentions.dictionary.show <GameMode|EventTags|Job|Department|Species|Sex|Traits|AntagRoles|AntagObjectiveTypes>";

    /// <summary>
    /// Executes the predicate dictionary dump command.
    /// </summary>
    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Help);
            return;
        }

        var service = new IntentionsPredicateDictionaryService(
            _prototypes,
            _entities.System<ObjectivesSystem>().Objectives);

        if (!service.TryGetDictionary(args[0], out var dictionary))
        {
            shell.WriteError(Help);
            return;
        }

        shell.WriteLine(IntentionsDebugFormatters.FormatPredicateDictionary(dictionary));
    }

    /// <summary>
    /// Provides shell completion for the dictionary name argument.
    /// </summary>
    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(IntentionsPredicateDictionaryService.DictionaryNames.ToArray(), "<dictionary>")
            : CompletionResult.Empty;
    }
}

[AdminCommand(AdminFlags.Debug)]
/// <summary>
/// Runs a dry start or refill wave without mutating runtime state.
/// </summary>
public sealed class IntentionsWavePreviewCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public string Command => "intentions.wave.preview";
    public string Description => "Runs an Intentions start/refill dry-run without commit.";
    public string Help => "intentions.wave.preview <start|refill> [waveId] [seed]";

    /// <summary>
    /// Executes the dry-run wave preview command.
    /// </summary>
    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 1 or > 3)
        {
            shell.WriteError(Help);
            return;
        }

        var kind = args[0];
        if (kind is not ("start" or "refill"))
        {
            shell.WriteError(Help);
            return;
        }

        if (!IntentionsDebugCommandHelpers.TryParseInt(args, 1, 1, out var waveId)
            || !IntentionsDebugCommandHelpers.TryParseNullableInt(args, 2, out var seed))
        {
            shell.WriteError(Help);
            return;
        }

        var request = kind == "start"
            ? IntentionsSnapshotRequest.Start(waveId)
            : IntentionsSnapshotRequest.Refill(waveId);

        if (!IntentionsDebugCommandHelpers.TryBuildSnapshot(_entities, shell, request, out var snapshot))
            return;

        var catalog = IntentionsDebugCommandHelpers.BuildCatalog(_prototypes);
        var registry = _entities.System<IntentionsRuntimeSystem>().Registry;
        var orchestrator = new IntentionsWaveOrchestrator();
        var result = kind == "start"
            ? orchestrator.RunStartWave(
                catalog,
                snapshot,
                new IntentionsStartWaveRequest(
                    waveId,
                    seed,
                    registry.AssignedScenarioIds,
                    IntentionsDebugCommandHelpers.RegistryAssignedPrimary(registry)))
            : orchestrator.RunRefillWave(
                catalog,
                snapshot,
                new IntentionsRefillWaveRequest(waveId, seed),
                registry);

        shell.WriteLine(IntentionsDebugFormatters.FormatWavePreview(result));
    }

    /// <summary>
    /// Provides shell completion for the preview command kind argument.
    /// </summary>
    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(["start", "refill"], "<start|refill>")
            : CompletionResult.Empty;
    }
}

[AdminCommand(AdminFlags.Debug)]
/// <summary>
/// Runs a scheduler-aware start or refill wave through the official distribution system.
/// </summary>
public sealed class IntentionsWaveRunCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entities = default!;

    public string Command => "intentions.wave.run";
    public string Description => "Runs an Intentions start/refill wave through the automatic distribution scheduler.";
    public string Help => "intentions.wave.run <start|refill> [seed]";

    /// <summary>
    /// Executes the safe scheduler-aware wave run command.
    /// </summary>
    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (!IntentionsDebugCommandHelpers.TryParseSafeWaveRunArgs(shell, args, out var kind, out var seed))
            return;

        var distribution = _entities.System<IntentionsDistributionSystem>();
        shell.WriteLine($"Intentions safe {kind} wave requested. seed={seed?.ToString(CultureInfo.InvariantCulture) ?? "auto"}");

        var runResult = kind == "start"
            ? distribution.RunStartWaveNow(seed)
            : distribution.RunRefillWaveNow(seed);

        if (runResult.Outcome == IntentionsDistributionManualRunOutcome.Rejected)
            shell.WriteError(runResult.Message);
        else
            shell.WriteLine(runResult.Message);

        if (runResult.WaveResult is not null)
        {
            shell.WriteLine(IntentionsDebugFormatters.FormatWavePreview(runResult.WaveResult));
            shell.WriteLine(IntentionsDebugFormatters.FormatRegistryDump(_entities.System<IntentionsRuntimeSystem>().Registry));
        }

        shell.WriteLine(IntentionsDebugFormatters.FormatWaveTimer(runResult.ScheduleStatus));
    }

    /// <summary>
    /// Provides shell completion for the safe wave run command kind argument.
    /// </summary>
    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(["start", "refill"], "<start|refill>")
            : CompletionResult.Empty;
    }
}

[AdminCommand(AdminFlags.Debug)]
/// <summary>
/// Runs a low-level committed wave directly for tests and diagnostics without scheduler coordination.
/// </summary>
public sealed class IntentionsTestWaveRunCommand : IConsoleCommand
{
    private static readonly SoundSpecifier AssignmentSound =
        new SoundPathSpecifier("/Audio/Machines/quickbeep.ogg", AudioParams.Default.WithVolume(-2f));

    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public string Command => "intentions.test.wave.run";
    public string Description => "Runs a low-level Intentions start/refill wave directly without synchronizing scheduler timers.";
    public string Help => "intentions.test.wave.run <start|refill> [waveId] [seed]";

    /// <summary>
    /// Executes the low-level committed test wave command.
    /// </summary>
    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 1 or > 3)
        {
            shell.WriteError(Help);
            return;
        }

        var kind = args[0];
        if (kind is not ("start" or "refill"))
        {
            shell.WriteError(Help);
            return;
        }

        if (!IntentionsDebugCommandHelpers.TryParseInt(args, 1, 1, out var waveId)
            || !IntentionsDebugCommandHelpers.TryParseNullableInt(args, 2, out var seed))
        {
            shell.WriteError(Help);
            return;
        }

        var request = kind == "start"
            ? IntentionsSnapshotRequest.Start(waveId)
            : IntentionsSnapshotRequest.Refill(waveId);

        shell.WriteLine($"Intentions {kind} wave starting. waveId={waveId} seed={seed?.ToString(CultureInfo.InvariantCulture) ?? "auto"}");

        if (kind == "refill")
        {
            var lifecycleResults = _entities.System<IntentionsLifecycleSystem>().ReconcileBeforeRefillNow();
            foreach (var lifecycleResult in lifecycleResults.Where(result => !result.IsSuccess))
            {
                var scenarioUid = lifecycleResult.ScenarioUid?.Value.ToString(CultureInfo.InvariantCulture) ?? "-";
                shell.WriteError($"Refill lifecycle reconciliation failed for scenarioUid={scenarioUid}: {lifecycleResult.FailureReason}");
            }
        }

        if (!IntentionsDebugCommandHelpers.TryBuildSnapshot(_entities, shell, request, out var snapshot))
            return;

        var catalog = IntentionsDebugCommandHelpers.BuildCatalog(_prototypes);
        var registry = _entities.System<IntentionsRuntimeSystem>().Registry;
        var orchestrator = new IntentionsWaveOrchestrator();
        var commitService = new IntentionsCommitService();
        var result = kind == "start"
            ? orchestrator.RunStartWaveAndCommit(
                catalog,
                snapshot,
                new IntentionsStartWaveRequest(
                    waveId,
                    seed,
                    registry.AssignedScenarioIds,
                    IntentionsDebugCommandHelpers.RegistryAssignedPrimary(registry)),
                registry,
                commitService)
            : orchestrator.RunRefillWaveAndCommit(
                catalog,
                snapshot,
                new IntentionsRefillWaveRequest(waveId, seed),
                registry,
                commitService);

        NotifyAssignedCharacters(result);

        var ui = _entities.System<IntentionsUiSystem>();
        foreach (var mindId in result.Context.SuccessfulBuilds
                     .SelectMany(build => build.BuiltSlots)
                     .Select(slot => slot.MindId)
                     .Distinct())
        {
            ui.RefreshMind(mindId);
        }

        shell.WriteLine($"Intentions {kind} wave finished. waveId={waveId} status={result.Context.Status} successfulBuilds={result.Context.SuccessfulBuilds.Count} rejectReasons={result.Context.RejectReasons.Count}");
        shell.WriteLine(IntentionsDebugFormatters.FormatWavePreview(result));
        shell.WriteLine(IntentionsDebugFormatters.FormatRegistryDump(registry));
    }

    /// <summary>
    /// Provides shell completion for the test wave command kind argument.
    /// </summary>
    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(["start", "refill"], "<start|refill>")
            : CompletionResult.Empty;
    }

    /// <summary>
    /// Plays the assignment sound once for every unique mind touched by the committed test wave.
    /// </summary>
    private void NotifyAssignedCharacters(StartWaveResult result)
    {
        var audio = _entities.System<SharedAudioSystem>();
        var notifiedMinds = new HashSet<EntityUid>();
        foreach (var slot in result.Context.SuccessfulBuilds.SelectMany(build => build.BuiltSlots))
        {
            if (!notifiedMinds.Add(slot.MindId) || !_entities.EntityExists(slot.OwnerEntityUid))
                continue;

            audio.PlayEntity(AssignmentSound, slot.OwnerEntityUid, slot.OwnerEntityUid);
        }
    }
}

[AdminCommand(AdminFlags.Debug)]
/// <summary>
/// Prints the current automatic wave timer status.
/// </summary>
public sealed class IntentionsWaveTimerCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entities = default!;

    public string Command => "intentions.wave.timer";
    public string Description => "Shows scheduling status and remaining time until the next automatic refill wave.";
    public string Help => "intentions.wave.timer";

    /// <summary>
    /// Executes the wave timer command.
    /// </summary>
    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 0)
        {
            shell.WriteError(Help);
            return;
        }

        var distribution = _entities.System<IntentionsDistributionSystem>();
        shell.WriteLine(IntentionsDebugFormatters.FormatWaveTimer(distribution.GetScheduleStatus()));
    }
}

[AdminCommand(AdminFlags.Debug)]
/// <summary>
/// Runs one pure builder pass for a specific scenario template.
/// </summary>
public sealed class IntentionsScenarioBuildCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public string Command => "intentions.scenario.build";
    public string Description => "Runs one Intentions scenario builder pass without commit.";
    public string Help => "intentions.scenario.build <scenarioId> [waveId] [seed]";

    /// <summary>
    /// Executes the single-scenario dry builder command.
    /// </summary>
    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 1 or > 3)
        {
            shell.WriteError(Help);
            return;
        }

        var scenarioId = args[0];
        if (!IntentionsDebugCommandHelpers.TryParseInt(args, 1, 1, out var waveId)
            || !IntentionsDebugCommandHelpers.TryParseNullableInt(args, 2, out var seed))
        {
            shell.WriteError(Help);
            return;
        }

        if (!IntentionsDebugCommandHelpers.TryBuildSnapshot(_entities, shell, IntentionsSnapshotRequest.Start(waveId), out var snapshot))
            return;

        var catalog = IntentionsDebugCommandHelpers.BuildCatalog(_prototypes);
        if (!catalog.ValidScenarios.TryGetValue(scenarioId, out var scenario))
        {
            shell.WriteError($"Unknown or invalid scenarioTemplate: {scenarioId}");
            return;
        }

        if (!catalog.ValidCategories.TryGetValue(scenario.Template.Category, out var category))
        {
            shell.WriteError($"Scenario category is missing or invalid: {scenario.Template.Category}");
            return;
        }

        var predicateResult = new IntentionsPredicateEngine().EvaluateGlobal(scenario.Template.GlobalPredicates, snapshot);
        if (!predicateResult.IsMatch)
        {
            shell.WriteLine($"Global predicates rejected scenario {scenario.Template.ID}.");
            foreach (var reject in predicateResult.RejectReasons)
                shell.WriteLine($"- {reject.Code} {reject.Scope}.{reject.Field} {reject.Operator}: {reject.Message}");
            return;
        }

        var actualSeed = seed ?? IntentionsDeterministicRandom.BuildSeed(
            waveId,
            snapshot.SnapshotId,
            snapshot.RoundFacts.GameMode,
            snapshot.RoundFacts.StationTime.Ticks);
        var registry = _entities.System<IntentionsRuntimeSystem>().Registry;
        var context = new DistributionWaveContext(
            waveId,
            snapshot.SnapshotId,
            actualSeed,
            snapshot.RoundFacts.StationTime,
            snapshot.RoundFacts.CrewCount,
            registry.AssignedScenarioIds,
            IntentionsDebugCommandHelpers.RegistryAssignedPrimary(registry),
            IntentionsWaveKind.Start,
            snapshot.RoundFacts.CrewCount)
        {
            Status = DistributionWaveStatus.Running,
        };
        var categoryState = new CategoryWaveState(
            scenario.Template.Category,
            targetQuota: 1,
            IntentionsQuotaCalculator.CalculateEffectiveMaxPrimaryPerMind(category, snapshot.RoundFacts.GameMode),
            desiredQuota: 1,
            existingActiveFrozenCount: 0,
            refillTarget: 1);
        var build = new IntentionsScenarioBuilder().TryBuildScenario(
            scenario,
            snapshot,
            context,
            categoryState,
            new IntentionsDeterministicRandom(actualSeed));

        shell.WriteLine(IntentionsDebugFormatters.FormatScenarioBuild(build));
    }
}

[AdminCommand(AdminFlags.Debug)]
/// <summary>
/// Dumps the current runtime registry and index diagnostics.
/// </summary>
public sealed class IntentionsRegistryDumpCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entities = default!;

    public string Command => "intentions.registry.dump";
    public string Description => "Prints the Intentions runtime registry and index consistency report.";
    public string Help => "intentions.registry.dump";

    /// <summary>
    /// Executes the registry dump command.
    /// </summary>
    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 0)
        {
            shell.WriteError(Help);
            return;
        }

        shell.WriteLine(IntentionsDebugFormatters.FormatRegistryDump(_entities.System<IntentionsRuntimeSystem>().Registry));
    }
}

[AdminCommand(AdminFlags.Debug)]
/// <summary>
/// Prints the Intentions read-model for one mind id.
/// </summary>
public sealed class IntentionsMindShowCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public string Command => "intentions.mind.show";
    public string Description => "Prints Intentions runtime read-model for one mind id.";
    public string Help => "intentions.mind.show <mindEntityUid> [player|admin]";

    /// <summary>
    /// Executes the mind-show command.
    /// </summary>
    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 1 or > 2 || !IntentionsDebugCommandHelpers.TryParseEntityUid(args[0], out var mindId))
        {
            shell.WriteError(Help);
            return;
        }

        var mode = IntentionsEuiMode.Admin;
        if (args.Length == 2)
        {
            if (args[1] == "player")
                mode = IntentionsEuiMode.Player;
            else if (args[1] != "admin")
            {
                shell.WriteError(Help);
                return;
            }
        }

        var catalog = IntentionsDebugCommandHelpers.BuildCatalog(_prototypes);
        var registry = _entities.System<IntentionsRuntimeSystem>().Registry;
        var now = IntentionsDebugCommandHelpers.GetRoundTime(_entities);
        var state = new IntentionsQueryService().GetIntentionsForMind(
            catalog,
            registry,
            mindId,
            now,
            mode,
            $"mind:{mindId.Id}");

        shell.WriteLine(IntentionsDebugFormatters.FormatMindShow(state));
    }

    /// <summary>
    /// Provides shell completion for the output mode argument.
    /// </summary>
    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 2
            ? CompletionResult.FromHintOptions(["admin", "player"], "<admin|player>")
            : CompletionResult.Empty;
    }
}

[AdminCommand(AdminFlags.Debug)]
/// <summary>
/// Dumps the timer reveal index used for hidden intentions.
/// </summary>
public sealed class IntentionsRevealDumpCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entities = default!;

    public string Command => "intentions.reveal.dump";
    public string Description => "Prints hidden Intentions reveal timers.";
    public string Help => "intentions.reveal.dump";

    /// <summary>
    /// Executes the reveal dump command.
    /// </summary>
    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 0)
        {
            shell.WriteError(Help);
            return;
        }

        var registry = _entities.System<IntentionsRuntimeSystem>().Registry;
        shell.WriteLine(IntentionsDebugFormatters.FormatRevealDump(registry, IntentionsDebugCommandHelpers.GetRoundTime(_entities)));
    }
}

[AdminCommand(AdminFlags.Debug)]
/// <summary>
/// Prints category quota diagnostics from the latest or requested committed wave.
/// </summary>
public sealed class IntentionsCategoryQuotaCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entities = default!;

    public string Command => "intentions.category.quota";
    public string Description => "Prints Intentions category quota state for a committed wave context.";
    public string Help => "intentions.category.quota [waveId]";

    /// <summary>
    /// Executes the category quota command.
    /// </summary>
    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length > 1)
        {
            shell.WriteError(Help);
            return;
        }

        var registry = _entities.System<IntentionsRuntimeSystem>().Registry;
        DistributionWaveContext? context = null;

        if (args.Length == 1)
        {
            if (!int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var waveId))
            {
                shell.WriteError(Help);
                return;
            }

            registry.WaveContextByWaveId.TryGetValue(waveId, out context);
        }
        else if (registry.WaveContextByWaveId.Count > 0)
        {
            context = registry.WaveContextByWaveId
                .OrderByDescending(pair => pair.Key)
                .First()
                .Value;
        }

        shell.WriteLine(IntentionsDebugFormatters.FormatCategoryQuota(context));
    }
}

[AdminCommand(AdminFlags.Debug)]
/// <summary>
/// Prints a lightweight trace for one committed wave id.
/// </summary>
public sealed class IntentionsLogsTraceCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entities = default!;

    public string Command => "intentions.logs.trace";
    public string Description => "Prints Intentions wave trace information by wave id.";
    public string Help => "intentions.logs.trace <waveId>";

    /// <summary>
    /// Executes the wave trace command.
    /// </summary>
    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1 || !int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var waveId))
        {
            shell.WriteError(Help);
            return;
        }

        shell.WriteLine(IntentionsDebugFormatters.FormatLogsTrace(_entities.System<IntentionsRuntimeSystem>().Registry, waveId));
    }
}

[AdminCommand(AdminFlags.Debug)]
/// <summary>
/// Removes every runtime scenario and intention by resetting the Intentions registry.
/// </summary>
public sealed class IntentionsRuntimeClearAllCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entities = default!;

    public string Command => "intentions.runtime.clearall";
    public string Description => "Deletes every runtime Intentions scenario and intention and clears uniqueness/quota indexes.";
    public string Help => "intentions.runtime.clearall";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 0)
        {
            shell.WriteError(Help);
            return;
        }

        var service = new IntentionsRuntimeAdminService();
        var runtime = _entities.System<IntentionsRuntimeSystem>();
        var result = service.ClearAllScenarios(runtime);

        var ui = _entities.System<IntentionsUiSystem>();
        foreach (var mindId in result.AffectedMindIds)
        {
            ui.RefreshMind(mindId);
        }

        shell.WriteLine(
            $"Removed {result.RemovedScenarioCount.ToString(CultureInfo.InvariantCulture)} scenarios and {result.RemovedIntentionCount.ToString(CultureInfo.InvariantCulture)} intentions from runtime.");
    }
}

[AdminCommand(AdminFlags.Debug)]
/// <summary>
/// Physically removes one runtime scenario and all of its committed intentions.
/// </summary>
public sealed class IntentionsRuntimeRemoveCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entities = default!;

    public string Command => "intentions.runtime.remove";
    public string Description => "Deletes one runtime Intentions scenario by scenario uid and owner mind id.";
    public string Help => "intentions.runtime.remove <scenarioUid> <ownerMindId>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2
            || !IntentionsDebugCommandHelpers.TryParseScenarioUid(args[0], out var scenarioUid)
            || !IntentionsDebugCommandHelpers.TryParseEntityUid(args[1], out var ownerMindId))
        {
            shell.WriteError(Help);
            return;
        }

        var registry = _entities.System<IntentionsRuntimeSystem>().Registry;
        var result = new IntentionsRuntimeAdminService().RemoveScenario(registry, scenarioUid, ownerMindId);
        if (!result.IsSuccess)
        {
            shell.WriteError(result.Message);
            return;
        }

        var ui = _entities.System<IntentionsUiSystem>();
        foreach (var mindId in result.AffectedMindIds)
        {
            ui.RefreshMind(mindId);
        }

        shell.WriteLine(result.Message);
    }
}

[AdminCommand(AdminFlags.Debug)]
/// <summary>
/// Force-assigns one validated scenario template to explicit mind ids while bypassing quota checks.
/// </summary>
public sealed class IntentionsRuntimeAssignCommand : IConsoleCommand
{
    private const string IgnorePredicatesFlag = "--ignore-predicates";

    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IPlayerManager _player = default!;

    public string Command => "intentions.runtime.assign";
    public string Description => "Force-assigns one Intentions scenario template to explicit minds in non-bound slotBuildOrder order, with an optional predicate-bypass flag.";
    public string Help => "intentions.runtime.assign [--ignore-predicates] <scenarioTemplateId> <mind-or-dash>...";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 1)
        {
            shell.WriteError(Help);
            return;
        }

        var ignorePredicates = false;
        var scenarioIndex = 0;

        if (string.Equals(args[0], IgnorePredicatesFlag, StringComparison.Ordinal))
        {
            ignorePredicates = true;
            scenarioIndex = 1;
        }
        else if (args[0].StartsWith("--", StringComparison.Ordinal))
        {
            shell.WriteError(Help);
            return;
        }

        if (args.Length <= scenarioIndex)
        {
            shell.WriteError(Help);
            return;
        }

        var scenarioTemplateId = args[scenarioIndex];
        var slotArgs = args.Skip(scenarioIndex + 1).ToArray();
        var runtime = _entities.System<IntentionsRuntimeSystem>();
        var waveId = runtime.NextManualWaveId();

        if (!IntentionsDebugCommandHelpers.TryBuildSnapshot(
                _entities,
                shell,
                IntentionsSnapshotRequest.Refill(waveId),
                out var snapshot))
        {
            return;
        }

        var catalog = IntentionsDebugCommandHelpers.BuildCatalog(_prototypes);
        var service = new IntentionsRuntimeAdminService(
            entities: _entities,
            player: _player,
            chat: _chat);
        var result = service.TryForceAssignScenario(
            catalog,
            snapshot,
            runtime.Registry,
            scenarioTemplateId,
            slotArgs,
            waveId,
            ignorePredicates: ignorePredicates);

        if (!result.IsSuccess)
        {
            shell.WriteError(result.Message);
            if (result.BuildResult is not null
                || result.GlobalPredicateResult is not null
                || !string.IsNullOrWhiteSpace(result.ExpectedArgumentLayout))
            {
                shell.WriteLine(IntentionsDebugFormatters.FormatRuntimeAssign(result));
            }

            return;
        }

        var ui = _entities.System<IntentionsUiSystem>();
        foreach (var mindId in result.AffectedMindIds)
        {
            ui.RefreshMind(mindId);
        }

        shell.WriteLine(result.Message);
        shell.WriteLine(IntentionsDebugFormatters.FormatRuntimeAssign(result));
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        var catalog = IntentionsDebugCommandHelpers.BuildCatalog(_prototypes);
        var scenarioIds = catalog.ValidScenarioOrder.Count > 0
            ? catalog.ValidScenarioOrder.ToArray()
            : catalog.ValidScenarios.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray();

        if (args.Length == 1)
        {
            var options = scenarioIds
                .Prepend(IgnorePredicatesFlag)
                .ToArray();

            return CompletionResult.FromHintOptions(options, "[--ignore-predicates] <scenarioTemplateId>");
        }

        if (args.Length == 2
            && string.Equals(args[0], IgnorePredicatesFlag, StringComparison.Ordinal))
        {
            return CompletionResult.FromHintOptions(scenarioIds, "<scenarioTemplateId>");
        }

        return CompletionResult.Empty;
    }
}

[AdminCommand(AdminFlags.Debug)]
/// <summary>
/// Forces one hidden runtime intention, or all hidden runtime intentions for one mind, to become visible immediately.
/// </summary>
public sealed class IntentionsRuntimeRevealCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entities = default!;

    public string Command => "intentions.runtime.reveal";
    public string Description => "Reveals one hidden runtime intention, or every active hidden runtime intention for one mind.";
    public string Help => "intentions.runtime.reveal one <intentionUid> <ownerMindId>\nintentions.runtime.reveal all <ownerMindId>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteError(Help);
            return;
        }

        var runtime = _entities.System<IntentionsRuntimeSystem>();
        var service = new IntentionsRuntimeAdminService();
        var now = IntentionsDebugCommandHelpers.GetRoundTime(_entities);

        RevealHiddenIntentionsRuntimeResult result;
        switch (args[0])
        {
            case "one":
                if (args.Length != 3
                    || !IntentionsDebugCommandHelpers.TryParseIntentionUid(args[1], out var intentionUid)
                    || !IntentionsDebugCommandHelpers.TryParseEntityUid(args[2], out var ownerMindId))
                {
                    shell.WriteError(Help);
                    return;
                }

                result = service.RevealHiddenIntention(runtime.Registry, intentionUid, ownerMindId, now);
                break;

            case "all":
                if (args.Length != 2
                    || !IntentionsDebugCommandHelpers.TryParseEntityUid(args[1], out var allMindId))
                {
                    shell.WriteError(Help);
                    return;
                }

                result = service.RevealAllHiddenIntentionsForMind(runtime.Registry, allMindId, now);
                break;

            default:
                shell.WriteError(Help);
                return;
        }

        if (!result.IsSuccess)
        {
            shell.WriteError(result.Message);
            shell.WriteLine(IntentionsDebugFormatters.FormatRuntimeReveal(result));
            return;
        }

        var ui = _entities.System<IntentionsUiSystem>();
        foreach (var mindId in result.AffectedMindIds)
        {
            ui.RefreshMind(mindId);
        }

        var popup = _entities.System<SharedPopupSystem>();
        foreach (var revealGroup in result.RevealedIntentions.GroupBy(reveal => reveal.MindId))
        {
            var ownerEntity = revealGroup
                .Select(reveal => reveal.OwnerEntityUid)
                .FirstOrDefault(uid => _entities.EntityExists(uid));

            if (_entities.EntityExists(ownerEntity))
                popup.PopupEntity(Loc.GetString("intentions-ui-reveal-notification"), ownerEntity, ownerEntity);
        }

        shell.WriteLine(result.Message);
        shell.WriteLine(IntentionsDebugFormatters.FormatRuntimeReveal(result));
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(["one", "all"], "<one|all>")
            : CompletionResult.Empty;
    }
}

/// <summary>
/// Shared parsing and runtime helper methods used by the debug console commands.
/// </summary>
internal static class IntentionsDebugCommandHelpers
{
    /// <summary>
    /// Parses the scheduler-aware wave run arguments and rejects the removed legacy syntax.
    /// </summary>
    public static bool TryParseSafeWaveRunArgs(
        IConsoleShell shell,
        string[] args,
        out string kind,
        out int? seed)
    {
        kind = string.Empty;
        seed = null;
        var parsedSeed = 0;

        if (args.Length == 3)
        {
            shell.WriteError("Legacy syntax removed. Use 'intentions.wave.run <start|refill> [seed]' or 'intentions.test.wave.run <start|refill> [waveId] [seed]'.");
            return false;
        }

        if (args.Length is < 1 or > 2)
        {
            shell.WriteError("intentions.wave.run <start|refill> [seed]");
            return false;
        }

        kind = args[0];
        if (kind is not ("start" or "refill"))
        {
            shell.WriteError("intentions.wave.run <start|refill> [seed]");
            return false;
        }

        if (args.Length == 2
            && !int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedSeed))
        {
            shell.WriteError("intentions.wave.run <start|refill> [seed]");
            return false;
        }

        if (args.Length == 2)
            seed = parsedSeed;

        return true;
    }

    /// <summary>
    /// Builds the currently valid Intentions catalog from loaded prototypes.
    /// </summary>
    public static ValidationCatalog BuildCatalog(IPrototypeManager prototypes)
    {
        return new IntentionsValidationService(prototypes).ValidateAll();
    }

    /// <summary>
    /// Builds a snapshot for a debug command and prints any issues on failure.
    /// </summary>
    public static bool TryBuildSnapshot(
        IEntityManager entities,
        IConsoleShell shell,
        IntentionsSnapshotRequest request,
        out IntentionsSnapshot snapshot)
    {
        var result = entities.System<IntentionsSnapshotService>().BuildSnapshot(request);
        if (result.IsSuccess && result.Snapshot is { } built)
        {
            snapshot = built;
            return true;
        }

        snapshot = default!;
        shell.WriteError("Intentions snapshot failed.");
        foreach (var issue in result.Issues)
            shell.WriteError($"- [{issue.Severity}] {issue.Code} mind={issue.MindId?.Id.ToString(CultureInfo.InvariantCulture) ?? "-"} path={issue.Path ?? "-"} {issue.Message}");

        return false;
    }

    /// <summary>
    /// Copies runtime primary-assignment counters into the immutable shape expected by wave inputs.
    /// </summary>
    public static IReadOnlyDictionary<EntityUid, IReadOnlyDictionary<string, int>> RegistryAssignedPrimary(IntentionsRuntimeRegistry registry)
    {
        return registry.AssignedPrimaryByMind.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyDictionary<string, int>) pair.Value.ToImmutableDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
    }

    /// <summary>
    /// Returns the current round time through the live game ticker.
    /// </summary>
    public static TimeSpan GetRoundTime(IEntityManager entities)
    {
        return entities.System<GameTicker>().RoundDuration();
    }

    /// <summary>
    /// Parses an integer argument with a fallback value when the index is absent.
    /// </summary>
    public static bool TryParseInt(string[] args, int index, int fallback, out int value)
    {
        if (args.Length <= index)
        {
            value = fallback;
            return true;
        }

        return int.TryParse(args[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Parses an optional integer argument.
    /// </summary>
    public static bool TryParseNullableInt(string[] args, int index, out int? value)
    {
        value = null;
        if (args.Length <= index)
            return true;

        if (!int.TryParse(args[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return false;

        value = parsed;
        return true;
    }

    /// <summary>
    /// Parses an entity uid from the plain integer format used by the debug commands.
    /// </summary>
    public static bool TryParseEntityUid(string value, out EntityUid entityUid)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            entityUid = default;
            return false;
        }

        entityUid = new EntityUid(id);
        return true;
    }

    /// <summary>
    /// Parses a runtime scenario uid from the plain integer format used by the debug commands.
    /// </summary>
    public static bool TryParseScenarioUid(string value, out ScenarioInstanceUid scenarioUid)
    {
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            scenarioUid = default;
            return false;
        }

        scenarioUid = new ScenarioInstanceUid(id);
        return true;
    }

    /// <summary>
    /// Parses a runtime intention uid from the plain integer format used by the debug commands.
    /// </summary>
    public static bool TryParseIntentionUid(string value, out IntentionInstanceUid intentionUid)
    {
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            intentionUid = default;
            return false;
        }

        intentionUid = new IntentionInstanceUid(id);
        return true;
    }
}
