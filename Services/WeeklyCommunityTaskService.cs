using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace ScumRconTool.Services;

public sealed class WeeklyCommunityTaskService
{
    public static IReadOnlyList<WeeklyCommunityTaskStatTarget> AvailableStatTargets { get; } = new List<WeeklyCommunityTaskStatTarget>
    {
        // survival_stats
        new("survival_stats", "highest_positive_fame_points", "Highest positive fame points", "Survival"),
        new("survival_stats", "doors_claimed", "Doors claimed", "Survival"),
        new("survival_stats", "animals_killed", "Animals killed", "Hunting"),
        new("survival_stats", "minutes_survived", "Minutes survived", "Survival"),
        new("survival_stats", "kills", "Kills", "Combat"),
        new("survival_stats", "deaths", "Deaths", "Survival"),
        new("survival_stats", "locks_picked", "Locks picked", "Looting"),
        new("survival_stats", "puppets_killed", "Puppets killed", "Combat"),
        new("survival_stats", "guns_crafted", "Guns crafted", "Crafting"),
        new("survival_stats", "bullets_crafted", "Bullets crafted", "Crafting"),
        new("survival_stats", "arrows_crafted", "Arrows crafted", "Crafting"),
        new("survival_stats", "clothing_crafted", "Clothing crafted", "Crafting"),
        new("survival_stats", "longest_kill_distance", "Longest kill distance", "Combat"),
        new("survival_stats", "melee_kills", "Melee kills", "Combat"),
        new("survival_stats", "archery_kills", "Archery kills", "Combat"),
        new("survival_stats", "players_knocked_out", "Players knocked out", "Combat"),
        new("survival_stats", "total_defecations", "Defecations", "Fun"),
        new("survival_stats", "total_urinations", "Urinations", "Fun"),
        new("survival_stats", "lights_fired", "Lights fired", "Survival"),
        new("survival_stats", "containers_looted", "Containers looted", "Looting"),
        new("survival_stats", "items_put_into_containers", "Items put into containers", "Looting"),
        new("survival_stats", "deaths_by_prisoners", "Deaths by prisoners", "Combat"),
        new("survival_stats", "animals_skinned", "Animals skinned", "Hunting"),
        new("survival_stats", "food_eaten", "Food eaten", "Survival"),
        new("survival_stats", "distance_travelled_by_foot", "Distance travelled by foot", "Travel"),
        new("survival_stats", "wounds_patched", "Wounds patched", "Medical"),
        new("survival_stats", "items_picked_up", "Items picked up", "Looting"),
        new("survival_stats", "liquid_drank", "Liquid drank", "Survival"),
        new("survival_stats", "teeth_lost", "Teeth lost", "Fun"),
        new("survival_stats", "total_calories_intake", "Calories intake", "Survival"),
        new("survival_stats", "shots_fired", "Shots fired", "Shooting"),
        new("survival_stats", "shots_hit", "Shots hit", "Shooting"),
        new("survival_stats", "headshots", "Headshots", "Shooting"),
        new("survival_stats", "melee_weapon_swings", "Melee weapon swings", "Combat"),
        new("survival_stats", "melee_weapon_hits", "Melee weapon hits", "Combat"),
        new("survival_stats", "melee_weapons_crafted", "Melee weapons crafted", "Crafting"),
        new("survival_stats", "drone_kills", "Drone kills", "Combat"),
        new("survival_stats", "sentry_kills", "Sentry kills", "Combat"),
        new("survival_stats", "prisoner_kills", "Prisoner kills", "Combat"),
        new("survival_stats", "puppets_knocked_out", "Puppets knocked out", "Combat"),
        new("survival_stats", "diarrheas", "Diarrheas", "Fun"),
        new("survival_stats", "vomits", "Vomits", "Fun"),
        new("survival_stats", "distance_travelled_in_vehicle", "Distance travelled in vehicle", "Travel"),
        new("survival_stats", "mushrooms_eaten", "Mushrooms eaten", "Survival"),
        new("survival_stats", "highest_muscle_mass", "Highest muscle mass", "Survival"),
        new("survival_stats", "highest_fat", "Highest fat", "Survival"),
        new("survival_stats", "heart_attacks", "Heart attacks", "Survival"),
        new("survival_stats", "overdose", "Overdose", "Survival"),
        new("survival_stats", "starvation", "Starvation", "Survival"),
        new("survival_stats", "highest_damage_taken", "Highest damage taken", "Survival"),
        new("survival_stats", "highest_weight_carried", "Highest weight carried", "Survival"),
        new("survival_stats", "lowest_negative_fame_points", "Lowest negative fame points", "Survival"),
        new("survival_stats", "distance_travelled_swimming", "Distance travelled swimming", "Travel"),
        new("survival_stats", "crows_killed", "Crows killed", "Hunting"),
        new("survival_stats", "seagulls_killed", "Seagulls killed", "Hunting"),
        new("survival_stats", "horses_killed", "Horses killed", "Hunting"),
        new("survival_stats", "boars_killed", "Boars killed", "Hunting"),
        new("survival_stats", "bears_killed", "Bears killed", "Hunting"),
        new("survival_stats", "goats_killed", "Goats killed", "Hunting"),
        new("survival_stats", "deers_killed", "Deers killed", "Hunting"),
        new("survival_stats", "chickens_killed", "Chickens killed", "Hunting"),
        new("survival_stats", "rabbits_killed", "Rabbits killed", "Hunting"),
        new("survival_stats", "donkeys_killed", "Donkeys killed", "Hunting"),
        new("survival_stats", "times_mauled_by_bear", "Times mauled by bear", "Hunting"),
        new("survival_stats", "longest_animal_kill_distance", "Longest animal kill distance", "Hunting"),
        new("survival_stats", "alcohol_drank", "Alcohol drank", "Survival"),
        new("survival_stats", "foliage_cut", "Foliage cut", "Survival"),
        new("survival_stats", "distance_travel_by_boat", "Distance travelled by boat", "Travel"),
        new("survival_stats", "distance_sailed", "Distance sailed", "Travel"),
        new("survival_stats", "times_caught_by_shark", "Times caught by shark", "Survival"),
        new("survival_stats", "times_escaped_shark_bite", "Times escaped shark bite", "Survival"),
        new("survival_stats", "wolves_killed", "Wolves killed", "Hunting"),
        new("survival_stats", "last_fame_point_award_consecutive_days", "Fame award consecutive days", "Survival"),
        new("survival_stats", "firearm_kills", "Firearm kills", "Combat"),
        new("survival_stats", "bare_handed_kills", "Bare handed kills", "Combat"),

        // fishing_stats
        new("fishing_stats", "fish_caught", "Fish caught", "Fishing"),
        new("fishing_stats", "fish_kept", "Fish kept", "Fishing"),
        new("fishing_stats", "fish_released", "Fish released", "Fishing"),
        new("fishing_stats", "lines_broken", "Lines broken", "Fishing"),
        new("fishing_stats", "heaviest_fish_caught", "Heaviest fish caught", "Fishing"),
        new("fishing_stats", "longest_fish_caught", "Longest fish caught", "Fishing"),
        new("fishing_stats", "bass_caught", "Bass caught", "Fishing"),
        new("fishing_stats", "catfish_caught", "Catfish caught", "Fishing"),
        new("fishing_stats", "pike_caught", "Pike caught", "Fishing"),
        new("fishing_stats", "carp_caught", "Carp caught", "Fishing"),
        new("fishing_stats", "amur_caught", "Amur caught", "Fishing"),
        new("fishing_stats", "bleak_caught", "Bleak caught", "Fishing"),
        new("fishing_stats", "chub_caught", "Chub caught", "Fishing"),
        new("fishing_stats", "ruffe_caught", "Ruffe caught", "Fishing"),
        new("fishing_stats", "prussian_carp_caught", "Prussian carp caught", "Fishing"),
        new("fishing_stats", "crucian_carp_caught", "Crucian carp caught", "Fishing"),
        new("fishing_stats", "sardine_caught", "Sardine caught", "Fishing"),
        new("fishing_stats", "dentex_caught", "Dentex caught", "Fishing"),
        new("fishing_stats", "orata_caught", "Orata caught", "Fishing"),
        new("fishing_stats", "tuna_caught", "Tuna caught", "Fishing")
    };

