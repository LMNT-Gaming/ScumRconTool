using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using ScumRconTool.Models;

namespace ScumRconTool.Services;

public sealed class EventEngine : IDisposable
{
    private const string LootPuppetAsset = "BP_Zombie_Civilian_Skinny_Loot";

    private readonly SourceRconClient _rcon;
    private readonly Action<string> _log;
    private readonly Action? _stateChanged;
    private readonly List<EventRuntime> _events;
    private readonly List<LootPack> _globalLootPacks;
    private readonly Random _random = new();
    private readonly BotSettings? _settings;
    private string? _scheduledRandomCycleKey;
    private int _scheduledRandomLastSlotStarted = -1;
    private readonly HashSet<string> _scheduledRandomStartedIds = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastScheduledRandomWaitLogUtc = DateTime.MinValue;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private DateTime _lastRandomizerLimitLogUtc = DateTime.MinValue;
    private DateTime _lastEngineHealthLogUtc = DateTime.MinValue;
    private DateTime _lastZoneDiagnosticLogUtc = DateTime.MinValue;

    public bool IsRunning => _loopTask is not null && !_loopTask.IsCompleted;

    public EventEngine(SourceRconClient rcon, IEnumerable<EventDefinition> definitions, Action<string> log, Action? stateChanged = null, BotSettings? settings = null, IEnumerable<LootPack>? globalLootPacks = null)
    {
        _rcon = rcon;
        _log = log;
        _stateChanged = stateChanged;
        _settings = settings;
        _globalLootPacks = (globalLootPacks ?? Enumerable.Empty<LootPack>()).ToList();
        _events = definitions.Select(x => new EventRuntime(x)).ToList();
    }

    public IReadOnlyList<EventRuntime> Events => _events;

    public IReadOnlyList<BuyableEventSummary> GetBuyableEvents() => _events
        .Where(r => r.Definition.Enabled)
        .Where(IsBuyZone)
        .Select(r => new BuyableEventSummary(
            r.Definition.Id ?? string.Empty,
            r.Definition.Name ?? string.Empty,
            ResolveBuyEventDisplayName(r.Definition),
            Math.Max(0, r.Definition.BuyPrice),
            r.State,
            r.CooldownUntilUtc))
        .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public async Task<BuyEventActivationResult> ActivateBuyEventAsync(string eventKey, string steamId, string playerName, CancellationToken cancellationToken = default)
    {
        var runtime = FindBuyEventRuntime(eventKey);
        if (runtime is null)
        {
            return BuyEventActivationResult.Fail("Event nicht gefunden oder nicht kaufbar.");
        }

        var now = DateTime.UtcNow;
        if (runtime.State == EventRuntimeState.Cooldown && runtime.CooldownUntilUtc <= now)
        {
            ResetRuntime(runtime, clearCooldown: true);
            SetState(runtime, EventRuntimeState.Stopped, "Cooldown beendet und Runtime fuer BuyEvent zurueckgesetzt.");
        }

        var availability = GetActivationBlockReason(runtime, now);
        if (!string.IsNullOrWhiteSpace(availability))
        {
            return BuyEventActivationResult.Fail(availability);
        }

        ScumPlayer? player = null;
        if (!string.IsNullOrWhiteSpace(steamId))
        {
            try
            {
                var players = await FetchPlayersAsync(cancellationToken);
                player = players.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.UserId) && p.UserId.Equals(steamId.Trim(), StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _log($"{runtime.Definition.Name}: Spielerposition fuer BuyEvent konnte nicht gelesen werden: {ex.Message}");
                AppLogService.WriteException("ScriptEngine.BuyEvent.PlayerLookup", ex);
            }
        }

        player ??= new ScumPlayer
        {
            UserId = steamId,
            SteamName = playerName
        };

        await InitiateAsync(runtime, player, cancellationToken, manual: true);
        return BuyEventActivationResult.Ok(runtime.Definition.Name ?? ResolveBuyEventDisplayName(runtime.Definition));
    }

