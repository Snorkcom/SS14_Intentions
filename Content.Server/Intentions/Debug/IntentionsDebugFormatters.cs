using System.Globalization;
using System.Linq;
using System.Text;
using Content.Server.Intentions.Runtime;
using Content.Server.Intentions.Waves;
using Content.Shared.Intentions.Runtime;
using Content.Shared.Intentions.Snapshot;
using Content.Shared.Intentions.UI;
using Content.Shared.Intentions.Validation;
using Content.Shared.Intentions.Waves;

namespace Content.Server.Intentions.Debug;

/// <summary>
/// Formats debug-oriented text dumps for Intentions validation, waves, runtime state, and read-models.
/// </summary>
public static class IntentionsDebugFormatters
{
    /// <summary>
    /// Formats the full validation catalog and every collected validation issue.
    /// </summary>
    public static string FormatValidation(ValidationCatalog catalog)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Intentions validation report");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Valid categories: {catalog.ValidCategories.Count}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Valid intentions: {catalog.ValidIntentions.Count}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Valid scenarios: {catalog.ValidScenarios.Count}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Issues: {catalog.Issues.Count}");

        foreach (var issue in catalog.Issues
                     .OrderByDescending(issue => issue.Severity)
                     .ThenBy(issue => issue.ObjectType.ToString(), StringComparer.Ordinal)
                     .ThenBy(issue => issue.ObjectId, StringComparer.Ordinal)
                     .ThenBy(issue => issue.Path, StringComparer.Ordinal)
                     .ThenBy(issue => issue.Code, StringComparer.Ordinal))
        {
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"- [{issue.Severity}] {issue.ObjectType}:{issue.ObjectId} {issue.Path} {issue.Code} - {issue.Message}");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Formats one immutable snapshot preview for console inspection.
    /// </summary>
    public static string FormatSnapshotPreview(IntentionsSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Intentions snapshot preview");
        builder.AppendLine(CultureInfo.InvariantCulture,
            $"Snapshot: id={snapshot.SnapshotId} kind={snapshot.Request.Kind} waveId={snapshot.Request.WaveId} builtAt={FormatTime(snapshot.BuiltAt)}");
        builder.AppendLine(CultureInfo.InvariantCulture,
            $"Round: mode={snapshot.RoundFacts.GameMode} stationTime={FormatTime(snapshot.RoundFacts.StationTime)} stationName=\"{snapshot.RoundFacts.StationName}\" crew={snapshot.RoundFacts.CrewCount} security={snapshot.RoundFacts.SecurityCount}");
        builder.AppendLine(CultureInfo.InvariantCulture,
            $"Event tags: {FormatList(snapshot.RoundFacts.EventTags)}");

        var antagSummary = snapshot.RoundFacts.AntagSummary;
        builder.AppendLine(CultureInfo.InvariantCulture,
            $"Antag summary: total={antagSummary.TotalCount} gameMode={antagSummary.GameModeAntagCount} ghostRole={antagSummary.GhostRoleAntagCount}");
        AppendCountMap(builder, "Antag roles:", antagSummary.ByRole);
        AppendCountMap(builder, "Antag objective types:", antagSummary.ByObjectiveType);

        builder.AppendLine(CultureInfo.InvariantCulture, $"Candidates: {snapshot.Candidates.Length}");
        if (snapshot.Candidates.Length == 0)
        {
            builder.AppendLine("- none");
            return builder.ToString();
        }

        foreach (var candidate in snapshot.Candidates)
        {
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"- mind={candidate.MindId.Id} user={candidate.UserId} entity={candidate.OwnerEntityUid.Id} name=\"{candidate.CharacterName}\"");
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"  job={candidate.Job ?? "-"} department={candidate.Department ?? "-"} age={candidate.Age?.ToString(CultureInfo.InvariantCulture) ?? "-"} species={candidate.Species ?? "-"} sex={candidate.Sex ?? "-"}");
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"  traits={FormatList(candidate.Traits)} hasMindshield={FormatYesNo(candidate.HasMindshield)}");
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"  antagRoles={FormatList(candidate.AntagRoles)} antagObjectiveTypes={FormatList(candidate.AntagObjectiveTypes)} isGhostRoleAntag={FormatYesNo(candidate.IsGhostRoleAntag)} hasAntagRole={FormatYesNo(candidate.HasAntagRole)}");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Formats one predicate dictionary for console inspection.
    /// </summary>
    public static string FormatPredicateDictionary(IntentionsPredicateDictionary dictionary)
    {
        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"Intentions predicate dictionary: {dictionary.Name}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Source: {dictionary.Source}");
        if (!string.IsNullOrWhiteSpace(dictionary.Note))
            builder.AppendLine(CultureInfo.InvariantCulture, $"Note: {dictionary.Note}");

        builder.AppendLine(CultureInfo.InvariantCulture, $"Values: {dictionary.Values.Length}");
        if (dictionary.Values.Length == 0)
        {
            builder.AppendLine("- none");
            return builder.ToString();
        }

        foreach (var value in dictionary.Values)
            builder.AppendLine(CultureInfo.InvariantCulture, $"- {value}");

        return builder.ToString();
    }

    /// <summary>
    /// Formats the outcome of a start or refill wave preview.
    /// </summary>
    public static string FormatWavePreview(StartWaveResult result)
    {
        var context = result.Context;
        var builder = new StringBuilder();
        builder.AppendLine("Intentions wave preview");
        builder.AppendLine(CultureInfo.InvariantCulture,
            $"Wave: {context.WaveId} kind={context.Kind} status={context.Status} seed={context.Seed} snapshot={context.SnapshotId}");
        builder.AppendLine(CultureInfo.InvariantCulture,
            $"Round time: {FormatTime(context.StartedAtRoundTime)} activeCrew={context.WaveActiveCrew} baseline={context.DistributionCrewBaseline}");

        if (!string.IsNullOrWhiteSpace(context.FailureReason))
            builder.AppendLine(CultureInfo.InvariantCulture, $"Failure: {context.FailureReason}");

        builder.AppendLine("Categories:");
        foreach (var categoryId in context.AllowedCategoryIds)
        {
            if (!context.CategoryStateById.TryGetValue(categoryId, out var state))
                continue;

            builder.AppendLine(CultureInfo.InvariantCulture,
                $"- {state.CategoryId}: target={state.TargetQuota} filled={state.FilledQuota} desired={state.DesiredQuota} activeFrozen={state.ExistingActiveFrozenCount} refill={state.RefillTarget} maxPrimaryPerMind={state.EffectiveMaxPrimaryPerMind} exhausted={state.IsExhausted} reason={state.ExhaustReason}");

            if (state.CandidateScenarioIds.Count > 0)
                builder.AppendLine(CultureInfo.InvariantCulture, $"  pool: {string.Join(", ", state.CandidateScenarioIds)}");

            if (state.RejectedScenarioIds.Count > 0)
                builder.AppendLine(CultureInfo.InvariantCulture, $"  rejected: {string.Join(", ", state.RejectedScenarioIds.OrderBy(id => id, StringComparer.Ordinal))}");

            foreach (var (code, count) in state.RejectCounters.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                builder.AppendLine(CultureInfo.InvariantCulture, $"  reject {code}: {count}");
        }

        builder.AppendLine("Selected builds:");
        if (context.SuccessfulBuilds.Count == 0)
        {
            builder.AppendLine("- none");
        }
        else
        {
            foreach (var build in context.SuccessfulBuilds)
                AppendBuildSummary(builder, build, "- ");
        }

        AppendScenarioRejects(builder, context);
        return builder.ToString();
    }

    /// <summary>
    /// Formats the outcome of one pure scenario builder run.
    /// </summary>
    public static string FormatScenarioBuild(ScenarioBuildResult build)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Intentions scenario build");
        AppendBuildSummary(builder, build, string.Empty);

        if (build.SkippedOptionalSlots.Length > 0)
            builder.AppendLine(CultureInfo.InvariantCulture, $"Skipped optional slots: {string.Join(", ", build.SkippedOptionalSlots)}");

        AppendSlotRejects(builder, build.SlotRejectReasons, "Slot rejects:");
        return builder.ToString();
    }

    /// <summary>
    /// Formats the outcome of one forced runtime scenario assignment.
    /// </summary>
    public static string FormatRuntimeAssign(ForceAssignScenarioRuntimeResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Intentions runtime assign");
        builder.AppendLine(CultureInfo.InvariantCulture,
            $"Scenario: template={result.ScenarioTemplateId} waveId={result.WaveId} ignoredPredicates={FormatYesNo(result.IgnoredPredicates)} success={FormatYesNo(result.IsSuccess)} failure={(string.IsNullOrWhiteSpace(result.FailureCode) ? "-" : result.FailureCode)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Message: {result.Message}");

        if (!string.IsNullOrWhiteSpace(result.ExpectedArgumentLayout))
            builder.AppendLine(CultureInfo.InvariantCulture, $"Expected slot args: {result.ExpectedArgumentLayout}");

        if (result.GlobalPredicateResult is { } global && global.RejectReasons.Length > 0)
        {
            builder.AppendLine("Global predicate rejects:");
            foreach (var reject in global.RejectReasons)
            {
                builder.AppendLine(CultureInfo.InvariantCulture,
                    $"- code={reject.Code} field={reject.Field} operator={reject.Operator} message={reject.Message}");
            }
        }

        if (result.BuildResult is { } build)
        {
            builder.AppendLine("Build:");
            AppendBuildSummary(builder, build, "- ");
            if (build.SkippedOptionalSlots.Length > 0)
                builder.AppendLine(CultureInfo.InvariantCulture, $"Skipped optional slots: {string.Join(", ", build.SkippedOptionalSlots)}");

            AppendSlotRejects(builder, build.SlotRejectReasons, "Slot rejects:");
        }

        if (result.CommitResult is { IsSuccess: true, ScenarioInstance: { } scenario } commit)
        {
            builder.AppendLine("Commit:");
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"- scenarioUid={scenario.Uid.Value} template={scenario.ScenarioTemplateId} category={scenario.CategoryId} status={scenario.Status} ownerMind={scenario.OwnerMindId.Id} wave={scenario.WaveId}");

            foreach (var intention in commit.IntentionInstances.OrderBy(intention => intention.Uid.Value))
            {
                builder.AppendLine(CultureInfo.InvariantCulture,
                    $"- intentionUid={intention.Uid.Value} slot={intention.SlotId} mind={intention.OwnerMindId.Id} status={intention.Status} hidden={intention.IsHidden} reveal={intention.RevealMode}");
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Formats the outcome of a debug/admin hidden-intention reveal request.
    /// </summary>
    public static string FormatRuntimeReveal(RevealHiddenIntentionsRuntimeResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Intentions runtime reveal");
        builder.AppendLine(CultureInfo.InvariantCulture,
            $"Target: scope={result.Scope} mind={result.RequestedMindId.Id} intentionUid={(result.RequestedIntentionUid?.Value.ToString(CultureInfo.InvariantCulture) ?? "-")} success={FormatYesNo(result.IsSuccess)} failure={(string.IsNullOrWhiteSpace(result.FailureCode) ? "-" : result.FailureCode)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Message: {result.Message}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Revealed intentions: {result.RevealedIntentions.Length}");

        if (result.RevealedIntentions.Length == 0)
        {
            builder.AppendLine("- none");
            return builder.ToString();
        }

        foreach (var reveal in result.RevealedIntentions.OrderBy(item => item.IntentionUid.Value))
        {
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"- intentionUid={reveal.IntentionUid.Value} scenarioUid={reveal.ScenarioUid.Value} mind={reveal.MindId.Id} ownerEntity={reveal.OwnerEntityUid.Id} mode={reveal.PreviousRevealMode} scheduledReveal={FormatNullableTime(reveal.ScheduledRevealAtRoundTime)} revealedAt={FormatTime(reveal.RevealedByAdminAtRoundTime)}");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Formats the runtime registry and a lightweight consistency report.
    /// </summary>
    public static string FormatRegistryDump(IntentionsRuntimeRegistry registry)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Intentions runtime registry");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Scenarios: {registry.ScenarioByUid.Count}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Intentions: {registry.IntentionByUid.Count}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Mind index entries: {registry.IntentionIdsByMind.Sum(pair => pair.Value.Count)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Slot assignments: {registry.SlotAssignmentByScenarioAndSlot.Count}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Assigned scenario templates: {registry.AssignedScenarioIds.Count}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Wave contexts: {registry.WaveContextByWaveId.Count}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Hidden reveal buckets: {registry.HiddenIntentionsByRevealTime.Count}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Missing owner scenarios: {registry.MissingOwnerScenarioIds.Count}");

        var consistencyIssues = FindRegistryConsistencyIssues(registry).ToArray();
        builder.AppendLine(consistencyIssues.Length == 0 ? "Consistency: ok" : "Consistency issues:");
        foreach (var issue in consistencyIssues)
            builder.AppendLine(CultureInfo.InvariantCulture, $"- {issue}");

        builder.AppendLine("Scenario instances:");
        foreach (var scenario in registry.ScenarioByUid.Values
                     .OrderBy(scenario => scenario.Uid.Value))
        {
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"- scenarioUid={scenario.Uid.Value} template={scenario.ScenarioTemplateId} category={scenario.CategoryId} status={scenario.Status} ownerMind={scenario.OwnerMindId.Id} ownerEntity={scenario.OwnerEntityUid.Id} wave={scenario.WaveId}");
        }

        builder.AppendLine("Intention instances:");
        foreach (var intention in registry.IntentionByUid.Values
                     .OrderBy(intention => intention.Uid.Value))
        {
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"- intentionUid={intention.Uid.Value} template={intention.IntentionTemplateId} scenarioUid={intention.ScenarioUid.Value} slot={intention.SlotId} mind={intention.OwnerMindId.Id} status={intention.Status} hidden={intention.IsHidden} reveal={intention.RevealMode} due={FormatNullableTime(intention.RevealedAtRoundTime)}");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Formats the read-model returned for one mind.
    /// </summary>
    public static string FormatMindShow(IntentionsEuiState state)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Intentions mind view");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Target: {state.TargetName}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Mode: {state.Mode}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Round time: {FormatTime(state.RoundTime)}");
        AppendCards(builder, "Own intentions:", state.OwnIntentions);
        AppendCards(builder, "Linked intentions:", state.LinkedIntentions);

        if (state.AdminScenarios.Length > 0)
        {
            builder.AppendLine("Admin scenario metadata:");
            foreach (var scenario in state.AdminScenarios.OrderBy(scenario => scenario.ScenarioUid))
            {
                builder.AppendLine(CultureInfo.InvariantCulture,
                    $"- scenarioUid={scenario.ScenarioUid} template={scenario.ScenarioTemplateId} category={scenario.CategoryId} status={scenario.Status} ownerMind={scenario.OwnerMindId} ownerEntity={scenario.OwnerEntityUid} wave={scenario.WaveId}");
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Formats the current automatic wave timer state.
    /// </summary>
    public static string FormatWaveTimer(IntentionsDistributionScheduleStatus status)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Intentions wave timer");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- start wave finished: {FormatYesNo(status.StartWaveFinished)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- start wave pending: {FormatYesNo(status.StartWavePending)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- next wave id: {status.NextWaveId}");

        if (status.RemainingToStartWave is { } startRemaining)
            builder.AppendLine(CultureInfo.InvariantCulture, $"- next start wave in: {FormatTime(startRemaining)}");

        if (status.RemainingToRefillWave is { } refillRemaining)
            builder.AppendLine(CultureInfo.InvariantCulture, $"- next refill wave in: {FormatTime(refillRemaining)}");
        else if (!status.StartWaveFinished)
            builder.AppendLine("- next refill wave in: not scheduled yet (start wave has not finished)");
        else
            builder.AppendLine("- next refill wave in: not scheduled");

        return builder.ToString();
    }

    /// <summary>
    /// Formats the hidden reveal timer index.
    /// </summary>
    public static string FormatRevealDump(IntentionsRuntimeRegistry registry, TimeSpan now)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Intentions reveal dump");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Now: {FormatTime(now)}");

        if (registry.HiddenIntentionsByRevealTime.Count == 0)
        {
            builder.AppendLine("Hidden timer index: empty");
            return builder.ToString();
        }

        foreach (var (revealTime, intentionUids) in registry.HiddenIntentionsByRevealTime)
        {
            var dueState = revealTime <= now ? "due" : "pending";
            builder.AppendLine(CultureInfo.InvariantCulture, $"- revealAt={FormatTime(revealTime)} state={dueState} count={intentionUids.Count}");
            foreach (var intentionUid in intentionUids.OrderBy(uid => uid.Value))
            {
                if (!registry.IntentionByUid.TryGetValue(intentionUid, out var intention))
                {
                    builder.AppendLine(CultureInfo.InvariantCulture, $"  - intentionUid={intentionUid.Value} missing-runtime-instance");
                    continue;
                }

                builder.AppendLine(CultureInfo.InvariantCulture,
                    $"  - intentionUid={intention.Uid.Value} template={intention.IntentionTemplateId} scenarioUid={intention.ScenarioUid.Value} mind={intention.OwnerMindId.Id} hidden={intention.IsHidden} status={intention.Status} mode={intention.RevealMode}");
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Formats category quota diagnostics from one committed wave context.
    /// </summary>
    public static string FormatCategoryQuota(DistributionWaveContext? context)
    {
        if (context is null)
            return "No wave context is available.\n";

        var builder = new StringBuilder();
        builder.AppendLine("Intentions category quota");
        builder.AppendLine(CultureInfo.InvariantCulture,
            $"Wave: {context.WaveId} kind={context.Kind} status={context.Status} baseline={context.DistributionCrewBaseline} seed={context.Seed}");

        foreach (var categoryId in context.AllowedCategoryIds)
        {
            if (!context.CategoryStateById.TryGetValue(categoryId, out var state))
                continue;

            builder.AppendLine(CultureInfo.InvariantCulture,
                $"- {categoryId}: target={state.TargetQuota} filled={state.FilledQuota} desired={state.DesiredQuota} activeFrozen={state.ExistingActiveFrozenCount} refill={state.RefillTarget} maxPrimaryPerMind={state.EffectiveMaxPrimaryPerMind} exhausted={state.IsExhausted} reason={state.ExhaustReason}");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Formats a lightweight trace for one committed wave id.
    /// </summary>
    public static string FormatLogsTrace(IntentionsRuntimeRegistry registry, int waveId)
    {
        if (!registry.WaveContextByWaveId.TryGetValue(waveId, out var context))
            return $"No wave context found for wave {waveId}.\n";

        var builder = new StringBuilder();
        builder.AppendLine("Intentions wave trace");
        builder.AppendLine(CultureInfo.InvariantCulture,
            $"Wave: {context.WaveId} kind={context.Kind} status={context.Status} snapshot={context.SnapshotId} seed={context.Seed} started={FormatTime(context.StartedAtRoundTime)}");
        builder.AppendLine(CultureInfo.InvariantCulture,
            $"Crew: active={context.WaveActiveCrew} baseline={context.DistributionCrewBaseline}");

        builder.AppendLine("Timeline:");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- snapshot {context.SnapshotId}");
        foreach (var build in context.SuccessfulBuilds)
            builder.AppendLine(CultureInfo.InvariantCulture, $"- build success scenario={build.ScenarioTemplateId} slots={build.BuiltSlots.Length}");

        foreach (var reject in context.RejectReasons)
            builder.AppendLine(CultureInfo.InvariantCulture, $"- reject scenario={reject.ScenarioTemplateId} code={reject.Code} slot={reject.SlotId ?? "-"}");

        foreach (var scenario in registry.ScenarioByUid.Values
                     .Where(scenario => scenario.WaveId == waveId)
                     .OrderBy(scenario => scenario.Uid.Value))
        {
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"- committed scenarioUid={scenario.Uid.Value} template={scenario.ScenarioTemplateId} status={scenario.Status}");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Appends one successful or failed build summary to the formatter output.
    /// </summary>
    private static void AppendBuildSummary(StringBuilder builder, ScenarioBuildResult build, string prefix)
    {
        builder.AppendLine(CultureInfo.InvariantCulture,
            $"{prefix}scenario={build.ScenarioTemplateId} category={build.CategoryId} success={build.IsSuccess} failure={build.FailureReason ?? "-"}");
        foreach (var slot in build.BuiltSlots.OrderBy(slot => slot.SlotId, StringComparer.Ordinal))
        {
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"{prefix}  slot={slot.SlotId} kind={slot.Kind} intention={slot.IntentionId} mind={slot.MindId.Id} entity={slot.OwnerEntityUid.Id} state={slot.State} required={slot.Required} bound={slot.WasBound} bindSource={slot.BoundToSlotId ?? "-"}");
        }
    }

    /// <summary>
    /// Appends scenario-level rejection information for the wave.
    /// </summary>
    private static void AppendScenarioRejects(StringBuilder builder, DistributionWaveContext context)
    {
        builder.AppendLine("Scenario rejects:");
        if (context.RejectReasons.Count == 0)
        {
            builder.AppendLine("- none");
            return;
        }

        foreach (var reject in context.RejectReasons
                     .OrderBy(reject => reject.CategoryId, StringComparer.Ordinal)
                     .ThenBy(reject => reject.ScenarioTemplateId, StringComparer.Ordinal)
                     .ThenBy(reject => reject.Code, StringComparer.Ordinal))
        {
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"- scenario={reject.ScenarioTemplateId} category={reject.CategoryId} code={reject.Code} slot={reject.SlotId ?? "-"} message={reject.Message}");
            AppendSlotRejects(builder, reject.SlotRejectReasons, "  slot rejects:");
        }
    }

    /// <summary>
    /// Appends slot-level rejection information when it exists.
    /// </summary>
    private static void AppendSlotRejects(StringBuilder builder, IEnumerable<SlotRejectReason> slotRejects, string header)
    {
        var rejects = slotRejects.ToArray();
        if (rejects.Length == 0)
            return;

        builder.AppendLine(header);
        foreach (var reject in rejects
                     .OrderBy(reject => reject.SlotId, StringComparer.Ordinal)
                     .ThenBy(reject => reject.Code, StringComparer.Ordinal))
        {
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"- slot={reject.SlotId} code={reject.Code} candidate={reject.CandidateMindId?.Id.ToString(CultureInfo.InvariantCulture) ?? "-"} message={reject.Message}");

            foreach (var predicateReject in reject.PredicateRejectReasons)
                builder.AppendLine(CultureInfo.InvariantCulture, $"  predicate={predicateReject.Code} field={predicateReject.Field} message={predicateReject.Message}");
        }
    }

    /// <summary>
    /// Appends a stable list of card summaries for the provided read-model cards.
    /// </summary>
    private static void AppendCards(StringBuilder builder, string header, IEnumerable<IntentionsCardView> cards)
    {
        var array = cards.OrderBy(card => card.AssignedAtRoundTime).ThenBy(card => card.IntentionUid).ToArray();
        builder.AppendLine(header);
        if (array.Length == 0)
        {
            builder.AppendLine("- none");
            return;
        }

        foreach (var card in array)
        {
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"- intentionUid={card.IntentionUid} scenarioUid={card.ScenarioUid} title=\"{card.Title}\" kind={card.Kind} hidden={card.IsHidden} intentionStatus={card.IntentionStatus} scenarioStatus={card.ScenarioStatus} slotStatus={card.SlotStatus} reveal=\"{card.RevealText}\" copyable={(card.CopyableText is null ? "no" : "yes")}");
        }
    }

    /// <summary>
    /// Enumerates lightweight registry consistency issues for debug output.
    /// </summary>
    private static IEnumerable<string> FindRegistryConsistencyIssues(IntentionsRuntimeRegistry registry)
    {
        foreach (var intention in registry.IntentionByUid.Values)
        {
            if (!registry.ScenarioByUid.ContainsKey(intention.ScenarioUid))
                yield return $"intention {intention.Uid.Value} references missing scenario {intention.ScenarioUid.Value}";

            if (!registry.ScenarioUidByIntentionUid.TryGetValue(intention.Uid, out var scenarioUid) || scenarioUid != intention.ScenarioUid)
                yield return $"intention {intention.Uid.Value} has missing or mismatched back-reference";

            if (!registry.IntentionIdsByMind.TryGetValue(intention.OwnerMindId, out var mindIndex) || !mindIndex.Contains(intention.Uid))
                yield return $"intention {intention.Uid.Value} is missing from mind index {intention.OwnerMindId.Id}";
        }

        foreach (var (mindId, intentions) in registry.IntentionIdsByMind)
        {
            foreach (var intentionUid in intentions)
            {
                if (!registry.IntentionByUid.ContainsKey(intentionUid))
                    yield return $"mind index {mindId.Id} references missing intention {intentionUid.Value}";
            }
        }
    }

    /// <summary>
    /// Appends one count map with deterministic ordering.
    /// </summary>
    private static void AppendCountMap(StringBuilder builder, string header, IReadOnlyDictionary<string, int> values)
    {
        builder.AppendLine(header);
        if (values.Count == 0)
        {
            builder.AppendLine("- none");
            return;
        }

        foreach (var (key, count) in values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            builder.AppendLine(CultureInfo.InvariantCulture, $"- {key}: {count}");
    }

    /// <summary>
    /// Formats an optional round time using the standard debug representation.
    /// </summary>
    private static string FormatNullableTime(TimeSpan? time)
    {
        return time is null ? "-" : FormatTime(time.Value);
    }

    /// <summary>
    /// Formats a round-relative time span using the standard debug representation.
    /// </summary>
    private static string FormatTime(TimeSpan time)
    {
        return time.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Formats a boolean as a human-readable yes/no string.
    /// </summary>
    private static string FormatYesNo(bool value)
    {
        return value ? "yes" : "no";
    }

    /// <summary>
    /// Formats a stable list as a comma-separated string or none when it is empty.
    /// </summary>
    private static string FormatList(IEnumerable<string> values)
    {
        var array = values.ToArray();
        return array.Length == 0 ? "none" : string.Join(", ", array);
    }
}