    private readonly SftpLogService _sftp;
    private readonly Action<string> _log;

    public WeeklyCommunityTaskService(SftpLogService sftp, Action<string> log)
    {
        _sftp = sftp;
        _log = log;
    }

    public bool IsRunning { get; private set; }
    private CancellationTokenSource? _cts;

    public void Start(BotSettings settings, Func<CancellationToken, Task<int>> getOnlinePlayersAsync, Func<IReadOnlyList<WeeklyCommunityTaskProgress>, Task> publishProgressAsync)
    {
        Stop();
        IsRunning = true;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _ = Task.Run(async () =>
        {
            // First pass announces/currently initializes the task once Discord is ready.
            await SafeTickAsync(settings, getOnlinePlayersAsync, publishProgressAsync, token, force: true);

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var minutes = Math.Max(5, settings.WeeklyTaskPollMinutes <= 0 ? 30 : settings.WeeklyTaskPollMinutes);
                    await Task.Delay(TimeSpan.FromMinutes(minutes), token);
                    await SafeTickAsync(settings, getOnlinePlayersAsync, publishProgressAsync, token, force: false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
            }
        }, token);
    }

    public void Stop()
    {
        IsRunning = false;
        try { _cts?.Cancel(); } catch { }
        _cts?.Dispose();
        _cts = null;
    }