    public void Start(int pollSeconds)
    {
        if (IsRunning)
        {
            _log("Script Engine laeuft bereits. Start wurde ignoriert, damit Runtime-States nicht zurueckgesetzt werden.");
            LogRuntimeSummary("Aktueller Script-Status");
            return;
        }

        if (pollSeconds < 2) pollSeconds = 2;

        InitializeLocalStates(DateTime.UtcNow);

        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => LoopAsync(TimeSpan.FromSeconds(pollSeconds), _cts.Token));
        _log($"Script Engine gestartet. Poll: {pollSeconds}s");
        LogRuntimeSummary("Script Engine Initialisierung");
        _stateChanged?.Invoke();
    }

    public void Stop()
    {
        var wasRunning = IsRunning;
        _cts?.Cancel();
        foreach (var runtime in _events)
        {
            if (runtime.State != EventRuntimeState.Cooldown)
            {
                runtime.State = EventRuntimeState.Stopped;
            }
        }
        if (wasRunning)
        {
            _log("Script Engine gestoppt.");
        }
        _stateChanged?.Invoke();
    }

    public async Task ManualAnnounceAsync(EventRuntime runtime, CancellationToken cancellationToken = default)
    {
        await InitiateAsync(runtime, null, cancellationToken, manual: true);
    }

    public async Task ManualScanAsync(CancellationToken cancellationToken = default)
    {
        var players = await FetchPlayersAsync(cancellationToken);
        await EvaluateAsync(players, cancellationToken);
    }

    private async Task LoopAsync(TimeSpan pollInterval, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var players = await FetchPlayersAsync(cancellationToken);
                await EvaluateAsync(players, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException ex) when (!cancellationToken.IsCancellationRequested)
            {
                _log("Script Engine Fehler: RCON-Client wurde beendet. Engine-Loop wird gestoppt, um Reconnect-/Auth-Spam zu verhindern.");
                AppLogService.WriteException("ScriptEngine.Loop.ObjectDisposed", ex);
                break;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _log("Script Engine Fehler: " + ex.Message + " - Engine bleibt aktiv, aber RCON-Reconnects sind jetzt gedrosselt.");
                AppLogService.WriteException("ScriptEngine.Loop", ex);
            }

            try
            {
                await Task.Delay(pollInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<List<ScumPlayer>> FetchPlayersAsync(CancellationToken cancellationToken)
    {
        var response = await _rcon.SendCommandAsync(CommandRegistry.ListPlayersJson(), cancellationToken);
        var players = PlayerParser.ParseListPlayersJson(response);
        _log($"Spieler-Scan: {players.Count} online.");
        return players;
    }

    private async Task EvaluateAsync(List<ScumPlayer> players, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        InitializeLocalStates(now);


        if (_settings?.RandomQuestScheduledMode == true)
        {
            await RunScheduledRandomQuestsAsync(players, now, cancellationToken);
        }
        else
        {
            await RunRandomizerAsync(players, now, cancellationToken);
        }

        await RunRandomActivatedAsync(players, now, cancellationToken);

        if (now - _lastEngineHealthLogUtc >= TimeSpan.FromMinutes(5))
        {
            LogRuntimeSummary("Script Engine Health");
            _lastEngineHealthLogUtc = now;
        }

        if (now - _lastZoneDiagnosticLogUtc >= TimeSpan.FromMinutes(1))
        {
            LogZoneDiagnostics(players);
            _lastZoneDiagnosticLogUtc = now;
        }

        foreach (var runtime in _events.Where(e => e.Definition.Enabled))
        {
            if (runtime.State == EventRuntimeState.Cooldown || runtime.State == EventRuntimeState.Stopped)
            {
                continue;
            }

            if (runtime.State == EventRuntimeState.Initiated)
            {
                await MaybeRepeatInitiatorAsync(runtime, now, cancellationToken);
            }

            var playersInZone = GetPlayersInZone(runtime, players);

            if (playersInZone.Count > 0)
            {
                runtime.LastOccupiedUtc = now;
                runtime.EmptySinceUtc = null;

                if (runtime.State == EventRuntimeState.Initiated)
                {
                    var triggerPlayer = playersInZone[0];
                    if (IsGroupBlocked(runtime))
                    {
                        _log($"{runtime.Definition.Name}: Spieler in Zone, aber Scriptgruppe '{runtime.Definition.EventGroup}' ist bereits aktiv. Script bleibt initiiert.");
                    }
                    else
                    {
                        await GoLiveAsync(runtime, triggerPlayer, cancellationToken);
                    }
                }
                else if (runtime.State == EventRuntimeState.CleanupPending)
                {
                    SetState(runtime, EventRuntimeState.Live, "Spieler wieder in der Zone. Cleanup abgebrochen.");
                }
            }
            else if (runtime.State == EventRuntimeState.Live)
            {
                runtime.EmptySinceUtc = now;
                SetState(runtime, EventRuntimeState.CleanupPending, "Zone leer. Cleanup-/Cooldown-Timer gestartet.");
            }
            else if (runtime.State == EventRuntimeState.CleanupPending && runtime.EmptySinceUtc is not null)
            {
                var emptyFor = now - runtime.EmptySinceUtc.Value;
                if (emptyFor >= TimeSpan.FromSeconds(runtime.Definition.CleanupWhenEmptySeconds))
                {
                    _log($"{runtime.Definition.Name}: EmptyBlock wird ausgefuehrt.");
                    await ExecuteBlockAsync(runtime.Definition.EmptyBlock, runtime.Definition.GetEmptyCommands(), runtime, null, cancellationToken);
                    await ExecuteBlockAsync(runtime.Definition.CleanupBlock, runtime.Definition.CleanupBlock.Commands, runtime, null, cancellationToken);
                    runtime.CooldownUntilUtc = now.AddMinutes(runtime.Definition.CooldownMinutes);
                    SetState(runtime, EventRuntimeState.Cooldown, $"Cooldown bis {runtime.CooldownUntilUtc.ToLocalTime():HH:mm:ss}.");
                }
            }
        }
    }

    private void InitializeLocalStates(DateTime now)
    {
        foreach (var runtime in _events)
        {
            if (!runtime.Definition.Enabled)
            {
                if (runtime.State != EventRuntimeState.Stopped)
                {
                    SetState(runtime, EventRuntimeState.Stopped, "Script ist deaktiviert.");
                }
                continue;
            }

            if (runtime.Definition.EffectiveZone.Radius <= 0 && !IsDirectLive(runtime))
            {
                _log($"{runtime.Definition.Name}: WARNUNG Aktivierzone hat Radius <= 0 und kann nicht triggern.");
                continue;
            }

            if (runtime.State == EventRuntimeState.Cooldown && runtime.CooldownUntilUtc <= now)
            {
                ResetRuntime(runtime, clearCooldown: true);
                SetState(runtime, IsSilentZone(runtime) ? EventRuntimeState.Initiated : EventRuntimeState.Stopped, "Cooldown beendet und Runtime sauber zurueckgesetzt.");
            }

            if (IsSilentZone(runtime) && runtime.State == EventRuntimeState.Stopped)
            {
                ResetRuntime(runtime, clearCooldown: false);
                SetState(runtime, EventRuntimeState.Initiated, "Silent-Script ist scharf und wartet auf Spieler in der Aktivierzone.");
            }
        }
    }

    private void ResetRuntime(EventRuntime runtime, bool clearCooldown)
    {
        runtime.EmptySinceUtc = null;
        runtime.LastOccupiedUtc = DateTime.MinValue;
        runtime.LastLiveUtc = DateTime.MinValue;
        runtime.LastInitiatedUtc = DateTime.MinValue;
        runtime.LastInitiatorRepeatUtc = DateTime.MinValue;
        if (clearCooldown)
        {
            runtime.CooldownUntilUtc = DateTime.MinValue;
        }
    }

    private void LogRuntimeSummary(string prefix)
    {
        var enabled = _events.Count(e => e.Definition.Enabled);
        var silent = _events.Count(e => e.Definition.Enabled && IsSilentZone(e));
        var random = _events.Count(e => e.Definition.Enabled && IsRandomAnnouncedZone(e) && e.Definition.IncludeInRandomizer);
        var randomActivated = _events.Count(e => e.Definition.Enabled && IsRandomActivated(e));
        var buyzone = _events.Count(e => e.Definition.Enabled && IsBuyZone(e));
        var initiated = _events.Count(e => e.State == EventRuntimeState.Initiated);
        var live = _events.Count(e => e.State == EventRuntimeState.Live);
        var cooldown = _events.Count(e => e.State == EventRuntimeState.Cooldown);
        _log($"{prefix}: {enabled} aktivierte Scripts, {silent} SilentZone, {random} Random, {randomActivated} RandomActivated, {buyzone} Buyzone, Status: {initiated} initiiert, {live} live, {cooldown} cooldown.");

        foreach (var runtime in _events.Where(e => e.Definition.Enabled && e.State != EventRuntimeState.Stopped))
        {
            _log($" - {runtime.Definition.Name}: {runtime.State}");
        }
    }

    private void LogZoneDiagnostics(List<ScumPlayer> players)
    {
        var waiting = _events
            .Where(e => e.Definition.Enabled && e.State == EventRuntimeState.Initiated)
            .ToList();

        if (waiting.Count == 0)
        {
            return;
        }

        if (players.Count == 0)
        {
            _log($"Script Engine Diagnose: {waiting.Count} initiiert, aber keine Spieler online.");
            return;
        }

        foreach (var runtime in waiting)
        {
            var zone = runtime.Definition.EffectiveZone;
            var nearest = players
                .Where(p => p.Location is not null)
                .Select(p => new { Player = p, Distance = p.Location!.Distance2DTo(zone.Center) })
                .OrderBy(x => x.Distance)
                .FirstOrDefault();

            if (nearest is null)
            {
                _log($"Script Engine Diagnose: {runtime.Definition.Name} wartet, aber #ListPlayersJson liefert keine Positionen.");
                continue;
            }

            var inside = nearest.Distance <= zone.Radius;
            var groupInfo = !string.IsNullOrWhiteSpace(runtime.Definition.EventGroup)
                ? $", Gruppe={runtime.Definition.EventGroup}, blockiert={IsGroupBlocked(runtime)}"
                : string.Empty;
            _log($"Script Engine Diagnose: {runtime.Definition.Name} wartet. Naechster Spieler {nearest.Player.DisplayName}: {nearest.Distance:0} / Radius {zone.Radius:0} -> {(inside ? "IN ZONE" : "ausserhalb")}{groupInfo}.");
        }
    }

    private bool IsGroupBlocked(EventRuntime runtime)
    {
        var group = runtime.Definition.EventGroup?.Trim();
        if (string.IsNullOrWhiteSpace(group))
        {
            return false;
        }

        var limit = runtime.Definition.MaxConcurrentInGroup;
        if (limit <= 0)
        {
            limit = _events
                .Where(e => e.Definition.Enabled)
                .Where(e => string.Equals(e.Definition.EventGroup?.Trim(), group, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Definition.MaxConcurrentInGroup)
                .Where(x => x > 0)
                .DefaultIfEmpty(1)
                .Min();
        }

        var active = _events
            .Where(e => !ReferenceEquals(e, runtime))
            .Where(e => e.Definition.Enabled)
            .Where(e => string.Equals(e.Definition.EventGroup?.Trim(), group, StringComparison.OrdinalIgnoreCase))
            .Count(e => e.State == EventRuntimeState.Live || e.State == EventRuntimeState.CleanupPending || e.State == EventRuntimeState.Cooldown);

        return active >= limit;
    }

    private async Task RunScheduledRandomQuestsAsync(List<ScumPlayer> players, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var randomScripts = _events
            .Where(r => r.Definition.Enabled)
            .Where(r => IsRandomAnnouncedZone(r))
            .Where(r => r.Definition.IncludeInRandomizer)
            .ToList();

        if (randomScripts.Count == 0)
        {
            return;
        }

        var settings = _settings;
        var intervalMinutes = Math.Max(1, settings?.RandomQuestIntervalMinutes ?? 60);
        var startDelayMinutes = Math.Max(0, settings?.RandomQuestStartDelayMinutes ?? 0);
        var nowLocal = DateTime.Now;
        var cycleStart = GetLatestScheduledRestartLocal(nowLocal, settings?.RandomQuestRestartTimes ?? "04:00,10:00,16:00,22:00");
        var cycleKey = cycleStart.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);

        if (!string.Equals(_scheduledRandomCycleKey, cycleKey, StringComparison.Ordinal))
        {
            _scheduledRandomCycleKey = cycleKey;
            _scheduledRandomLastSlotStarted = -1;
            _scheduledRandomStartedIds.Clear();

            foreach (var runtime in randomScripts)
            {
                ResetRuntime(runtime, clearCooldown: true);
                if (runtime.State != EventRuntimeState.Stopped)
                {
                    runtime.State = EventRuntimeState.Stopped;
                }
                runtime.NextRandomizerUtc = DateTime.MinValue;
            }

            _log($"RandomQuest Scheduler: neuer Restart-Zyklus seit {cycleStart:dd.MM. HH:mm}. Random-Scripts wurden fuer den neuen Zyklus zurueckgesetzt.");
            _stateChanged?.Invoke();
        }

        var elapsed = nowLocal - cycleStart.AddMinutes(startDelayMinutes);
        if (elapsed < TimeSpan.Zero)
        {
            return;
        }

        var slot = (int)Math.Floor(elapsed.TotalMinutes / intervalMinutes);
        if (slot <= _scheduledRandomLastSlotStarted)
        {
            return;
        }

        var candidates = randomScripts
            .Where(r => r.State == EventRuntimeState.Stopped)
            .Where(r => nowUtc >= r.CooldownUntilUtc)
            .Where(r => !_scheduledRandomStartedIds.Contains(r.Definition.Id ?? r.Definition.Name ?? string.Empty))
            .Where(r => GetPlayersInZone(r, players).Count == 0)
            .ToList();

        if (candidates.Count == 0)
        {
            var stopped = randomScripts
                .Where(r => r.State == EventRuntimeState.Stopped)
                .Where(r => nowUtc >= r.CooldownUntilUtc)
                .Where(r => GetPlayersInZone(r, players).Count == 0)
                .ToList();

            if (stopped.Count > 0)
            {
                _scheduledRandomStartedIds.Clear();
                candidates = stopped;
            }
        }

        if (candidates.Count == 0)
        {
            if (nowUtc - _lastScheduledRandomWaitLogUtc >= TimeSpan.FromMinutes(5))
            {
                var active = randomScripts.Count(IsRandomScriptActive);
                _log($"RandomQuest Scheduler wartet: Slot {slot + 1} ist faellig, aber kein gestopptes Random-Script ist verfuegbar. Aktiv/Initiiert: {active}, Random-Scripts gesamt: {randomScripts.Count}.");
                _lastScheduledRandomWaitLogUtc = nowUtc;
            }
            return;
        }

        var selected = candidates[_random.Next(candidates.Count)];
        _log($"RandomQuest Scheduler: Slot {slot + 1} nach Restart {cycleStart:HH:mm} startet '{selected.Definition.Name}'. Naechster Slot in {intervalMinutes} Minuten.");
        await InitiateAsync(selected, null, cancellationToken, manual: false);

        _scheduledRandomLastSlotStarted = slot;
        _scheduledRandomStartedIds.Add(selected.Definition.Id ?? selected.Definition.Name ?? Guid.NewGuid().ToString("N"));
    }

    private static DateTime GetLatestScheduledRestartLocal(DateTime nowLocal, string restartTimes)
    {
        var times = ParseRestartTimes(restartTimes);
        var candidates = times
            .Select(t => nowLocal.Date.Add(t))
            .Concat(times.Select(t => nowLocal.Date.AddDays(-1).Add(t)))
            .Where(dt => dt <= nowLocal)
            .ToList();

        return candidates.Count == 0 ? nowLocal.Date : candidates.Max();
    }

    private static List<TimeSpan> ParseRestartTimes(string restartTimes)
    {
        var result = new List<TimeSpan>();
        var parts = (restartTimes ?? string.Empty).Split(new[] { ',', ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in parts)
        {
            if (TimeSpan.TryParse(part, CultureInfo.InvariantCulture, out var ts))
            {
                result.Add(ts);
                continue;
            }

            if (part.Length == 4 && int.TryParse(part[..2], out var hour) && int.TryParse(part[2..], out var minute))
            {
                if (hour >= 0 && hour <= 23 && minute >= 0 && minute <= 59)
                {
                    result.Add(new TimeSpan(hour, minute, 0));
                }
            }
        }

        if (result.Count == 0)
        {
            result.Add(new TimeSpan(4, 0, 0));
            result.Add(new TimeSpan(10, 0, 0));
            result.Add(new TimeSpan(16, 0, 0));
            result.Add(new TimeSpan(22, 0, 0));
        }

        return result.Distinct().OrderBy(x => x).ToList();
    }

    private async Task RunRandomizerAsync(List<ScumPlayer> players, DateTime now, CancellationToken cancellationToken)
    {
        var randomScripts = _events
            .Where(r => r.Definition.Enabled)
            .Where(r => IsRandomAnnouncedZone(r))
            .Where(r => r.Definition.IncludeInRandomizer)
            .ToList();

        if (randomScripts.Count == 0)
        {
            return;
        }

        var positiveLimits = randomScripts
            .Select(r => r.Definition.MaxConcurrentRandomEvents)
            .Where(limit => limit > 0)
            .ToList();

        var maxConcurrent = positiveLimits.Count == 0 ? 0 : positiveLimits.Min();
        var activeRandomCount = randomScripts.Count(IsRandomScriptActive);

        if (maxConcurrent > 0 && activeRandomCount >= maxConcurrent)
        {
            if (now - _lastRandomizerLimitLogUtc >= TimeSpan.FromMinutes(5))
            {
                _log($"Randomizer wartet: {activeRandomCount}/{maxConcurrent} Random-Scripts sind bereits aktiv.");
                _lastRandomizerLimitLogUtc = now;
            }
            return;
        }

        var candidates = randomScripts
            .Where(r => r.State == EventRuntimeState.Stopped)
            .Where(r => now >= r.NextRandomizerUtc)
            .Where(r => GetPlayersInZone(r, players).Count == 0)
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        var selected = candidates[_random.Next(candidates.Count)];
        await InitiateAsync(selected, null, cancellationToken, manual: false);

        var next = now.AddMinutes(Math.Max(1, selected.Definition.RandomizerEveryMinutes > 0 ? selected.Definition.RandomizerEveryMinutes : selected.Definition.AnnounceEveryMinutes));
        foreach (var candidate in candidates)
        {
            candidate.NextRandomizerUtc = next;
        }
    }

    private async Task RunRandomActivatedAsync(List<ScumPlayer> players, DateTime now, CancellationToken cancellationToken)
    {
        var candidates = _events
            .Where(r => r.Definition.Enabled)
            .Where(IsRandomActivated)
            .Where(r => r.State == EventRuntimeState.Stopped)
            .Where(r => now >= r.CooldownUntilUtc)
            .Where(r => now >= r.NextRandomizerUtc)
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        foreach (var runtime in candidates)
        {
            var intervalMinutes = Math.Max(1, runtime.Definition.RandomizerEveryMinutes > 0 ? runtime.Definition.RandomizerEveryMinutes : runtime.Definition.AnnounceEveryMinutes);
            runtime.NextRandomizerUtc = now.AddMinutes(intervalMinutes);

            var playersInZone = GetPlayersInZone(runtime, players);
            if (playersInZone.Count == 0)
            {
                continue;
            }

            if (IsGroupBlocked(runtime))
            {
                _log($"{runtime.Definition.Name}: RandomActivated faellig, aber Scriptgruppe '{runtime.Definition.EventGroup}' ist bereits aktiv.");
                continue;
            }

            var chance = Math.Clamp(runtime.Definition.RandomActivationChancePercent, 0, 100);
            if (chance <= 0)
            {
                continue;
            }

            var roll = _random.Next(1, 101);
            if (roll > chance)
            {
                _log($"{runtime.Definition.Name}: RandomActivated nicht ausgeloest. Wurf {roll}/100, Chance {chance}%.");
                continue;
            }

            var triggerPlayer = playersInZone[_random.Next(playersInZone.Count)];
            _log($"{runtime.Definition.Name}: RandomActivated ausgeloest durch belegte Zone. Wurf {roll}/100, Chance {chance}%.");
            await InitiateAsync(runtime, triggerPlayer, cancellationToken, manual: false);
        }
    }

    private static bool IsRandomScriptActive(EventRuntime runtime) =>
        runtime.State == EventRuntimeState.Initiated ||
        runtime.State == EventRuntimeState.Live ||
        runtime.State == EventRuntimeState.CleanupPending;


    private async Task MaybeRepeatInitiatorAsync(EventRuntime runtime, DateTime now, CancellationToken cancellationToken)
    {
        var repeatMinutes = runtime.Definition.InitiatorRepeatEveryMinutes;
        if (repeatMinutes <= 0)
        {
            return;
        }

        var nextRepeat = runtime.LastInitiatorRepeatUtc.AddMinutes(repeatMinutes);
        if (now < nextRepeat)
        {
            return;
        }

        _log($"{runtime.Definition.Name}: InitiatorBlock Wiederholung startet.");
        await ExecuteBlockAsync(runtime.Definition.InitiatorBlock, runtime.Definition.GetInitiatorCommands(), runtime, null, cancellationToken);
        runtime.LastInitiatorRepeatUtc = now;
    }

    private async Task InitiateAsync(EventRuntime runtime, ScumPlayer? player, CancellationToken cancellationToken, bool manual)
    {
        if (runtime.State == EventRuntimeState.Cooldown)
        {
            _log($"{runtime.Definition.Name}: kann nicht initiiert werden, Script ist im Cooldown.");
            return;
        }

        _log($"{runtime.Definition.Name}: InitiatorBlock startet{(manual ? " (manuell)" : "")}.");
        await ExecuteBlockAsync(runtime.Definition.InitiatorBlock, runtime.Definition.GetInitiatorCommands(), runtime, player, cancellationToken);
        runtime.LastInitiatedUtc = DateTime.UtcNow;
        runtime.LastInitiatorRepeatUtc = runtime.LastInitiatedUtc;
        SetState(runtime, EventRuntimeState.Initiated, "initiiert und wartet auf Aktivierzone.");

        if (IsDirectLive(runtime))
        {
            await GoLiveAsync(runtime, player, cancellationToken);
        }
    }

    private async Task GoLiveAsync(EventRuntime runtime, ScumPlayer? triggerPlayer, CancellationToken cancellationToken)
    {
        _log($"{runtime.Definition.Name}: LiveBlock startet" + (triggerPlayer is null ? "." : $" durch {triggerPlayer.DisplayName} ({triggerPlayer.UserId})."));
        await SendTriggerServerMessageAsync(runtime, triggerPlayer, cancellationToken);
        if (runtime.Definition.ActivationDelayMs > 0)
        {
            _log($"{runtime.Definition.Name}: Aktivierungs-Timer wartet {runtime.Definition.ActivationDelayMs}ms.");
            await Task.Delay(runtime.Definition.ActivationDelayMs, cancellationToken);
        }

        await ExecuteBlockAsync(runtime.Definition.PreLiveCleanupBlock, runtime.Definition.PreLiveCleanupBlock.Commands, runtime, triggerPlayer, cancellationToken);
        await ExecuteBlockAsync(runtime.Definition.LiveBlock, runtime.Definition.GetLiveCommands(), runtime, triggerPlayer, cancellationToken);
        await ExecuteSpawnBlocksAsync(runtime, triggerPlayer, cancellationToken);
        var lootPackSpawned = await ExecuteRandomLootPackAsync(runtime, triggerPlayer, cancellationToken);
        if (!lootPackSpawned)
        {
            await ExecuteRandomLootCommandPackAsync(runtime, triggerPlayer, cancellationToken);
        }
        else if (runtime.Definition.LootCommandPacks?.Any(pack => pack.Enabled && !string.IsNullOrWhiteSpace(pack.Command)) == true)
        {
            _log($"{runtime.Definition.Name}: alte LootCommandPacks ignoriert, weil bereits ein LootPack ausgegeben wurde.");
        }
        runtime.LastLiveUtc = DateTime.UtcNow;
        SetState(runtime, EventRuntimeState.Live, "live.");
    }

    private async Task SendTriggerServerMessageAsync(EventRuntime runtime, ScumPlayer? triggerPlayer, CancellationToken cancellationToken)
    {
        var message = ReplacePlaceholders(runtime.Definition.TriggerServerMessage, runtime, triggerPlayer).Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var command = CommandRegistry.Broadcast(runtime.Definition.TriggerServerMessageType, message);
        MarkRuntime(runtime, "Trigger Servernachricht gesendet", command);
        await SendAsync(command, cancellationToken);
    }

    private List<ScumPlayer> GetPlayersInZone(EventRuntime runtime, List<ScumPlayer> players)
    {
        var zone = runtime.Definition.EffectiveZone;
        return players
            .Where(p => p.Location is not null && p.Location.Distance2DTo(zone.Center) <= zone.Radius)
            .ToList();
    }


    private async Task ExecuteRandomLootCommandPackAsync(EventRuntime runtime, ScumPlayer? player, CancellationToken cancellationToken)
    {
        var packs = (runtime.Definition.LootCommandPacks ?? new List<LootCommandPack>())
            .Where(p => p.Enabled && !string.IsNullOrWhiteSpace(p.Command))
            .ToList();

        if (packs.Count == 0)
        {
            return;
        }

        var totalWeight = packs.Sum(p => Math.Max(1, p.Weight));
        var roll = _random.Next(1, totalWeight + 1);
        LootCommandPack selected = packs[0];
        foreach (var pack in packs)
        {
            roll -= Math.Max(1, pack.Weight);
            if (roll <= 0)
            {
                selected = pack;
                break;
            }
        }

        var command = ReplacePlaceholders(selected.Command, runtime, player).Trim();
        if (!string.IsNullOrWhiteSpace(selected.Location) &&
            !Regex.IsMatch(command, @"\bLocation\b", RegexOptions.IgnoreCase))
        {
            var location = ReplacePlaceholders(selected.Location, runtime, player).Trim();
            command += " Location \"" + location + "\"";
        }

        var unresolvedPlaceholders = Regex.Matches(command, @"\{[A-Za-z][A-Za-z0-9_]*\}")
            .Cast<Match>()
            .Select(m => m.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (unresolvedPlaceholders.Count > 0)
        {
            _log("LootCommandPack uebersprungen, Platzhalter nicht aufgeloest (" + string.Join(", ", unresolvedPlaceholders) + "): " + command);
            return;
        }

        var normalizedCommand = NormalizeCommandBeforeSend(EnsureExecAsForTriggerPlayer(command, player));
        _log($"{runtime.Definition.Name}: LootCommandPack gewaehlt: {selected.Name}");
        runtime.LastLootSummary = "LootCommandPack: " + selected.Name;
        runtime.SpawnedLootCount++;
        MarkRuntime(runtime, "LootCommandPack gespawnt: " + selected.Name, normalizedCommand);
        await SendAsync(normalizedCommand, cancellationToken);

        if (selected.DelayMs > 0)
        {
            await Task.Delay(selected.DelayMs, cancellationToken);
        }
    }

    private async Task<bool> ExecuteRandomLootPackAsync(EventRuntime runtime, ScumPlayer? player, CancellationToken cancellationToken)
    {
        var packs = ResolveLootPacks(runtime)
            .Where(p => p.Enabled && p.Items.Any(item => !string.IsNullOrWhiteSpace(item.Item)))
            .ToList();

        if (packs.Count == 0)
        {
            return false;
        }

        var locations = ResolveLootPackLocations(runtime, player);
        if (locations.Count == 0)
        {
            return false;
        }

        var mode = runtime.Definition.LootPackSpawnMode?.Trim() ?? "OneTotal";
        if (mode.Equals("OnePerLocation", StringComparison.OrdinalIgnoreCase) ||
            mode.Equals("PerLocation", StringComparison.OrdinalIgnoreCase) ||
            mode.Equals("AllPoints", StringComparison.OrdinalIgnoreCase) ||
            mode.Equals("AllePunkte", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var location in locations)
            {
                var selected = SelectWeighted(packs);
                await SpawnLootPackAsync(runtime, selected, location, player, cancellationToken);
            }
            return true;
        }

        // Single/OneTotal = genau ein Pack aus allen Packs an einem Lootpunkt.
        var one = SelectWeighted(packs);
        var oneLocation = locations[_random.Next(locations.Count)];
        await SpawnLootPackAsync(runtime, one, oneLocation, player, cancellationToken);
        return true;
    }

    private LootPack SelectWeighted(List<LootPack> packs)
    {
        var totalWeight = packs.Sum(p => Math.Max(1, p.Weight));
        var roll = _random.Next(1, totalWeight + 1);
        var selected = packs[0];
        foreach (var pack in packs)
        {
            roll -= Math.Max(1, pack.Weight);
            if (roll <= 0)
            {
                selected = pack;
                break;
            }
        }
        return selected;
    }

    private IEnumerable<LootPack> ResolveLootPacks(EventRuntime runtime)
    {
        var selectedNames = runtime.Definition.LootPackNames?
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (selectedNames.Count > 0)
        {
            return _globalLootPacks.Where(pack => selectedNames.Contains(pack.Name ?? ""));
        }

        return runtime.Definition.LootPacks ?? new List<LootPack>();
    }

    private List<string> ResolveLootPackLocations(EventRuntime runtime, ScumPlayer? player)
    {
        var locations = new List<string>();
        foreach (var variable in runtime.Definition.LocalVariables?.LootSpawnLocations ?? new List<ScriptLocationVariable>())
        {
            var location = NormalizeLocationKey(ReplacePlaceholders(variable.Location, runtime, player));
            if (!string.IsNullOrWhiteSpace(location))
            {
                locations.Add(location);
            }
        }

        if (locations.Count == 0)
        {
            foreach (var legacyPackLocation in (runtime.Definition.LootPacks ?? new List<LootPack>()).Select(pack => pack.Location ?? ""))
            {
                var location = NormalizeLocationKey(ReplacePlaceholders(legacyPackLocation, runtime, player));
                if (!string.IsNullOrWhiteSpace(location))
                {
                    locations.Add(location);
                }
            }
        }

        if (locations.Count == 0)
        {
            var triggerZone = NormalizeLocationKey(ReplacePlaceholders("{triggerZone}", runtime, player));
            if (!string.IsNullOrWhiteSpace(triggerZone))
            {
                locations.Add(triggerZone);
            }
        }

        return locations.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task SpawnLootPackAsync(EventRuntime runtime, LootPack selected, string location, ScumPlayer? player, CancellationToken cancellationToken)
    {
        _log($"{runtime.Definition.Name}: LootPack gewaehlt: {selected.Name}");
        runtime.LastLootSummary = "LootPack: " + selected.Name + " @ " + location;
        var spawnedCount = 0;

        foreach (var item in selected.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Item))
            {
                continue;
            }

            var quantity = Math.Max(1, item.Quantity);
            var command = EnsureExecAsForTriggerPlayer($"#SpawnItem {item.Item.Trim()} {quantity} Location \"{location}\"", player);
            spawnedCount += quantity;
            runtime.SpawnedLootCount += quantity;
            MarkRuntime(runtime, $"Loot gespawnt: {item.Item.Trim()} x{quantity}", command);
            await SendAsync(command, cancellationToken);

            if (item.DelayMs > 0)
            {
                await Task.Delay(item.DelayMs, cancellationToken);
            }
        }
    }

    private static string NormalizeLocationKey(string location)
    {
        return Regex.Replace(location ?? string.Empty, @"\s+", " ").Trim();
    }

    private static string ReplacePlaceholders(string input, EventRuntime runtime, ScumPlayer? player)
    {
        var values = BuildPlaceholderValues(runtime, player);

        var result = input ?? string.Empty;
        foreach (var pair in values)
        {
            result = result.Replace("{" + pair.Key + "}", pair.Value, StringComparison.OrdinalIgnoreCase);
        }
        return result;
    }

    private static bool IsRandomAnnouncedZone(EventRuntime runtime) =>
        runtime.Definition.Mode.Equals("RandomAnnouncedZone", StringComparison.OrdinalIgnoreCase) ||
        runtime.Definition.Mode.Equals("Random", StringComparison.OrdinalIgnoreCase) ||
        runtime.Definition.Mode.Equals("A", StringComparison.OrdinalIgnoreCase) ||
        runtime.Definition.Mode.Equals("AnnouncedThenZone", StringComparison.OrdinalIgnoreCase);

    private static bool IsRandomActivated(EventRuntime runtime) =>
        runtime.Definition.Mode.Equals("RandomActivated", StringComparison.OrdinalIgnoreCase) ||
        runtime.Definition.Mode.Equals("RandomActivatedZone", StringComparison.OrdinalIgnoreCase);

    private static bool IsBuyZone(EventRuntime runtime) =>
        runtime.Definition.Mode.Equals("Buyzone", StringComparison.OrdinalIgnoreCase) ||
        runtime.Definition.Mode.Equals("BuyZone", StringComparison.OrdinalIgnoreCase) ||
        runtime.Definition.Mode.Equals("BuyEvent", StringComparison.OrdinalIgnoreCase);

    private static bool IsSilentZone(EventRuntime runtime) =>
        runtime.Definition.Mode.Equals("SilentZone", StringComparison.OrdinalIgnoreCase) ||
        runtime.Definition.Mode.Equals("B", StringComparison.OrdinalIgnoreCase) ||
        runtime.Definition.Mode.Equals("Silent", StringComparison.OrdinalIgnoreCase);

    private static bool IsDirectLive(EventRuntime runtime) =>
        runtime.Definition.Mode.Equals("DirectLive", StringComparison.OrdinalIgnoreCase) ||
        IsBuyZone(runtime);

    private EventRuntime? FindBuyEventRuntime(string eventKey)
    {
        var key = (eventKey ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var buyEvents = _events
            .Where(r => r.Definition.Enabled)
            .Where(IsBuyZone)
            .ToList();

        return buyEvents.FirstOrDefault(r => MatchesBuyEventKey(r.Definition, key, exactOnly: true))
            ?? buyEvents.FirstOrDefault(r => MatchesBuyEventKey(r.Definition, key, exactOnly: false));
    }

    private static bool MatchesBuyEventKey(EventDefinition definition, string key, bool exactOnly)
    {
        var values = new[]
        {
            definition.BuyAlias,
            definition.Name,
            definition.Id
        };

        if (values.Any(value => !string.IsNullOrWhiteSpace(value) && value.Trim().Equals(key, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (exactOnly)
        {
            return false;
        }

        var normalizedKey = NormalizeBuyEventKey(key);
        return values.Any(value => !string.IsNullOrWhiteSpace(value) && NormalizeBuyEventKey(value).Equals(normalizedKey, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeBuyEventKey(string? value) =>
        Regex.Replace((value ?? string.Empty).Trim(), @"[\s_\-]+", "", RegexOptions.CultureInvariant).ToLowerInvariant();

    private static string ResolveBuyEventDisplayName(EventDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.BuyAlias)) return definition.BuyAlias.Trim();
        if (!string.IsNullOrWhiteSpace(definition.Name)) return definition.Name.Trim();
        return string.IsNullOrWhiteSpace(definition.Id) ? "Event" : definition.Id.Trim();
    }

    private string GetActivationBlockReason(EventRuntime runtime, DateTime now)
    {
        if (!runtime.Definition.Enabled)
        {
            return "Event ist deaktiviert.";
        }

        if (runtime.State == EventRuntimeState.Cooldown && runtime.CooldownUntilUtc > now)
        {
            return "Event ist noch im Cooldown bis " + runtime.CooldownUntilUtc.ToLocalTime().ToString("HH:mm:ss") + ".";
        }

        if (runtime.State is EventRuntimeState.Initiated or EventRuntimeState.Live or EventRuntimeState.CleanupPending)
        {
            return "Event ist bereits aktiv.";
        }

        if (IsGroupBlocked(runtime))
        {
            return "Eine Event-Gruppe blockiert diesen Event gerade.";
        }

        return string.Empty;
    }

    private void SetState(EventRuntime runtime, EventRuntimeState state, string reason)
    {
        if (runtime.State != state)
        {
            runtime.State = state;
            _log($"{runtime.Definition.Name}: Status -> {state}. {reason}");
            _stateChanged?.Invoke();
        }
        else if (!string.IsNullOrWhiteSpace(reason))
        {
            _log($"{runtime.Definition.Name}: {reason}");
        }
    }

    private async Task ExecuteBlockAsync(ScriptBlock block, IEnumerable<EventCommand> fallbackCommands, EventRuntime runtime, ScumPlayer? player, CancellationToken cancellationToken)
    {
        if (block.Commands.Count > 0 && !block.Enabled)
        {
            _log($"Block deaktiviert: {block.Name}");
            return;
        }

        await ExecuteCommandListAsync(fallbackCommands, runtime, player, cancellationToken);
    }

    private async Task ExecuteSpawnBlocksAsync(EventRuntime runtime, ScumPlayer? player, CancellationToken cancellationToken)
    {
        var blocks = (runtime.Definition.SpawnBlocks ?? new List<SpawnBlock>())
            .Where(b => b.Enabled)
            .Where(b => !string.IsNullOrWhiteSpace(b.Location))
            .ToList();

        if (blocks.Count == 0)
        {
            return;
        }

        foreach (var block in blocks)
        {
            var hasDelayedLoop = GetStartDelay(block) > TimeSpan.Zero || (block.Repeat > 1 && GetRepeatDelay(block) > TimeSpan.Zero);
            if (hasDelayedLoop)
            {
                _log($"{runtime.Definition.Name}: SpawnBlock '{block.Name}' wurde als Hintergrund-Loop geplant.");
                _ = Task.Run(() => RunSpawnBlockLoopAsync(runtime, block, player, cancellationToken), cancellationToken);
                continue;
            }

            await RunSpawnBlockLoopAsync(runtime, block, player, cancellationToken);
        }
    }

    private async Task RunSpawnBlockLoopAsync(EventRuntime runtime, SpawnBlock block, ScumPlayer? player, CancellationToken cancellationToken)
    {
        try
        {
            var repeat = Math.Max(1, block.Repeat);
            var startDelay = GetStartDelay(block);
            if (startDelay > TimeSpan.Zero)
            {
                _log($"{runtime.Definition.Name}: SpawnBlock '{block.Name}' startet in {FormatDelay(startDelay)}.");
                await Task.Delay(startDelay, cancellationToken);
                if (runtime.State == EventRuntimeState.Stopped || runtime.State == EventRuntimeState.Cooldown)
                {
                    _log($"{runtime.Definition.Name}: SpawnBlock '{block.Name}' nach Startdelay gestoppt, Script ist nicht mehr aktiv.");
                    return;
                }
            }

            for (var i = 0; i < repeat; i++)
            {
                if (i > 0 && runtime.State != EventRuntimeState.Live && runtime.State != EventRuntimeState.CleanupPending)
                {
                    _log($"{runtime.Definition.Name}: SpawnBlock '{block.Name}' Loop gestoppt, Script ist nicht mehr live.");
                    return;
                }

                var command = BuildSpawnBlockCommand(block, runtime, player);
                if (string.IsNullOrWhiteSpace(command))
                {
                    _log($"{runtime.Definition.Name}: SpawnBlock '{block.Name}' uebersprungen. Typ ungueltig/nicht unterstuetzt, Asset oder Location fehlen.");
                    return;
                }

                var unresolvedPlaceholders = Regex.Matches(command, @"\{[A-Za-z][A-Za-z0-9_]*\}")
                    .Cast<Match>()
                    .Select(m => m.Value)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (unresolvedPlaceholders.Count > 0)
                {
                    _log("SpawnBlock uebersprungen, Platzhalter nicht aufgeloest (" + string.Join(", ", unresolvedPlaceholders) + "): " + command);
                    return;
                }

                var normalizedCommand = NormalizeCommandBeforeSend(command);
                MarkRuntime(runtime, $"SpawnBlock gesendet: {block.Name} ({i + 1}/{repeat})", normalizedCommand);
                await SendAsync(normalizedCommand, cancellationToken);

                if (block.DelayMs > 0)
                {
                    await Task.Delay(block.DelayMs, cancellationToken);
                }

                var loopDelay = GetRepeatDelay(block);
                if (i < repeat - 1 && loopDelay > TimeSpan.Zero)
                {
                    await Task.Delay(loopDelay, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _log($"{runtime.Definition.Name}: SpawnBlock '{block.Name}' Fehler: {ex.Message}");
            AppLogService.WriteException("ScriptEngine.SpawnBlock", ex);
        }
    }

    private static TimeSpan GetStartDelay(SpawnBlock block)
    {
        if (block.StartDelayMs > 0)
        {
            return TimeSpan.FromMilliseconds(block.StartDelayMs);
        }

        return TimeSpan.FromSeconds(Math.Max(0, block.StartDelaySeconds));
    }

    private static TimeSpan GetRepeatDelay(SpawnBlock block)
    {
        if (block.RepeatEveryMs > 0)
        {
            return TimeSpan.FromMilliseconds(block.RepeatEveryMs);
        }

        return TimeSpan.FromSeconds(Math.Max(0, block.RepeatEverySeconds));
    }

    private static string FormatDelay(TimeSpan delay)
    {
        return delay.TotalSeconds >= 1
            ? delay.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture) + "s"
            : delay.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + "ms";
    }

    private string BuildSpawnBlockCommand(SpawnBlock block, EventRuntime runtime, ScumPlayer? player)
    {
        var type = NormalizeSpawnType(block.Type);
        var location = ReplacePlaceholders(block.Location, runtime, player).Trim();

        if (type == "Custom")
        {
            return BuildCustomSpawnBlockCommand(block, runtime, player, location);
        }

        if (type == "CargoDrop")
        {
            return BuildCargoDropSpawnBlockCommand(block, player, location);
        }

        var asset = ReplaceSpawnBlockScopedPlaceholders(block.Asset, runtime, player, location).Trim();
        var isRandomZombie = type == "RandomZombie" || (type == "Zombie" && IsRandomZombieAsset(asset));
        var commandName = type switch
        {
            "Item" => "#SpawnItem",
            "ArmedNPC" => "#SpawnArmedNPC",
            "RandomZombie" => "#SpawnRandomZombie",
            "Lootpuppet" => "#SpawnZombie",
            "Zombie" when isRandomZombie => "#SpawnRandomZombie",
            "Zombie" => "#SpawnZombie",
            "Vehicle" => "#SpawnVehicle",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(commandName))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(location) ||
            (RequiresSpawnAsset(type) && string.IsNullOrWhiteSpace(asset)))
        {
            return string.Empty;
        }

        var quantity = Math.Max(1, block.Quantity);
        var command = type switch
        {
            "RandomZombie" => $"{commandName} {quantity} Location \"{location}\"",
            "Lootpuppet" => $"{commandName} {LootPuppetAsset} {quantity} Location \"{location}\"",
            "Zombie" when isRandomZombie => $"{commandName} {quantity} Location \"{location}\"",
            "Zombie" => $"{commandName} {asset} {quantity} Location \"{location}\"",
            _ => $"{commandName} {asset} {quantity} Location \"{location}\""
        };
        if (block.DespawnLifetimeSeconds > 0)
        {
            command += " DespawnLifetime " + block.DespawnLifetimeSeconds.ToString(CultureInfo.InvariantCulture);
        }

        var extra = ReplaceSpawnBlockScopedPlaceholders(block.Extra, runtime, player, location).Trim();
        if (!string.IsNullOrWhiteSpace(extra))
        {
            command += " " + extra;
        }

        return block.UseTriggerPlayer ? EnsureExecAsForTriggerPlayer(command, player) : command;
    }

    private string BuildCustomSpawnBlockCommand(SpawnBlock block, EventRuntime runtime, ScumPlayer? player, string location)
    {
        var command = ReplaceSpawnBlockScopedPlaceholders(block.Asset, runtime, player, location).Trim();
        if (string.IsNullOrWhiteSpace(command))
        {
            return string.Empty;
        }

        command = EnsureRconCommandPrefix(command);
        return block.UseTriggerPlayer ? EnsureExecAsForTriggerPlayer(command, player) : command;
    }

    private static string BuildCargoDropSpawnBlockCommand(SpawnBlock block, ScumPlayer? player, string location)
    {
        var worldLocation = FormatWorldEventLocation(location);
        if (string.IsNullOrWhiteSpace(worldLocation))
        {
            return string.Empty;
        }

        var command = "#ScheduleWorldEvent BP_CargoDropEvent " + worldLocation;
        return block.UseTriggerPlayer ? EnsureExecAsForTriggerPlayer(command, player) : command;
    }

    private string ReplaceSpawnBlockScopedPlaceholders(string input, EventRuntime runtime, ScumPlayer? player, string location)
    {
        var normalizedLocation = NormalizeLocationKey(location);
        var worldLocation = FormatWorldEventLocation(normalizedLocation);
        return ReplacePlaceholders(input ?? string.Empty, runtime, player)
            .Replace("{location}", normalizedLocation, StringComparison.OrdinalIgnoreCase)
            .Replace("{spawnLocation}", normalizedLocation, StringComparison.OrdinalIgnoreCase)
            .Replace("{worldLocation}", worldLocation, StringComparison.OrdinalIgnoreCase)
            .Replace("{worldEventLocation}", worldLocation, StringComparison.OrdinalIgnoreCase)
            .Replace("{cargoDropLocation}", worldLocation, StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureRconCommandPrefix(string command)
    {
        var trimmed = command.Trim();
        if (trimmed.StartsWith('#'))
        {
            return trimmed;
        }

        return Regex.IsMatch(trimmed, @"^[A-Za-z]") ? "#" + trimmed : trimmed;
    }

    private static string FormatWorldEventLocation(string location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return string.Empty;
        }

        var coordinatePart = location.Split('|')[0];
        if (TryReadLocationCoordinate(coordinatePart, "X", out var x) &&
            TryReadLocationCoordinate(coordinatePart, "Y", out var y) &&
            TryReadLocationCoordinate(coordinatePart, "Z", out var z))
        {
            return "X=" + x.ToString("0.###", CultureInfo.InvariantCulture) +
                   " Y=" + y.ToString("0.###", CultureInfo.InvariantCulture) +
                   " Z=" + z.ToString("0.###", CultureInfo.InvariantCulture);
        }

        var values = Regex.Matches(location, @"-?\d+(?:[\.,]\d+)?")
            .Cast<Match>()
            .Select(match => match.Value.Replace(',', '.'))
            .Take(3)
            .ToArray();
        if (values.Length == 3 &&
            double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) &&
            double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) &&
            double.TryParse(values[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z))
        {
            return "X=" + x.ToString("0.###", CultureInfo.InvariantCulture) +
                   " Y=" + y.ToString("0.###", CultureInfo.InvariantCulture) +
                   " Z=" + z.ToString("0.###", CultureInfo.InvariantCulture);
        }

        return NormalizeLocationKey(location.Trim().Trim('"'));
    }

    private static bool TryReadLocationCoordinate(string source, string key, out double value)
    {
        value = 0;
        var match = Regex.Match(source, @"\b" + key + @"\s*=\s*(-?\d+(?:[\.,]\d+)?)", RegexOptions.IgnoreCase);
        return match.Success &&
               double.TryParse(match.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string NormalizeSpawnType(string? type)
    {
        var value = (type ?? string.Empty).Trim();
        return value switch
        {
            "ArmedNpc" => "ArmedNPC",
            "armednpc" => "ArmedNPC",
            "Armed NPC" => "ArmedNPC",
            "armed npc" => "ArmedNPC",
            "NPC" => "ArmedNPC",
            "npc" => "ArmedNPC",
            "Puppet" => "Zombie",
            "puppet" => "Zombie",
            "Puppets" => "Zombie",
            "puppets" => "Zombie",
            "RandomZombie" => "RandomZombie",
            "randomZombie" => "RandomZombie",
            "randomzombie" => "RandomZombie",
            "Random Zombie" => "RandomZombie",
            "random zombie" => "RandomZombie",
            "Lootpuppet" => "Lootpuppet",
            "lootpuppet" => "Lootpuppet",
            "LootPuppet" => "Lootpuppet",
            "Loot Puppet" => "Lootpuppet",
            "loot puppet" => "Lootpuppet",
            "BP_Zombie_Civilian_Skinny_Loot" => "Lootpuppet",
            "Item" => "Item",
            "item" => "Item",
            "Zombie" => "Zombie",
            "zombie" => "Zombie",
            "Vehicle" => "Vehicle",
            "vehicle" => "Vehicle",
            "Custom" => "Custom",
            "custom" => "Custom",
            "Command" => "Custom",
            "command" => "Custom",
            "RawCommand" => "Custom",
            "Raw Command" => "Custom",
            "CargoDrop" => "CargoDrop",
            "cargoDrop" => "CargoDrop",
            "cargodrop" => "CargoDrop",
            "Cargo Drop" => "CargoDrop",
            "cargo drop" => "CargoDrop",
            "ScheduleCargoDrop" => "CargoDrop",
            "BP_CargoDropEvent" => "CargoDrop",
            _ => value
        };
    }

    private static bool RequiresSpawnAsset(string type) =>
        type is "Item" or "ArmedNPC" or "Vehicle";

    private static bool IsRandomZombieAsset(string? asset)
    {
        var value = asset?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(value) ||
               value.Equals("Random Zombie", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("RandomZombie", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("Random", StringComparison.OrdinalIgnoreCase);
    }

    private async Task ExecuteCommandListAsync(IEnumerable<EventCommand> commands, EventRuntime runtime, ScumPlayer? player, CancellationToken cancellationToken)
    {
        var values = BuildPlaceholderValues(runtime, player);

        foreach (var eventCommand in commands)
        {
            if (!eventCommand.Enabled)
            {
                if (!string.IsNullOrWhiteSpace(eventCommand.Name))
                {
                    _log("Command deaktiviert: " + eventCommand.Name);
                }
                continue;
            }

            if (string.IsNullOrWhiteSpace(eventCommand.Command)) continue;
            var repeat = Math.Max(1, eventCommand.Repeat);

            for (var i = 0; i < repeat; i++)
            {
                var command = eventCommand.Command;
                foreach (var pair in values)
                {
                    command = command.Replace("{" + pair.Key + "}", pair.Value, StringComparison.OrdinalIgnoreCase);
                }

                var unresolvedPlaceholders = Regex.Matches(command, @"\{[A-Za-z][A-Za-z0-9_]*\}")
                    .Cast<Match>()
                    .Select(m => m.Value)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (unresolvedPlaceholders.Count > 0)
                {
                    _log("Command uebersprungen, Platzhalter nicht aufgeloest (" + string.Join(", ", unresolvedPlaceholders) + "): " + command);
                    continue;
                }

                if (IsSpawnCommand(command))
                {
                    command = EnsureExecAsForTriggerPlayer(command, player);
                }

                var normalizedCommand = NormalizeCommandBeforeSend(command);

                MarkRuntime(runtime, "Command gesendet" + (string.IsNullOrWhiteSpace(eventCommand.Name) ? "" : ": " + eventCommand.Name), normalizedCommand);
                await SendAsync(normalizedCommand, cancellationToken);

                if (eventCommand.DelayMs > 0)
                {
                    await Task.Delay(eventCommand.DelayMs, cancellationToken);
                }
            }
        }
    }

    private static Dictionary<string, string> BuildPlaceholderValues(EventRuntime runtime, ScumPlayer? player)
    {
        var zone = runtime.Definition.EffectiveZone;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["scriptId"] = runtime.Definition.Id ?? "",
            ["scriptName"] = runtime.Definition.Name ?? "",
            ["state"] = runtime.State.ToString(),
            ["playerId"] = player?.UserId ?? "",
            ["playerName"] = player?.DisplayName ?? "",
            ["x"] = player?.Location?.X.ToString("0.###", CultureInfo.InvariantCulture) ?? "",
            ["y"] = player?.Location?.Y.ToString("0.###", CultureInfo.InvariantCulture) ?? "",
            ["z"] = player?.Location?.Z.ToString("0.###", CultureInfo.InvariantCulture) ?? "",
            ["triggerX"] = zone.CenterX.ToString("0.###", CultureInfo.InvariantCulture),
            ["triggerY"] = zone.CenterY.ToString("0.###", CultureInfo.InvariantCulture),
            ["triggerZ"] = zone.CenterZ.ToString("0.###", CultureInfo.InvariantCulture),
            ["triggerRadius"] = zone.Radius.ToString("0.###", CultureInfo.InvariantCulture),
            ["triggerZone"] = FormatLocation(zone.CenterX, zone.CenterY, zone.CenterZ),
            ["buyPrice"] = Math.Max(0, runtime.Definition.BuyPrice).ToString(CultureInfo.InvariantCulture),
            ["buyAlias"] = ResolveBuyEventDisplayName(runtime.Definition),
            ["activationDelayMs"] = Math.Max(0, runtime.Definition.ActivationDelayMs).ToString(CultureInfo.InvariantCulture),
            ["initiatorMessage"] = FirstNonEmpty(runtime.Definition.LocalVariables?.InitiatorMessage, runtime.Definition.Announcement)
        };

        foreach (var location in runtime.Definition.LocalVariables?.LootSpawnLocations ?? new List<ScriptLocationVariable>())
        {
            var key = "loot_" + SanitizePlaceholderName(location.Name);
            if (!values.ContainsKey(key))
            {
                values[key] = location.Location ?? "";
            }
        }

        foreach (var location in runtime.Definition.LocalVariables?.NpcSpawnLocations ?? new List<ScriptLocationVariable>())
        {
            var key = "npc_" + SanitizePlaceholderName(location.Name);
            if (!values.ContainsKey(key))
            {
                values[key] = location.Location ?? "";
            }
        }

        return values;
    }

    private static string FormatLocation(double x, double y, double z) =>
        "[{X=" + x.ToString("0.###", CultureInfo.InvariantCulture) +
        " Y=" + y.ToString("0.###", CultureInfo.InvariantCulture) +
        " Z=" + z.ToString("0.###", CultureInfo.InvariantCulture) +
        "|P=0 Y=0 R=0}]";

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static string SanitizePlaceholderName(string? value)
    {
        var cleaned = Regex.Replace((value ?? string.Empty).Trim(), "[^A-Za-z0-9_]+", "_");
        cleaned = Regex.Replace(cleaned, "_+", "_").Trim('_');
        return string.IsNullOrWhiteSpace(cleaned) ? "value" : cleaned;
    }

    private static string NormalizeCommandBeforeSend(string command)
    {
        return string.IsNullOrWhiteSpace(command) ? command : command.Trim();
    }

    private static string EnsureExecAsForTriggerPlayer(string command, ScumPlayer? player)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return command;
        }

        var trimmed = command.Trim();
        if (Regex.IsMatch(trimmed, @"^#ExecAs\b", RegexOptions.IgnoreCase))
        {
            return trimmed;
        }

        if (string.IsNullOrWhiteSpace(player?.UserId))
        {
            return trimmed;
        }

        // Event-Loot und LootCommandPacks muessen aus Spielersicht gespawnt werden,
        // damit SCUM die Items/NPCs/Objekte zuverlaessig am Zielort erzeugt.
        return "#ExecAs " + player.UserId.Trim() + " " + trimmed;
    }

    private static bool IsSpawnCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        var trimmed = command.Trim();
        return Regex.IsMatch(
                   trimmed,
                   @"^#(SpawnItem|SpawnInventory|SpawnInventoryFullOf|SpawnArmedNPC|SpawnRandomZombie|SpawnZombie|SpawnRazor|SpawnVehicle|ScheduleCargoDrop)\b",
                   RegexOptions.IgnoreCase) ||
               Regex.IsMatch(trimmed, @"^#ScheduleWorldEvent\s+BP_CargoDropEvent\b", RegexOptions.IgnoreCase);
    }

    private void MarkRuntime(EventRuntime runtime, string action, string rawCommand)
    {
        runtime.LastAction = action;
        runtime.LastRawCommand = rawCommand;
        runtime.LastUpdatedUtc = DateTime.UtcNow;
        _stateChanged?.Invoke();
    }

    private async Task SendAsync(string command, CancellationToken cancellationToken)
    {
        _log("> " + command);
        var response = await _rcon.SendCommandAsync(command, cancellationToken);
        if (!string.IsNullOrWhiteSpace(response)) _log(response);
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}

public sealed class EventRuntime
{
    public EventRuntime(EventDefinition definition)
    {
        Definition = definition;
        NextRandomizerUtc = DateTime.MinValue;
    }

    public EventDefinition Definition { get; }
    public EventRuntimeState State { get; set; } = EventRuntimeState.Stopped;
    public DateTime LastInitiatedUtc { get; set; } = DateTime.MinValue;
    public DateTime LastInitiatorRepeatUtc { get; set; } = DateTime.MinValue;
    public DateTime LastLiveUtc { get; set; } = DateTime.MinValue;
    public DateTime LastOccupiedUtc { get; set; } = DateTime.MinValue;
    public DateTime? EmptySinceUtc { get; set; }
    public DateTime CooldownUntilUtc { get; set; } = DateTime.MinValue;
    public DateTime NextRandomizerUtc { get; set; } = DateTime.MinValue;

    // Live-Diagnose fuer die Script-Uebersicht.
    // Diese Werte werden von EventEngine.MarkRuntime(...) und den Loot-Routinen aktualisiert.
    public string LastAction { get; set; } = string.Empty;
    public string LastRawCommand { get; set; } = string.Empty;
    public string LastLootSummary { get; set; } = string.Empty;
    public int SpawnedLootCount { get; set; }
    public DateTime LastUpdatedUtc { get; set; } = DateTime.MinValue;

    public override string ToString() => $"{Definition.Name} - {State}";
}

public sealed record BuyableEventSummary(
    string Id,
    string Name,
    string DisplayName,
    int Price,
    EventRuntimeState State,
    DateTime CooldownUntilUtc);

public sealed record BuyEventActivationResult(bool Success, string Message, string EventName)
{
    public static BuyEventActivationResult Ok(string eventName) => new(true, string.Empty, eventName);
    public static BuyEventActivationResult Fail(string message) => new(false, message, string.Empty);
}
