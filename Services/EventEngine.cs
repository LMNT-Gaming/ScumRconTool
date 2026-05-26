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
    private readonly SourceRconClient _rcon;
    private readonly Action<string> _log;
    private readonly Action? _stateChanged;
    private readonly List<EventRuntime> _events;
    private readonly Random _random = new();
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private DateTime _lastRandomizerLimitLogUtc = DateTime.MinValue;

    public bool IsRunning => _loopTask is not null && !_loopTask.IsCompleted;

    public EventEngine(SourceRconClient rcon, IEnumerable<EventDefinition> definitions, Action<string> log, Action? stateChanged = null)
    {
        _rcon = rcon;
        _log = log;
        _stateChanged = stateChanged;
        _events = definitions.Select(x => new EventRuntime(x)).ToList();
    }

    public IReadOnlyList<EventRuntime> Events => _events;

    public void Start(int pollSeconds)
    {
        if (IsRunning) return;
        if (pollSeconds < 2) pollSeconds = 2;

        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => LoopAsync(TimeSpan.FromSeconds(pollSeconds), _cts.Token));
        _log($"Script Engine gestartet. Poll: {pollSeconds}s");
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
            catch (Exception ex)
            {
                _log("Script Engine Fehler: " + ex.Message);
                throw;
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

        foreach (var runtime in _events.Where(e => e.Definition.Enabled))
        {
            if (runtime.State == EventRuntimeState.Cooldown && runtime.CooldownUntilUtc <= now)
            {
                SetState(runtime, IsSilentZone(runtime) ? EventRuntimeState.Initiated : EventRuntimeState.Stopped, "Cooldown beendet.");
            }

            if (IsSilentZone(runtime) && runtime.State == EventRuntimeState.Stopped)
            {
                SetState(runtime, EventRuntimeState.Initiated, "Silent-Script ist scharf und wartet auf Spieler in der Aktivierzone.");
            }
        }

        await RunRandomizerAsync(players, now, cancellationToken);

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
                    await GoLiveAsync(runtime, triggerPlayer, cancellationToken);
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
        await ExecuteBlockAsync(runtime.Definition.PreLiveCleanupBlock, runtime.Definition.PreLiveCleanupBlock.Commands, runtime, triggerPlayer, cancellationToken);
        await ExecuteBlockAsync(runtime.Definition.LiveBlock, runtime.Definition.GetLiveCommands(), runtime, triggerPlayer, cancellationToken);
        await ExecuteRandomLootPackAsync(runtime, triggerPlayer, cancellationToken);
        await ExecuteRandomLootCommandPackAsync(runtime, triggerPlayer, cancellationToken);
        runtime.LastLiveUtc = DateTime.UtcNow;
        SetState(runtime, EventRuntimeState.Live, "live.");
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
        var packs = runtime.Definition.LootCommandPacks
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

        var normalizedCommand = NormalizeCommandBeforeSend(command);
        _log($"{runtime.Definition.Name}: LootCommandPack gewaehlt: {selected.Name}");
        await SendAsync(normalizedCommand, cancellationToken);

        if (selected.DelayMs > 0)
        {
            await Task.Delay(selected.DelayMs, cancellationToken);
        }
    }

    private async Task ExecuteRandomLootPackAsync(EventRuntime runtime, ScumPlayer? player, CancellationToken cancellationToken)
    {
        var packs = runtime.Definition.LootPacks
            .Where(p => p.Enabled && p.Items.Count > 0 && !string.IsNullOrWhiteSpace(p.Location))
            .ToList();

        if (packs.Count == 0)
        {
            return;
        }

        var totalWeight = packs.Sum(p => Math.Max(1, p.Weight));
        var roll = _random.Next(1, totalWeight + 1);
        LootPack selected = packs[0];
        foreach (var pack in packs)
        {
            roll -= Math.Max(1, pack.Weight);
            if (roll <= 0)
            {
                selected = pack;
                break;
            }
        }

        var location = ReplacePlaceholders(selected.Location, runtime, player);
        _log($"{runtime.Definition.Name}: LootPack gewaehlt: {selected.Name}");

        foreach (var item in selected.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Item))
            {
                continue;
            }

            var quantity = Math.Max(1, item.Quantity);
            var command = $"#SpawnItem {item.Item.Trim()} {quantity} Location \"{location}\"";
            await SendAsync(command, cancellationToken);

            if (item.DelayMs > 0)
            {
                await Task.Delay(item.DelayMs, cancellationToken);
            }
        }
    }

    private static string ReplacePlaceholders(string input, EventRuntime runtime, ScumPlayer? player)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["scriptId"] = runtime.Definition.Id ?? "",
            ["scriptName"] = runtime.Definition.Name ?? "",
            ["state"] = runtime.State.ToString(),
            ["playerId"] = player?.UserId ?? "",
            ["playerName"] = player?.DisplayName ?? "",
            ["x"] = player?.Location?.X.ToString("0.###", CultureInfo.InvariantCulture) ?? "",
            ["y"] = player?.Location?.Y.ToString("0.###", CultureInfo.InvariantCulture) ?? "",
            ["z"] = player?.Location?.Z.ToString("0.###", CultureInfo.InvariantCulture) ?? ""
        };

        var result = input ?? string.Empty;
        foreach (var pair in values)
        {
            result = result.Replace("{" + pair.Key + "}", pair.Value, StringComparison.OrdinalIgnoreCase);
        }
        return result;
    }

    private static bool IsRandomAnnouncedZone(EventRuntime runtime) =>
        runtime.Definition.Mode.Equals("RandomAnnouncedZone", StringComparison.OrdinalIgnoreCase) ||
        runtime.Definition.Mode.Equals("A", StringComparison.OrdinalIgnoreCase) ||
        runtime.Definition.Mode.Equals("AnnouncedThenZone", StringComparison.OrdinalIgnoreCase);

    private static bool IsSilentZone(EventRuntime runtime) =>
        runtime.Definition.Mode.Equals("SilentZone", StringComparison.OrdinalIgnoreCase) ||
        runtime.Definition.Mode.Equals("B", StringComparison.OrdinalIgnoreCase) ||
        runtime.Definition.Mode.Equals("Silent", StringComparison.OrdinalIgnoreCase);

    private static bool IsDirectLive(EventRuntime runtime) =>
        runtime.Definition.Mode.Equals("DirectLive", StringComparison.OrdinalIgnoreCase);

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

    private async Task ExecuteCommandListAsync(IEnumerable<EventCommand> commands, EventRuntime runtime, ScumPlayer? player, CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["scriptId"] = runtime.Definition.Id ?? "",
            ["scriptName"] = runtime.Definition.Name ?? "",
            ["state"] = runtime.State.ToString(),
            ["playerId"] = player?.UserId ?? "",
            ["playerName"] = player?.DisplayName ?? "",
            ["x"] = player?.Location?.X.ToString("0.###", CultureInfo.InvariantCulture) ?? "",
            ["y"] = player?.Location?.Y.ToString("0.###", CultureInfo.InvariantCulture) ?? "",
            ["z"] = player?.Location?.Z.ToString("0.###", CultureInfo.InvariantCulture) ?? ""
        };

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

                var normalizedCommand = NormalizeCommandBeforeSend(command);
                if (!string.Equals(command, normalizedCommand, StringComparison.Ordinal))
                {
                    _log("Command normalisiert: #ExecAs vor #SpawnItem entfernt, damit kein Spieler-Executor sichtbar genutzt wird.");
                }

                await SendAsync(normalizedCommand, cancellationToken);

                if (eventCommand.DelayMs > 0)
                {
                    await Task.Delay(eventCommand.DelayMs, cancellationToken);
                }
            }
        }
    }

    private static string NormalizeCommandBeforeSend(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return command;
        }

        // Items sollen bewusst NICHT ueber #ExecAs laufen: Spieler sehen sonst ggf. die Admin-Aktion.
        // Beispiel:
        // #ExecAs 7656... #SpawnItem Weapon_SKS 1 Location "[...]"
        // wird zu:
        // #SpawnItem Weapon_SKS 1 Location "[...]"
        var execAsSpawnItem = Regex.Match(command, @"^\s*#ExecAs\s+\S+\s+(#SpawnItem\b.*)$", RegexOptions.IgnoreCase);
        if (execAsSpawnItem.Success)
        {
            return execAsSpawnItem.Groups[1].Value.Trim();
        }

        return command;
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

    public override string ToString() => $"{Definition.Name} - {State}";
}