    public async Task<List<WeeklyCommunityTaskProgress>> ScanAllOnceAsync(BotSettings settings, CancellationToken cancellationToken = default, bool resetBaseline = false)
    {
        var allDefinitions = settings.GetWeeklyTaskDefinitions()
            .Where(x => x.Enabled)
            .ToList();

        var nowUtc = DateTime.UtcNow;
        var definitions = allDefinitions
            .Where(x => IsTaskRunnableNow(x, nowUtc))
            .ToList();

        var plannedCount = allDefinitions.Count(x => IsTaskPlannedForFuture(x, nowUtc));
        var expiredCount = allDefinitions.Count(x => IsTaskExpiredByConfigurationOrBaseline(x, nowUtc));

        if (definitions.Count == 0)
        {
            _log($"Weekly/Daily Tasks: keine aktuell laufende Aufgabe konfiguriert. Geplant={plannedCount}, Abgelaufen={expiredCount}.");
            return new List<WeeklyCommunityTaskProgress>();
        }

        foreach (var definition in definitions)
        {
            ValidateDefinition(definition);
        }

        var localDb = await _sftp.DownloadFileAsync(settings.WeeklyTaskDbRemoteFilePath, Path.Combine("WeeklyTasks", "Db"), cancellationToken);
        if (string.IsNullOrWhiteSpace(localDb) || !File.Exists(localDb))
        {
            _log($"Weekly/Daily Tasks: SCUM.db konnte nicht von {settings.WeeklyTaskDbRemoteFilePath} heruntergeladen werden.");
            return new List<WeeklyCommunityTaskProgress>();
        }

        try
        {
            var result = new List<WeeklyCommunityTaskProgress>();
            foreach (var definition in definitions)
            {
                try
                {
                    result.Add(ScanDefinitionFromLocalDb(localDb, definition, resetBaseline));
                }
                catch (Exception ex)
                {
                    _log($"Weekly/Daily Task Fehler bei '{definition.Id}': {ex.Message}");
                    AppLogService.WriteException("WeeklyCommunityTask " + definition.Id, ex);
                }
            }

            return result;
        }
        finally
        {
            // Die SCUM.db wird nur zum aktuellen Auslesen gebraucht. Nicht lokal archivieren,
            // sonst entsteht bei jedem Poll eine weitere grosse DB-Kopie.
            var weeklyDbDirectory = SftpLogService.GetAppDataLocalDirectory(Path.Combine("WeeklyTasks", "Db"));
            LocalRetentionService.TryDeleteFile(localDb);
            LocalRetentionService.TryDeleteFiles(weeklyDbDirectory, "*.db");
            LocalRetentionService.CleanupDirectory(weeklyDbDirectory);
        }
    }

