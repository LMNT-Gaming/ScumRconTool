using Discord;
using Discord.WebSocket;
using ScumRconTool.Models;

namespace ScumRconTool.Services;

public sealed class DiscordBridgeService : IAsyncDisposable
{
    private readonly Action<string> _log;
    private readonly Action<bool>? _connectionChanged;
    private DiscordSocketClient? _client;
    private Func<string, Task>? _sendToGameAsync;
    private ulong _gameBridgeChannelId;
    private string _messageType = "Cyan";
    private TaskCompletionSource<bool>? _readyTcs;
    private readonly HashSet<ulong> _legacyStatusCleanupDone = new();

    public DiscordBridgeService(Action<string> log, Action<bool>? connectionChanged = null)
    {
        _log = log;
        _connectionChanged = connectionChanged;
    }

    public bool IsReady => _client?.ConnectionState == ConnectionState.Connected && _client.LoginState == LoginState.LoggedIn && _client.CurrentUser is not null;
    public bool IsStarted => _client is not null && _client.LoginState != LoginState.LoggedOut;

    public async Task StartAsync(
        string token,
        ulong gameBridgeChannelId,
        Func<string, Task> sendToGameAsync,
        string messageType = "Cyan",
        CancellationToken cancellationToken = default)
    {
        var cleanToken = NormalizeToken(token);
        if (string.IsNullOrWhiteSpace(cleanToken)) throw new ArgumentException("Discord Bot Token fehlt.", nameof(token));

        await DisposeAsync();

        _gameBridgeChannelId = gameBridgeChannelId;
        _sendToGameAsync = sendToGameAsync;
        _messageType = string.IsNullOrWhiteSpace(messageType) ? "Cyan" : messageType.Trim();
        _readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent,
            AlwaysDownloadUsers = false,
            LogLevel = LogSeverity.Info,
            DefaultRetryMode = RetryMode.AlwaysRetry
        });

        _client.Log += message =>
        {
            var text = !string.IsNullOrWhiteSpace(message.Message) ? message.Message : message.Exception?.Message;
            if (!string.IsNullOrWhiteSpace(text)) _log("Discord: " + text);
            if (message.Exception is not null) AppLogService.WriteException("Discord", message.Exception);
            return Task.CompletedTask;
        };

        _client.Connected += () =>
        {
            _log("Discord: Gateway verbunden.");
            _connectionChanged?.Invoke(true);
            return Task.CompletedTask;
        };

        _client.Disconnected += exception =>
        {
            if (exception is null)
            {
                _log("Discord: Gateway getrennt.");
            }
            else
            {
                _log("Discord: Gateway getrennt: " + exception.Message);
                AppLogService.WriteException("Discord.Disconnected", exception);
            }

            _connectionChanged?.Invoke(false);
            return Task.CompletedTask;
        };

        _client.LoggedIn += () =>
        {
            _log("Discord: Eingeloggt.");
            return Task.CompletedTask;
        };

        _client.LoggedOut += () =>
        {
            _log("Discord: Ausgeloggt.");
            _connectionChanged?.Invoke(false);
            return Task.CompletedTask;
        };

        _client.Ready += async () =>
        {
            var user = _client.CurrentUser?.Username ?? "unbekannt";
            _log("Discord: Bot ist bereit: " + user);
            if (_client is not null) await _client.SetStatusAsync(UserStatus.Online);
            _connectionChanged?.Invoke(true);
            _readyTcs?.TrySetResult(true);
        };

        _client.MessageReceived += OnMessageReceivedAsync;

        _log("Discord: Login wird gestartet...");
        await _client.LoginAsync(TokenType.Bot, cleanToken);

        _log("Discord: Gateway wird gestartet...");
        await _client.StartAsync();

        // Discord.Net verbindet asynchron. Vorher wurde hier hart abgebrochen und WPF zeigte nur
        // "A task was canceled". Jetzt warten wir kurz auf Ready, lassen den Bot danach aber weiterlaufen
        // und loggen einen brauchbaren Hinweis fuer Debugging.
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            await _readyTcs.Task.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _log("Discord: Ready kam nach 45 Sekunden noch nicht. Der Bot laeuft weiter und versucht im Hintergrund zu verbinden. Pruefe Token, Bot-Einladung, Internet/Firewall und den Message Content Intent im Discord Developer Portal.");
        }
    }

    public async Task SetStatusAsync(string text, ActivityType type = ActivityType.Playing)
    {
        if (_client is null || !IsReady) throw new InvalidOperationException("Discord Bot ist nicht verbunden.");
        text = string.IsNullOrWhiteSpace(text) ? "SCUM Server" : text.Trim();
        if (text.Length > 128) text = text[..128];
        await _client.SetGameAsync(text, type: type);
        await _client.SetStatusAsync(UserStatus.Online);
    }



    public async Task SendChatEmbedAsync(ulong channelId, ChatLogMessage message)
    {
        if (_client is null || !IsReady) throw new InvalidOperationException("Discord Bot ist nicht verbunden.");
        if (channelId == 0) throw new InvalidOperationException("Discord Chatlog Channel-ID fehlt.");
        if (_client.GetChannel(channelId) is not IMessageChannel channel)
        {
            throw new InvalidOperationException("Discord Chatlog Channel wurde nicht gefunden. Ist der Bot auf dem Server und hat Zugriff auf den Channel?");
        }

        var author = string.IsNullOrWhiteSpace(message.PlayerName) ? "Unbekannt" : message.PlayerName;
        var embed = new EmbedBuilder()
            .WithTitle("Ingame Chat")
            .WithColor(new Color(211, 21, 42))
            .AddField("Name", author, true)
            .AddField("Channel", string.IsNullOrWhiteSpace(message.Channel) ? "Global" : message.Channel, true)
            .AddField("Nachricht", Truncate(message.Message, 1000), false)
            .WithCurrentTimestamp();

        if (!string.IsNullOrWhiteSpace(message.SteamId)) embed.WithFooter("SteamID: " + message.SteamId);
        await channel.SendMessageAsync(embed: embed.Build());
    }


    public async Task SendVehicleDestructionEmbedAsync(ulong channelId, VehicleDestructionLogEntry entry)
    {
        if (_client is null || !IsReady) throw new InvalidOperationException("Discord Bot ist nicht verbunden.");
        if (channelId == 0) throw new InvalidOperationException("Discord Vehicle-Log Channel-ID fehlt.");
        if (_client.GetChannel(channelId) is not IMessageChannel channel)
        {
            throw new InvalidOperationException("Discord Vehicle-Log Channel wurde nicht gefunden. Ist der Bot auf dem Server und hat Zugriff auf den Channel?");
        }

        var (title, description, color) = BuildVehicleEventPresentation(entry.Action);
        var owner = entry.HasOwner
            ? $"{CleanDiscordName(entry.OwnerName)} ({entry.OwnerSteamId})".Trim()
            : "N/A";
        if (string.IsNullOrWhiteSpace(entry.OwnerName) && !string.IsNullOrWhiteSpace(entry.OwnerSteamId)) owner = entry.OwnerSteamId;

        var location = entry.X.HasValue && entry.Y.HasValue && entry.Z.HasValue
            ? $"X={entry.X.Value:0.###} Y={entry.Y.Value:0.###} Z={entry.Z.Value:0.###}"
            : "Unbekannt";

        var embed = new EmbedBuilder()
            .WithTitle(title)
            .WithDescription(description)
            .WithColor(color)
            .AddField("Fahrzeug", CleanDiscordName(entry.VehicleIconName), true)
            .AddField("VehicleId", string.IsNullOrWhiteSpace(entry.VehicleId) ? "Unbekannt" : entry.VehicleId, true)
            .AddField("Owner", owner, false)
            .AddField("Location", $"`{location}`", false)
            .WithImageUrl(entry.IconUrl)
            .WithCurrentTimestamp();

        if (entry.Timestamp.HasValue) embed.AddField("Zeit", entry.Timestamp.Value.ToString("yyyy-MM-dd HH:mm:ss"), true);
        embed.WithFooter("SCUM vehicle_destruction log");

        await channel.SendMessageAsync(embed: embed.Build());
    }

    private static (string Title, string Description, Color Color) BuildVehicleEventPresentation(string action)
    {
        return action.Trim() switch
        {
            "Destroyed" => ("Fahrzeug zerstoert", "Ein Fahrzeug wurde zerstoert.", new Color(211, 21, 42)),
            "Disappeared" => ("Fahrzeug verschwunden", "Ein Fahrzeug ist despawned oder verschwunden.", new Color(155, 89, 182)),
            "VehicleInactiveTimerReached" => ("Fahrzeug inaktiv", "Der Inaktivitaets-Timer des Fahrzeugs wurde erreicht.", new Color(243, 156, 18)),
            "ForbiddenZoneTimerExpired" => ("Fahrzeug in verbotener Zone entfernt", "Der Forbidden-Zone-Timer ist abgelaufen.", new Color(230, 126, 34)),
            "Failed to spawn" => ("Fahrzeug-Spawn fehlgeschlagen", "Ein Fahrzeug konnte nicht gespawnt werden.", new Color(127, 140, 141)),
            var other when !string.IsNullOrWhiteSpace(other) => ($"Fahrzeug-Event: {CleanDiscordName(other)}", "Ein Vehicle-Destruction-Log-Event wurde erkannt.", new Color(52, 152, 219)),
            _ => ("Fahrzeug-Event", "Ein Vehicle-Destruction-Log-Event wurde erkannt.", new Color(52, 152, 219))
        };
    }


    public async Task SendOrUpdateServerStatusAsync(
        ulong channelId,
        string title,
        string serverName,
        string serverAddress,
        IReadOnlyCollection<ScumPlayer> players,
        int maxPlayers,
        GgconWeatherResponse? weather,
        IEnumerable<EventRuntime> runtimes)
    {
        if (_client is null || !IsReady) throw new InvalidOperationException("Discord Bot ist nicht verbunden.");
        if (channelId == 0) throw new InvalidOperationException("Discord Serverstatus Channel-ID fehlt.");
        if (_client.GetChannel(channelId) is not IMessageChannel channel)
        {
            throw new InvalidOperationException("Discord Serverstatus Channel wurde nicht gefunden. Ist der Bot auf dem Server und hat Zugriff auf den Channel?");
        }

        await CleanupLegacyStatusMessagesOnceAsync(channelId, channel);

        title = string.IsNullOrWhiteSpace(title) ? "SCUM Serverstatus" : CleanDiscordName(title);
        serverName = string.IsNullOrWhiteSpace(serverName) ? "SCUM Server" : CleanDiscordName(serverName);
        serverAddress = string.IsNullOrWhiteSpace(serverAddress) ? "Nicht gesetzt" : serverAddress.Trim();
        maxPlayers = maxPlayers > 0 ? maxPlayers : 64;

        var onlineText = $"**{players.Count}/{maxPlayers}** online";
        var playerText = BuildPlayerPreview(players);
        var weatherText = BuildWeatherStatusText(weather);
        var eventText = BuildActiveEventStatusText(runtimes);

        var embed = new EmbedBuilder()
            .WithTitle(title)
            .WithColor(new Color(46, 204, 113))
            .AddField("Server", $"**{serverName}**\n`{serverAddress}`", false)
            .AddField("Spieler", onlineText + "\n" + playerText, false)
            .AddField("Wetter / Zeit", weatherText, false)
            .AddField("Aktive Events", eventText, false)
            .WithFooter("Automatisch aktualisiert")
            .WithCurrentTimestamp()
            .Build();

        var ownMessage = await FindOwnMessageByTitleAsync(channel, title);
        if (ownMessage is not null)
        {
            await ownMessage.ModifyAsync(x =>
            {
                x.Content = string.Empty;
                x.Embed = embed;
            });
            return;
        }

        await channel.SendMessageAsync(embed: embed);
    }

    private static string BuildPlayerPreview(IReadOnlyCollection<ScumPlayer> players)
    {
        if (players.Count == 0)
        {
            return "_Keine Spieler online._";
        }

        var names = players
            .Select(x => CleanDiscordName(x.DisplayName))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Take(20)
            .ToList();

        var text = string.Join(", ", names);
        if (players.Count > names.Count)
        {
            text += $" und {players.Count - names.Count} weitere";
        }

        return text.Length <= 900 ? text : text[..900] + "...";
    }

    private static string BuildWeatherStatusText(GgconWeatherResponse? weather)
    {
        if (weather is null)
        {
            return "_Keine Wetterdaten verfuegbar._";
        }

        var air = GgconWeatherResponse.FormatTemperatureValue(weather.AirTemperature);
        var water = GgconWeatherResponse.FormatTemperatureValue(weather.WaterTemperature);
        var time = weather.FormatIngameTime();
        var score = weather.GetWeatherScore().ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        var icon = weather.GetWeatherIcon();

        return $"{icon} Wetterwert `{score}`\n🌡️ Luft: **{air}°C** | 🏊 Wasser: **{water}°C** | ⌚ Ingame: **{time}**";
    }

    private static string BuildActiveEventStatusText(IEnumerable<EventRuntime> runtimes)
    {
        var active = runtimes
            .Where(x => x.Definition.Enabled)
            .Where(IsRandomRuntime)
            .Where(x => x.State == EventRuntimeState.Initiated || x.State == EventRuntimeState.Live || x.State == EventRuntimeState.CleanupPending)
            .OrderByDescending(x => x.State == EventRuntimeState.Live)
            .ThenBy(x => x.Definition.Name, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .Select(x =>
            {
                var state = x.State switch
                {
                    EventRuntimeState.Initiated => "Initialisiert",
                    EventRuntimeState.Live => "Live",
                    EventRuntimeState.CleanupPending => "Cleanup",
                    _ => x.State.ToString()
                };
                return $"`{state}` {CleanDiscordName(x.Definition.Name)}";
            })
            .ToList();

        return active.Count == 0
            ? "_Keine aktiven Events._"
            : string.Join("\n", active);
    }




    public async Task SendOrUpdateWeeklyTaskAsync(ulong channelId, WeeklyCommunityTaskProgress progress)
    {
        if (_client is null || !IsReady) throw new InvalidOperationException("Discord Bot ist nicht verbunden.");
        if (channelId == 0) throw new InvalidOperationException("Discord Weekly Task Channel-ID fehlt.");
        if (_client.GetChannel(channelId) is not IMessageChannel channel)
        {
            throw new InvalidOperationException("Discord Weekly Task Channel wurde nicht gefunden. Ist der Bot auf dem Server und hat Zugriff auf den Channel?");
        }

        var definition = progress.Definition;
        var progressLine = $"{progress.Progress:N0}/{Math.Max(1, definition.Target):N0} ({progress.Percent:0.0}%)";
        var bar = BuildProgressBar(progress.Percent);
        var kind = WeeklyCommunityTaskService.GetTaskKind(definition);
        var title = string.IsNullOrWhiteSpace(definition.Title) ? kind + " Community Aufgabe" : CleanDiscordName(definition.Title);
        var discordTitle = kind + " Community Aufgabe: " + title;
        var description = string.IsNullOrWhiteSpace(definition.Description) ? "Community Aufgabe" : definition.Description.Trim();
        var reward = string.IsNullOrWhiteSpace(definition.RewardText) ? "Reward wird manuell gesetzt." : definition.RewardText.Trim();
        var completedText = string.IsNullOrWhiteSpace(definition.CompletedText) ? "Ziel erreicht!" : definition.CompletedText.Trim();

        var squadText = BuildSquadContributionText(progress);

        var embedBuilder = new EmbedBuilder()
            .WithTitle(discordTitle)
            .WithColor(progress.IsCompleted ? new Color(46, 204, 113) : new Color(241, 196, 15))
            .WithDescription(description)
            .AddField("Fortschritt", progressLine + "\n" + bar, false)
            .AddField("Offen", progress.IsCompleted ? "0" : progress.Remaining.ToString("N0"), true)
            .AddField("Reward", reward, true)
            .AddField("Laufzeit", BuildChallengeRuntimeText(progress), false);

        embedBuilder.AddField("Beitrag pro Squad", squadText, false);

        var embed = embedBuilder
            .AddField("Status", progress.IsCompleted ? completedText : "Aktiv", false)
            .WithFooter($"Typ: {kind} | Stat: {definition.StatColumn} | Startwert: {progress.Baseline.BaselineValue:N0}")
            .WithTimestamp(progress.UpdatedUtc)
            .Build();

        var ownMessage = await FindOwnMessageByTitleAsync(channel, discordTitle);
        if (ownMessage is not null)
        {
            await ownMessage.ModifyAsync(x =>
            {
                x.Content = string.Empty;
                x.Embed = embed;
            });
            return;
        }

        await channel.SendMessageAsync(embed: embed);
    }

    private static string BuildChallengeRuntimeText(WeeklyCommunityTaskProgress progress)
    {
        var configuredStartUtc = WeeklyCommunityTaskService.GetTaskStartUtc(progress.Definition);
        var startUtc = configuredStartUtc ?? (progress.Baseline.CreatedUtc == default ? progress.UpdatedUtc : progress.Baseline.CreatedUtc);
        var endUtc = WeeklyCommunityTaskService.GetTaskEndUtc(progress.Definition, startUtc);
        if (endUtc is null)
        {
            return "Keine feste Laufzeit konfiguriert.";
        }

        var nowUtc = DateTime.UtcNow;
        var remaining = endUtc.Value - nowUtc;
        var unixEnd = new DateTimeOffset(DateTime.SpecifyKind(endUtc.Value, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var unixStart = new DateTimeOffset(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();

        if (progress.IsCompleted)
        {
            return $"Abgeschlossen. Lief seit <t:{unixStart}:f> bis <t:{unixEnd}:f>.";
        }

        if (remaining <= TimeSpan.Zero)
        {
            return $"Abgelaufen seit <t:{unixEnd}:R>. Ende war <t:{unixEnd}:f>.";
        }

        return $"Endet <t:{unixEnd}:R> (<t:{unixEnd}:f>)\nVerbleibend: {FormatDuration(remaining)}";
    }

    private static string FormatDuration(TimeSpan value)
    {
        if (value.TotalDays >= 1)
        {
            return $"{(int)value.TotalDays}d {value.Hours}h {value.Minutes}m";
        }

        if (value.TotalHours >= 1)
        {
            return $"{(int)value.TotalHours}h {value.Minutes}m";
        }

        return $"{Math.Max(0, value.Minutes)}m";
    }

    private static string BuildSquadContributionText(WeeklyCommunityTaskProgress progress)
    {
        if (progress.SquadProgress.Count == 0)
        {
            return "Keine Squads in der DB gefunden oder keine Spieler sind einem Squad zugeordnet.";
        }

        var qualifiedSquads = progress.SquadProgress
            .Where(x => x.IsSuccessfulParticipant)
            .OrderByDescending(x => x.Progress)
            .ThenBy(x => x.SquadName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var minimumLine = $"Mindestbeitrag fuer erfolgreiche Teilnahme: **{progress.MinimumParticipationValue:N0}** ({progress.MinimumParticipationPercent:0.##}% vom Ziel, abgerundet).";

        if (qualifiedSquads.Count == 0)
        {
            var bestSquad = progress.SquadProgress
                .OrderByDescending(x => x.Progress)
                .FirstOrDefault();

            if (bestSquad is null || bestSquad.Progress <= 0)
            {
                return minimumLine + "\nNoch kein Squad hat seit dem gespeicherten Startwert beigetragen.";
            }

            return minimumLine + $"\nNoch kein Squad hat die Mindestbeteiligung erreicht. Bester Stand: **{CleanDiscordName(bestSquad.SquadName)}** mit {bestSquad.Progress:N0}.";
        }

        var lines = qualifiedSquads
            .Take(10)
            .Select((squad, index) => $"{index + 1}. **{CleanDiscordName(squad.SquadName)}**: {squad.Progress:N0}")
            .ToList();

        lines.Insert(0, minimumLine);

        if (qualifiedSquads.Count > 10)
        {
            var rest = qualifiedSquads.Skip(10).Sum(x => x.Progress);
            lines.Add($"Weitere erfolgreiche Squads: {rest:N0}");
        }

        var text = string.Join("\n", lines);
        return text.Length <= 1024 ? text : text[..1020] + "...";
    }

    private Task<IUserMessage?> FindOwnMessageByTitlePrefixAsync(IMessageChannel channel, string titlePrefix)
    {
        return FindOwnMessageAsync(channel, e => e.Title is not null && e.Title.StartsWith(titlePrefix, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildProgressBar(double percent)
    {
        const int width = 20;
        var filled = Math.Clamp((int)Math.Round(percent / 100.0 * width), 0, width);
        return "`[" + new string('#', filled) + new string('-', width - filled) + "]`";
    }

    private static bool IsRandomRuntime(EventRuntime runtime) =>
        runtime.Definition.IncludeInRandomizer &&
        (runtime.Definition.Mode.Equals("RandomAnnouncedZone", StringComparison.OrdinalIgnoreCase) ||
         runtime.Definition.Mode.Equals("A", StringComparison.OrdinalIgnoreCase) ||
         runtime.Definition.Mode.Equals("AnnouncedThenZone", StringComparison.OrdinalIgnoreCase));

    private async Task CleanupLegacyStatusMessagesOnceAsync(ulong channelId, IMessageChannel channel)
    {
        if (!_legacyStatusCleanupDone.Add(channelId))
        {
            return;
        }

        foreach (var legacyTitle in new[] { "Aktuell verbundene Spieler", "Aktive Random Events" })
        {
            var message = await FindOwnMessageByTitleAsync(channel, legacyTitle);
            if (message is null)
            {
                continue;
            }

            try
            {
                await message.DeleteAsync();
                _log("Discord: alte separate Status-Nachricht entfernt: " + legacyTitle);
            }
            catch (Exception ex)
            {
                _log("Discord: alte separate Status-Nachricht konnte nicht entfernt werden: " + legacyTitle + " - " + ex.Message);
                AppLogService.WriteException("Discord.LegacyStatusCleanup", ex);
            }
        }
    }


    private Task<IUserMessage?> FindOwnMessageByTitleAsync(IMessageChannel channel, string title)
    {
        return FindOwnMessageAsync(channel, e => string.Equals(e.Title, title, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IUserMessage?> FindOwnMessageAsync(IMessageChannel channel, Func<IEmbed, bool> predicate)
    {
        if (_client?.CurrentUser is null) return null;

        try
        {
            var messages = await channel.GetMessagesAsync(30).FlattenAsync();
            return messages
                .OfType<IUserMessage>()
                .Where(x => x.Author.Id == _client.CurrentUser.Id)
                .FirstOrDefault(x => x.Embeds.Any(predicate));
        }
        catch (Exception ex)
        {
            _log("Discord: vorhandene Nachricht konnte nicht gesucht werden: " + ex.Message);
            AppLogService.WriteException("Discord.FindOwnMessage", ex);
            return null;
        }
    }

    private static string CleanDiscordName(string value)
    {
        value = (value ?? string.Empty).Trim();
        if (value.Length > 64) value = value[..64];
        return value
            .Replace("`", "'", StringComparison.Ordinal)
            .Replace("@", "@\u200b", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
    }

    private async Task OnMessageReceivedAsync(SocketMessage socketMessage)
    {
        try
        {
            if (socketMessage is not SocketUserMessage message) return;
            if (message.Author.IsBot || message.Author.IsWebhook) return;
            if (_gameBridgeChannelId == 0 || message.Channel.Id != _gameBridgeChannelId) return;
            if (_sendToGameAsync is null) return;

            var raw = message.Content ?? string.Empty;
            if (!TryBuildSafeGameMessage(message.Author.Username, raw, out var safeText, out var reason))
            {
                _log($"Discord->Game blockiert: {reason}");
                return;
            }

            var command = CommandRegistry.Broadcast(_messageType, safeText);
            await _sendToGameAsync(command);
            _log("Discord->Game gesendet: " + safeText);
        }
        catch (Exception ex)
        {
            _log("Discord->Game Fehler: " + ex.Message);
            AppLogService.WriteException("Discord.MessageReceived", ex);
        }
    }

    public static bool TryBuildSafeGameMessage(string author, string content, out string safeText, out string reason)
    {
        safeText = string.Empty;
        reason = string.Empty;

        var text = (content ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            reason = "Leere Nachricht.";
            return false;
        }

        if (text.Contains('\r') || text.Contains('\n'))
        {
            reason = "Mehrzeilige Nachrichten sind nicht erlaubt.";
            return false;
        }

        var first = text.TrimStart()[0];
        if (first is '#' or '/' or '!' or '.' or ';' or '$')
        {
            reason = "Nachricht beginnt wie ein Command.";
            return false;
        }

        text = text.Replace("`", "'").Replace("@everyone", "everyone").Replace("@here", "here");
        author = string.IsNullOrWhiteSpace(author) ? "Discord" : author.Trim();
        author = new string(author.Where(ch => !char.IsControl(ch)).ToArray());
        text = new string(text.Where(ch => !char.IsControl(ch)).ToArray());

        safeText = $"[DC] {author}: {text}";
        if (safeText.Length > 220) safeText = safeText[..220];
        return true;
    }

    public static string FormatStatus(string template, int players, int maxPlayers, DateTime updated)
    {
        if (string.IsNullOrWhiteSpace(template)) template = "SCUM {players}/{max} Spieler online";
        return template
            .Replace("{players}", players.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{max}", maxPlayers.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{updated}", updated.ToString("HH:mm"), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeToken(string token)
    {
        token = (token ?? string.Empty).Trim().Trim('"').Trim('\'');
        const string botPrefix = "Bot ";
        if (token.StartsWith(botPrefix, StringComparison.OrdinalIgnoreCase)) token = token[botPrefix.Length..].Trim();
        return token;
    }

    private static string Truncate(string value, int maxLength)
    {
        value = value ?? string.Empty;
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is null) return;
        try
        {
            _client.MessageReceived -= OnMessageReceivedAsync;
            await _client.SetGameAsync(null);
            await _client.StopAsync();
            await _client.LogoutAsync();
        }
        catch
        {
        }
        finally
        {
            _client.Dispose();
            _client = null;
            _readyTcs = null;
            _connectionChanged?.Invoke(false);
        }
    }
}