    public async Task<WeeklyCommunityTaskProgress?> ScanOnceAsync(BotSettings settings, CancellationToken cancellationToken = default, bool resetBaseline = false)
    {
        var all = await ScanAllOnceAsync(settings, cancellationToken, resetBaseline);
        return all.FirstOrDefault();
    }

    private WeeklyCommunityTaskProgress ScanDefinitionFromLocalDb(string localDb, WeeklyCommunityTaskDefinition definition, bool resetBaseline)
    {
        var statTarget = ResolveStatTarget(definition);
        definition.StatTable = statTarget.TableName;
        definition.StatColumn = statTarget.ColumnName;

        var currentTotal = ReadStatTotal(localDb, statTarget);
        var currentSquadTotals = ReadSquadStatTotals(localDb, statTarget);
        var baseline = LoadBaseline(definition);
        var baselineMismatch = !string.Equals(baseline.TaskId, definition.Id, StringComparison.OrdinalIgnoreCase) ||
                               !string.Equals(baseline.StatTable, statTarget.TableName, StringComparison.OrdinalIgnoreCase) ||
                               !string.Equals(baseline.StatColumn, statTarget.ColumnName, StringComparison.OrdinalIgnoreCase);

        if (resetBaseline || baselineMismatch)
        {
            baseline = new WeeklyCommunityTaskBaseline
            {
                TaskId = definition.Id,
                StatTable = statTarget.TableName,
                StatColumn = statTarget.ColumnName,
                CreatedUtc = DateTime.UtcNow,
                BaselineValue = currentTotal,
                SquadBaselineValues = currentSquadTotals.ToDictionary(x => x.SquadId, x => x.CurrentTotal, StringComparer.OrdinalIgnoreCase)
            };
            SaveBaseline(baseline);
            _log($"{GetTaskKind(definition)} Task '{definition.Title}': Startwert gespeichert ({statTarget.Key}={currentTotal}, Squads={baseline.SquadBaselineValues.Count}).");
        }
        else if (baseline.SquadBaselineValues.Count == 0 && currentSquadTotals.Count > 0)
        {
            baseline.SquadBaselineValues = currentSquadTotals.ToDictionary(x => x.SquadId, x => x.CurrentTotal, StringComparer.OrdinalIgnoreCase);
            SaveBaseline(baseline);
            _log($"{GetTaskKind(definition)} Task '{definition.Title}': Squad-Startwerte nachgetragen ({baseline.SquadBaselineValues.Count} Squads).");
        }

        var progressValue = Math.Max(0, currentTotal - baseline.BaselineValue);
        var target = Math.Max(1, definition.Target);
        var minimumParticipationPercent = GetMinimumParticipationPercent(definition);
        var minimumParticipationValue = GetMinimumParticipationValue(target, minimumParticipationPercent);
        var squadProgress = BuildSquadProgress(currentSquadTotals, baseline, target, minimumParticipationValue);
        var successfulSquads = squadProgress.Count(x => x.IsSuccessfulParticipant);
        _log($"{GetTaskKind(definition)} Task '{definition.Title}': DB gelesen. Gesamt={currentTotal}, Fortschritt={progressValue}/{target}, Squads={squadProgress.Count}, ErfolgreicheSquads={successfulSquads}, Mindestbeitrag={minimumParticipationValue}, SquadFortschritt={squadProgress.Sum(x => x.Progress)}.");

        var progress = new WeeklyCommunityTaskProgress
        {
            Definition = definition,
            Baseline = baseline,
            CurrentTotal = currentTotal,
            Progress = progressValue,
            Remaining = Math.Max(0, target - progressValue),
            Percent = Math.Min(100.0, progressValue * 100.0 / target),
            IsCompleted = progressValue >= target,
            MinimumParticipationValue = minimumParticipationValue,
            MinimumParticipationPercent = minimumParticipationPercent,
            SquadProgress = squadProgress,
            UpdatedUtc = DateTime.UtcNow
        };

        if (progress.IsCompleted && baseline.CompletedUtc is null)
        {
            baseline.CompletedUtc = DateTime.UtcNow;
            progress.Baseline = baseline;
            SaveBaseline(baseline);
            _log($"{GetTaskKind(definition)} Task '{definition.Title}': Ziel erreicht.");
        }

        return progress;
    }

    public async Task<List<WeeklyCommunityTaskProgress>> ResetAllBaselinesAsync(BotSettings settings, CancellationToken cancellationToken = default)
    {
        return await ScanAllOnceAsync(settings, cancellationToken, resetBaseline: true);
    }

    public async Task<WeeklyCommunityTaskProgress?> ResetBaselineAsync(BotSettings settings, CancellationToken cancellationToken = default)
    {
        var all = await ResetAllBaselinesAsync(settings, cancellationToken);
        return all.FirstOrDefault();
    }

    private async Task SafeTickAsync(
        BotSettings settings,
        Func<CancellationToken, Task<int>> getOnlinePlayersAsync,
        Func<IReadOnlyList<WeeklyCommunityTaskProgress>, Task> publishProgressAsync,
        CancellationToken cancellationToken,
        bool force)
    {
        try
        {
            if (!settings.AutoStartWeeklyTasks) return;

            var onlinePlayers = await getOnlinePlayersAsync(cancellationToken);
            if (!force && settings.WeeklyTaskOnlyWhenPlayersOnline && onlinePlayers <= 0)
            {
                _log("Weekly Task: kein Spieler online, DB-Download uebersprungen.");
                return;
            }

            var progresses = await ScanAllOnceAsync(settings, cancellationToken);
            if (progresses.Count == 0) return;

            await publishProgressAsync(progresses);
            foreach (var progress in progresses)
            {
                var baseline = progress.Baseline;
                baseline.LastDiscordUpdateUtc = DateTime.UtcNow;
                if (baseline.LastAnnouncementUtc is null) baseline.LastAnnouncementUtc = DateTime.UtcNow;
                SaveBaseline(baseline);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // normal shutdown
        }
        catch (Exception ex)
        {
            _log("Weekly Task Fehler: " + ex.Message);
            AppLogService.WriteException("WeeklyCommunityTask", ex);
        }
    }

    public static string GetTaskKind(WeeklyCommunityTaskDefinition definition)
    {
        var type = string.IsNullOrWhiteSpace(definition.Type) ? "Weekly" : definition.Type.Trim();
        return type;
    }

    public static DateTime? GetTaskStartUtc(WeeklyCommunityTaskDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.StartUtc) &&
            DateTime.TryParse(definition.StartUtc, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var configuredStartUtc))
        {
            return configuredStartUtc;
        }

        return null;
    }

    private bool IsTaskRunnableNow(WeeklyCommunityTaskDefinition definition, DateTime nowUtc)
    {
        if (!definition.Enabled) return false;
        if (IsTaskPlannedForFuture(definition, nowUtc)) return false;
        if (IsTaskExpiredByConfigurationOrBaseline(definition, nowUtc)) return false;
        return true;
    }

    private static bool IsTaskPlannedForFuture(WeeklyCommunityTaskDefinition definition, DateTime nowUtc)
    {
        var startUtc = GetTaskStartUtc(definition);
        return startUtc.HasValue && startUtc.Value > nowUtc;
    }

    private bool IsTaskExpiredByConfigurationOrBaseline(WeeklyCommunityTaskDefinition definition, DateTime nowUtc)
    {
        var configuredStartUtc = GetTaskStartUtc(definition);
        var baseline = LoadBaseline(definition);
        var startUtc = configuredStartUtc ?? (baseline.CreatedUtc == default ? DateTime.MinValue : baseline.CreatedUtc);
        if (startUtc == DateTime.MinValue) return false;

        var endUtc = GetTaskEndUtc(definition, startUtc);
        return endUtc.HasValue && endUtc.Value <= nowUtc;
    }

    public static DateTime? GetTaskEndUtc(WeeklyCommunityTaskDefinition definition, DateTime startUtc)
    {
        if (!string.IsNullOrWhiteSpace(definition.EndUtc) &&
            DateTime.TryParse(definition.EndUtc, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var configuredEndUtc))
        {
            return configuredEndUtc;
        }

        var hours = definition.DurationHours;
        if (hours <= 0)
        {
            var kind = GetTaskKind(definition);
            if (kind.Equals("Daily", StringComparison.OrdinalIgnoreCase)) hours = 24;
            else if (kind.Equals("Weekly", StringComparison.OrdinalIgnoreCase)) hours = 24 * 7;
        }

        return hours > 0 ? startUtc.AddHours(hours) : null;
    }

    private static void ValidateDefinition(WeeklyCommunityTaskDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Id)) throw new InvalidOperationException("Weekly Task ID fehlt.");
        if (string.IsNullOrWhiteSpace(definition.StatColumn)) throw new InvalidOperationException("Weekly Task StatColumn fehlt.");
        _ = ResolveStatTarget(definition);
        if (definition.Target <= 0) throw new InvalidOperationException("Weekly Task Ziel muss groesser 0 sein.");
    }

    private static WeeklyCommunityTaskStatTarget ResolveStatTarget(WeeklyCommunityTaskDefinition definition)
    {
        var statTable = string.IsNullOrWhiteSpace(definition.StatTable) ? "survival_stats" : definition.StatTable.Trim();
        var statColumn = (definition.StatColumn ?? string.Empty).Trim();

        // Backwards-compatible shortcut: "fishing_stats.fish_caught" inside StatColumn.
        var dotIndex = statColumn.IndexOf('.');
        if (dotIndex > 0 && dotIndex < statColumn.Length - 1)
        {
            statTable = statColumn[..dotIndex];
            statColumn = statColumn[(dotIndex + 1)..];
        }

        var target = AvailableStatTargets.FirstOrDefault(x =>
            string.Equals(x.TableName, statTable, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.ColumnName, statColumn, StringComparison.OrdinalIgnoreCase));

        if (target is null)
        {
            throw new InvalidOperationException($"Weekly Task Ziel ist nicht erlaubt: {statTable}.{statColumn}");
        }

        return target;
    }

    private static long ReadStatTotal(string dbPath, WeeklyCommunityTaskStatTarget target)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        };

        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COALESCE(SUM(CAST({target.ColumnName} AS INTEGER)), 0) FROM {target.TableName}";
        var value = command.ExecuteScalar();
        return Convert.ToInt64(value ?? 0);
    }

    private static List<WeeklyCommunityTaskSquadProgress> ReadSquadStatTotals(string dbPath, WeeklyCommunityTaskStatTarget target)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        };

        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $@"
SELECT
    CAST(s.id AS TEXT) AS squad_id,
    COALESCE(NULLIF(TRIM(s.name), ''), 'Squad ohne Namen') AS squad_name,
    COALESCE(SUM(CAST(st.{target.ColumnName} AS INTEGER)), 0) AS total
FROM {target.TableName} st
INNER JOIN squad_member sm ON sm.user_profile_id = st.user_profile_id
INNER JOIN squad s ON s.id = sm.squad_id
GROUP BY s.id, s.name
ORDER BY total DESC";

        var result = new List<WeeklyCommunityTaskSquadProgress>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new WeeklyCommunityTaskSquadProgress
            {
                SquadId = reader.GetString(0),
                SquadName = reader.GetString(1),
                CurrentTotal = Convert.ToInt64(reader.GetValue(2))
            });
        }

        return result;
    }

    private static List<WeeklyCommunityTaskSquadProgress> BuildSquadProgress(
        List<WeeklyCommunityTaskSquadProgress> currentSquadTotals,
        WeeklyCommunityTaskBaseline baseline,
        long target,
        long minimumParticipationValue)
    {
        var currentById = currentSquadTotals.ToDictionary(x => x.SquadId, StringComparer.OrdinalIgnoreCase);
        var allSquadIds = currentById.Keys
            .Concat(baseline.SquadBaselineValues.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return allSquadIds
            .Select(squadId =>
            {
                currentById.TryGetValue(squadId, out var current);
                baseline.SquadBaselineValues.TryGetValue(squadId, out var squadBaseline);

                var currentTotal = current?.CurrentTotal ?? squadBaseline;
                var value = Math.Max(0, currentTotal - squadBaseline);

                return new WeeklyCommunityTaskSquadProgress
                {
                    SquadId = squadId,
                    SquadName = current?.SquadName ?? $"Squad {squadId}",
                    CurrentTotal = currentTotal,
                    BaselineValue = squadBaseline,
                    Progress = value,
                    Percent = Math.Min(100.0, value * 100.0 / Math.Max(1, target)),
                    IsSuccessfulParticipant = value >= minimumParticipationValue
                };
            })
            .OrderByDescending(x => x.Progress)
            .ThenBy(x => x.SquadName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static double GetMinimumParticipationPercent(WeeklyCommunityTaskDefinition definition)
    {
        return definition.MinimumParticipationPercent > 0 ? definition.MinimumParticipationPercent : 2.0;
    }

    private static long GetMinimumParticipationValue(long target, double percent)
    {
        var value = (long)Math.Floor(Math.Max(1, target) * percent / 100.0);
        return Math.Max(1, value);
    }

    private static WeeklyCommunityTaskBaseline LoadBaseline(WeeklyCommunityTaskDefinition definition)
    {
        var path = GetBaselinePath(definition);
        if (!File.Exists(path)) return new WeeklyCommunityTaskBaseline();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<WeeklyCommunityTaskBaseline>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new WeeklyCommunityTaskBaseline();
        }
        catch
        {
            return new WeeklyCommunityTaskBaseline();
        }
    }

    public static List<WeeklyCommunityTaskBaseline> LoadSavedBaselines()
    {
        var result = new List<WeeklyCommunityTaskBaseline>();
        foreach (var directory in GetBaselineDirectories())
        {
            if (!Directory.Exists(directory)) continue;
            foreach (var path in Directory.EnumerateFiles(directory, "*.baseline.json"))
            {
                try
                {
                    var json = File.ReadAllText(path);
                    var baseline = JsonSerializer.Deserialize<WeeklyCommunityTaskBaseline>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (baseline is null || string.IsNullOrWhiteSpace(baseline.TaskId)) continue;
                    result.Add(baseline);
                }
                catch
                {
                    // Ignore broken baseline files. The planner uses this only as a recovery helper.
                }
            }
        }

        return result
            .GroupBy(x => x.TaskId, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.OrderByDescending(b => b.LastDiscordUpdateUtc ?? b.LastAnnouncementUtc ?? b.CreatedUtc).First())
            .OrderByDescending(x => x.LastDiscordUpdateUtc ?? x.LastAnnouncementUtc ?? x.CreatedUtc)
            .ToList();
    }

    public static WeeklyCommunityTaskDefinition CreateDefinitionFromBaseline(WeeklyCommunityTaskBaseline baseline)
    {
        var table = string.IsNullOrWhiteSpace(baseline.StatTable) ? "survival_stats" : baseline.StatTable.Trim();
        var column = string.IsNullOrWhiteSpace(baseline.StatColumn) ? "puppets_killed" : baseline.StatColumn.Trim();
        var target = AvailableStatTargets.FirstOrDefault(x =>
            string.Equals(x.TableName, table, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.ColumnName, column, StringComparison.OrdinalIgnoreCase));

        var id = string.IsNullOrWhiteSpace(baseline.TaskId) ? "recovered-challenge" : baseline.TaskId.Trim();
        var kind = id.Contains("daily", StringComparison.OrdinalIgnoreCase) ? "Daily" : "Weekly";
        var title = target is null ? id : target.DisplayName;
        long targetValue = 1000;
        var description = "Aus vorhandenem Startwert wiederhergestellt. Bitte Zielwert, Laufzeit und Reward pruefen.";
        var rewardText = "Reward bitte pruefen.";
        var completedText = "Challenge abgeschlossen.";

        if (id.Equals("weekly-puppets", StringComparison.OrdinalIgnoreCase))
        {
            kind = "Weekly";
            title = "Zombie Jagd";
            targetValue = 5000;
            description = "Killt gemeinsam 5.000 Zombies.";
            rewardText = "Fuer jede Teilnehmergruppe 1 Level 1 Basebuilding Expansion Kit";
            completedText = "Krass! Ihr habt euch alle 1 Basebuilding Expansionkit verdient! Nervt diesen Admin um euer Expansion Kit zu erhalten!";
        }

        return new WeeklyCommunityTaskDefinition
        {
            Enabled = true,
            Id = id,
            Type = kind,
            Title = title,
            Description = description,
            StatTable = table,
            StatColumn = column,
            Target = targetValue,
            DurationHours = kind.Equals("Daily", StringComparison.OrdinalIgnoreCase) ? 24 : 168,
            MinimumParticipationPercent = 2.0,
            RewardText = rewardText,
            CompletedText = completedText
        };
    }

    private static void SaveBaseline(WeeklyCommunityTaskBaseline baseline)
    {
        var definition = new WeeklyCommunityTaskDefinition { Id = baseline.TaskId };
        var path = GetBaselinePath(definition);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(baseline, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string GetBaselineDirectory()
    {
        return GetBaselineDirectories().First();
    }

    private static IReadOnlyList<string> GetBaselineDirectories()
    {
        return new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RedRavenRconTool", "WeeklyTasks"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScumRconTool", "WeeklyTasks"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RedRavenRconTool", "WeeklyTasks")
        };
    }

    private static string GetBaselinePath(WeeklyCommunityTaskDefinition definition)
    {
        var safeId = string.Join("_", (definition.Id ?? "weekly").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (string.IsNullOrWhiteSpace(safeId)) safeId = "weekly";
        return Path.Combine(GetBaselineDirectory(), safeId + ".baseline.json");
    }

    public static string BuildDefaultTaskJson()
    {
        var examples = new List<WeeklyCommunityTaskDefinition>
        {
            new()
            {
                Id = "weekly-puppets",
                Type = "Weekly",
                Title = "Zombie Jagd",
                Description = "Killt gemeinsam 5.000 Zombies.",
                StatTable = "survival_stats",
                StatColumn = "puppets_killed",
                Target = 5000,
                DurationHours = 168,
                MinimumParticipationPercent = 2.0,
                RewardText = "Fuer jede Teilnehmergruppe 1 Level 1 Basebuilding Expansion Kit",
                CompletedText = "Krass! Ihr habt euch alle 1 Basebuilding Expansionkit verdient! Nervt diesen Admin, um euer Expansion Kit zu erhalten!"
            },
            new()
            {
                Id = "daily-animals",
                Type = "Daily",
                Title = "Tierische Jagd",
                Description = "Killt gemeinsam 250 Tiere.",
                StatTable = "survival_stats",
                StatColumn = "animals_killed",
                Target = 250,
                DurationHours = 24,
                MinimumParticipationPercent = 2.0,
                RewardText = "Bei Abschluss wird der Skillgain fuer Bogen und Ueberleben verdoppelt.",
                CompletedText = "Krass! Ihr habt es geschafft!"
            },
            new()
            {
                Id = "daily-fishing",
                Type = "Daily",
                Title = "Angelkoenig",
                Description = "Fangt gemeinsam 100 Fische.",
                StatTable = "fishing_stats",
                StatColumn = "fish_caught",
                Target = 100,
                DurationHours = 24,
                MinimumParticipationPercent = 2.0,
                RewardText = "Fishing Reward wird manuell freigeschaltet.",
                CompletedText = "Krass! Die Fishing Challenge wurde geschafft!"
            }
        };

        return JsonSerializer.Serialize(examples, new JsonSerializerOptions { WriteIndented = true });
    }
}
