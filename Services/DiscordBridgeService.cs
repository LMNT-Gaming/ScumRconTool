using System.Net.Http;
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
    private static readonly HttpClient ItemImageClient = new() { Timeout = TimeSpan.FromSeconds(6) };

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

    public async Task SendTextMessageAsync(ulong channelId, string text)
    {
        if (_client is null || !IsReady) throw new InvalidOperationException("Discord Bot ist nicht verbunden.");
        if (channelId == 0) throw new InvalidOperationException("Discord Channel-ID f?r den SettingRandomizer fehlt.");
        if (_client.GetChannel(channelId) is not IMessageChannel channel)
        {
            throw new InvalidOperationException("Discord Channel wurde nicht gefunden. Ist der Bot auf dem Server und hat Zugriff auf den Channel?");
        }

        text = (text ?? string.Empty).Trim();
        if (text.Length == 0) return;
        if (text.Length > 2000) text = text[..1997] + "...";
        await channel.SendMessageAsync(text);
    }



    public async Task SendQuizChallengeAsync(
        ulong channelId,
        int quizNumber,
        string title,
        string question,
        string imagePath,
        string lootPackName,
        int maxAttempts,
        bool isGerman)
    {
        if (_client is null || !IsReady) throw new InvalidOperationException("Discord Bot ist nicht verbunden.");
        if (channelId == 0) throw new InvalidOperationException(isGerman ? "Discord Channel-ID für Rätsel-Challenges fehlt." : "Discord channel ID for quiz challenges is missing.");
        if (_client.GetChannel(channelId) is not IMessageChannel channel)
            throw new InvalidOperationException(isGerman ? "Discord Channel wurde nicht gefunden." : "Discord channel was not found.");

        var embed = new EmbedBuilder()
            .WithTitle(Truncate($"Quiz {quizNumber}: {title}", 250))
            .WithDescription(Truncate(question, 3900))
            .WithColor(new Color(211, 21, 42))
            .AddField(isGerman ? "So nimmst du teil" : "How to participate",
                isGerman ? $"Schreibe ingame `/quiz{quizNumber} DEINE ANTWORT`. Du hast **{maxAttempts} Versuche**." : $"Type `/quiz{quizNumber} YOUR ANSWER` in game. You have **{maxAttempts} attempts**.", false)
            .AddField(isGerman ? "Belohnung" : "Reward", CleanDiscordName(lootPackName), true)
            .WithFooter($"Red Raven | Quiz {quizNumber}")
            .WithCurrentTimestamp();

        if (!string.IsNullOrWhiteSpace(imagePath))
        {
            if (!File.Exists(imagePath)) throw new FileNotFoundException(isGerman ? "Das ausgewählte Rätselbild wurde nicht gefunden." : "The selected quiz image was not found.", imagePath);
            var fileName = Path.GetFileName(imagePath);
            embed.WithImageUrl("attachment://" + fileName);
            await channel.SendFileAsync(imagePath, embed: embed.Build());
            return;
        }

        await channel.SendMessageAsync(embed: embed.Build());
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

    public async Task SendVehicleInactivityWarningAsync(ulong channelId, VehicleInactivityWarning warning, string? iconUrl, CancellationToken cancellationToken = default)
    {
        if (_client is null || !IsReady) throw new InvalidOperationException("Discord Bot ist nicht verbunden.");
        if (channelId == 0 || _client.GetChannel(channelId) is not IMessageChannel channel)
            throw new InvalidOperationException("Discord-Channel für Fahrzeug-Inaktivitätswarnungen wurde nicht gefunden.");

        var unix = new DateTimeOffset(warning.DeletionUtc).ToUnixTimeSeconds();
        var builder = new EmbedBuilder()
            .WithTitle("⚠️ Fahrzeug wird bald gelöscht")
            .WithDescription($"**{CleanDiscordName(warning.VehicleName)}** von **{CleanDiscordName(warning.OwnerName)}**")
            .AddField("Fahrzeugtyp", CleanDiscordName(warning.VehicleName), true)
            .AddField("Fahrzeug-ID", warning.VehicleId, true)
            .AddField("Voraussichtliche Löschung", $"<t:{unix}:F>\n<t:{unix}:R>", true)
            .AddField("Was ist zu tun?", "Bewege beziehungsweise benutze das Fahrzeug rechtzeitig, damit der Inaktivitätszeitpunkt zurückgesetzt wird.")
            .WithColor(new Color(243, 156, 18))
            .WithFooter("RedRaven Fahrzeug-Inaktivitätswarnung")
            .WithCurrentTimestamp();
        if (Uri.TryCreate(iconUrl, UriKind.Absolute, out var iconUri) && iconUri.Scheme == Uri.UriSchemeHttps && iconUri.Host.Equals("icons.gghost.games", StringComparison.OrdinalIgnoreCase))
            builder.WithThumbnailUrl(iconUri.AbsoluteUri);
        var embed = builder.Build();
        await channel.SendMessageAsync(embed: embed, options: new RequestOptions { CancelToken = cancellationToken });
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
        IEnumerable<EventRuntime> runtimes,
        IReadOnlyCollection<string> variableSettings,
        bool isGerman)
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

        var embedBuilder = new EmbedBuilder()
            .WithTitle(title)
            .WithColor(new Color(46, 204, 113))
            .AddField("Server", $"**{serverName}**\n`{serverAddress}`", false)
            .AddField(isGerman ? "Spieler" : "Players", onlineText + "\n" + playerText, false)
            .AddField(isGerman ? "Wetter / Zeit" : "Weather / time", weatherText, false);

        if (variableSettings.Count > 0)
        {
            embedBuilder.AddField(
                isGerman ? "Variable Servereinstellungen" : "Variable server settings",
                BuildVariableServerSettingsText(variableSettings),
                false);
        }

        var embed = embedBuilder
            .AddField(isGerman ? "Aktive Events" : "Active events", eventText, false)
            .WithFooter(isGerman ? "Automatisch aktualisiert" : "Updated automatically")
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

    private static string BuildVariableServerSettingsText(IReadOnlyCollection<string> statusTexts)
    {
        var lines = statusTexts
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => "• " + CleanDiscordName(x))
            .Take(20);
        var text = string.Join(Environment.NewLine, lines);
        return text.Length <= 1024 ? text : text[..1021] + "...";
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
        var thermometer = char.ConvertFromUtf32(0x1F321) + char.ConvertFromUtf32(0xFE0F);
        var swimmer = char.ConvertFromUtf32(0x1F3CA);
        var clock = char.ConvertFromUtf32(0x231A);
        var degree = char.ConvertFromUtf32(0x00B0);

        return weather.GetWeatherIcon() + " Wetterwert " + score + Environment.NewLine +
               thermometer + " Luft: **" + air + degree + "C** | " +
               swimmer + " Wasser: **" + water + degree + "C** | " +
               clock + " Ingame: **" + time + "**";
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




    public async Task SendOrUpdateWeeklyTaskAsync(ulong channelId, WeeklyCommunityTaskProgress progress, bool isGerman)
    {
        if (_client is null || !IsReady) throw new InvalidOperationException("Discord Bot ist nicht verbunden.");
        if (channelId == 0) throw new InvalidOperationException("Discord Herausforderungen Channel-ID fehlt.");
        if (_client.GetChannel(channelId) is not IMessageChannel channel)
        {
            throw new InvalidOperationException("Discord Herausforderungen Channel wurde nicht gefunden.");
        }

        var definition = progress.Definition;
        var perPlayer = WeeklyCommunityTaskService.IsPersonalGoal(definition);
        var kind = WeeklyCommunityTaskService.GetTaskKind(definition);
        var title = string.IsNullOrWhiteSpace(definition.Title) ? (isGerman ? "Herausforderung" : "Challenge") : CleanDiscordName(definition.Title);
        var personIcon = char.ConvertFromUtf32(0x1F464);
        var globeIcon = char.ConvertFromUtf32(0x1F30D);
        var moneyIcon = char.ConvertFromUtf32(0x1F4B0);
        var starIcon = char.ConvertFromUtf32(0x2B50);
        var diceIcon = char.ConvertFromUtf32(0x1F3B2);
        const string bullet = "•";
        var discordTitle = (perPlayer ? personIcon + " " : globeIcon + " ") + title;
        var description = string.IsNullOrWhiteSpace(definition.Description) ? (isGerman ? "Aktive Herausforderung" : "Active challenge") : definition.Description.Trim();
        var rewardPackNames = WeeklyRewardItems.GetConfiguredLootPackNames(definition);
        var rewardItems = rewardPackNames.Count == 0
            ? WeeklyRewardItems.GetConfigured(definition)
            : new List<WeeklyRewardItemDefinition>();
        var rewardParts = rewardPackNames.Count > 0
            ? new List<string>
            {
                isGerman ? $"{diceIcon} Ein zufälliges Lootpack pro Empfänger:": $"{diceIcon} One random loot pack per recipient:",
                string.Join(Environment.NewLine, rewardPackNames.Select(name => $"{bullet} 🎁 {CleanDiscordName(name)}"))
            }
            : rewardItems.Select(x => bullet + " " + WeeklyRewardItems.Format(x)).ToList();
        if (definition.RewardMoney > 0) rewardParts.Add(isGerman ? $"{bullet} {moneyIcon} {definition.RewardMoney:N0} pro Empfaenger" : $"{bullet} {moneyIcon} {definition.RewardMoney:N0} per recipient");
        if (definition.RewardFame > 0) rewardParts.Add(isGerman ? $"{bullet} {starIcon} {definition.RewardFame:N0} Fame pro Empfaenger" : $"{bullet} {starIcon} {definition.RewardFame:N0} fame per recipient");
        var reward = Truncate(rewardParts.Count == 0 ? (isGerman ? "Keine Belohnung hinterlegt." : "No reward configured.") : string.Join(Environment.NewLine, rewardParts), 1024);

        var payoutPerSquad = string.Equals(definition.RewardDistribution, "PerSquad", StringComparison.OrdinalIgnoreCase);
        var contributorCount = progress.PlayerProgress.Count(x => x.Progress > 0);
        var multipleGoals = progress.GoalProgresses.Count > 1;
        var requireAllGoals = WeeklyCommunityTaskService.RequiresAllGoals(definition);
        var progressText = multipleGoals
            ? $"{progress.Percent:0.0}%{Environment.NewLine}{BuildProgressBar(progress.Percent)}"
            : $"{progress.Progress:N0}/{Math.Max(1, definition.Target):N0} ({progress.Percent:0.0}%){Environment.NewLine}{BuildProgressBar(progress.Percent)}";
        var participationText = payoutPerSquad
            ? BuildSquadAndSoloContributionText(progress, isGerman)
            : BuildPlayerContributionText(progress, isGerman);
        var participationHeading = payoutPerSquad
            ? (isGerman ? "Squads & Spieler ohne Squad" : "Squads & players without a squad")
            : (isGerman ? $"Teilnehmer ({contributorCount:N0})" : $"Participants ({contributorCount:N0})");
        var embedBuilder = new EmbedBuilder()
            .WithTitle(discordTitle)
            .WithColor(perPlayer ? new Color(88, 101, 242) : (progress.IsCompleted ? new Color(46, 204, 113) : new Color(241, 196, 15)))
            .WithDescription(description)
            .AddField(isGerman ? "Typ" : "Type", perPlayer ? "Personal" : "Community", true)
            .AddField(isGerman ? (requireAllGoals ? "Ziele (UND)" : "Ziele (ODER)") : (requireAllGoals ? "Goals (AND)" : "Goals (OR)"), BuildChallengeGoalText(progress), false);

        if (perPlayer)
        {
            var personalFields = BuildPersonalGoalContributionFields(progress, isGerman);
            for (var index = 0; index < personalFields.Count; index++)
            {
                embedBuilder.AddField(
                    index == 0 ? (isGerman ? "Persönliche Spielerstände" : "Personal player progress") : (isGerman ? "Spielerstände – Fortsetzung" : "Player progress – continued"),
                    personalFields[index],
                    false);
            }
        }
        else
        {
            embedBuilder.AddField(isGerman ? "Community-Fortschritt" : "Community progress", progressText, false);
            if (multipleGoals)
            {
                var contributionFields = BuildPersonalGoalContributionFields(progress, isGerman);
                for (var index = 0; index < contributionFields.Count; index++)
                {
                    embedBuilder.AddField(
                        index == 0 ? (isGerman ? "Spielerstände je Ziel" : "Player progress by goal") : (isGerman ? "Spielerstände – Fortsetzung" : "Player progress – continued"),
                        contributionFields[index],
                        false);
                }
            }
            else
            {
                embedBuilder.AddField(participationHeading, participationText, false);
            }
        }

        embedBuilder
            .AddField(isGerman ? "Loot & Belohnung" : "Loot & reward", reward, false)
            .AddField(isGerman ? "Laufzeit" : "Duration", BuildChallengeRuntimeText(progress, isGerman), false)
            .AddField("Status", progress.IsCompleted ? char.ConvertFromUtf32(0x2705) + " " + (isGerman ? "Erreicht" : "Completed") : (isGerman ? "Aktiv" : "Active"), true)
            .WithFooter($"RR-Herausforderung-ID: {definition.Id} | {kind}")
            .WithTimestamp(progress.UpdatedUtc);

        var firstItem = rewardItems.FirstOrDefault();
        if (firstItem is not null)
        {
            var imageUrl = WeeklyRewardItems.GetIconUrl(firstItem.Item);
            if (await IsItemImageAvailableAsync(imageUrl)) embedBuilder.WithThumbnailUrl(imageUrl);
        }

        var embed = embedBuilder.Build();
        var marker = "RR-Herausforderung-ID: " + definition.Id;
        var ownMessage = await FindOwnMessageAsync(channel, e =>
            e.Footer?.Text?.StartsWith(marker, StringComparison.OrdinalIgnoreCase) == true);
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

    public async Task<bool> SendOrUpdatePlannedWeeklyTasksAsync(
        ulong channelId,
        IReadOnlyCollection<WeeklyCommunityTaskDefinition> plannedDefinitions,
        bool isGerman)
    {
        if (_client is null || !IsReady || channelId == 0) return false;
        if (_client.GetChannel(channelId) is not IMessageChannel channel) return false;

        const string footerMarker = "RR-Herausforderungen-Planung";
        var entries = plannedDefinitions
            .Select(definition => new
            {
                Definition = definition,
                StartUtc = WeeklyCommunityTaskService.GetTaskStartUtc(definition)
            })
            .Where(x => x.StartUtc.HasValue)
            .OrderBy(x => x.StartUtc)
            .Select(x =>
            {
                var title = string.IsNullOrWhiteSpace(x.Definition.Title) ? x.Definition.Id : CleanDiscordName(x.Definition.Title);
                var unix = new DateTimeOffset(DateTime.SpecifyKind(x.StartUtc!.Value, DateTimeKind.Utc)).ToUnixTimeSeconds();
                return $"**{title}** - <t:{unix}:f>";
            })
            .ToList();

        var description = entries.Count == 0
            ? (isGerman ? "Aktuell sind keine Herausforderungen geplant." : "No challenges are currently scheduled.")
            : string.Join(Environment.NewLine, entries);

        var embed = new EmbedBuilder()
            .WithTitle(isGerman ? "Geplante Herausforderungen" : "Scheduled challenges")
            .WithDescription(Truncate(description, 4096))
            .WithColor(new Color(88, 101, 242))
            .WithFooter(footerMarker)
            .WithCurrentTimestamp()
            .Build();

        var ownMessage = await FindOwnMessageAsync(channel,
            e => string.Equals(e.Footer?.Text, footerMarker, StringComparison.Ordinal));
        if (ownMessage is not null)
        {
            await ownMessage.ModifyAsync(x =>
            {
                x.Content = string.Empty;
                x.Embed = embed;
            });
            return false;
        }

        await channel.SendMessageAsync(embed: embed);
        return true;
    }

    public async Task DeleteMarkedWeeklyTaskEmbedsForReorderAsync(ulong channelId)
    {
        if (_client?.CurrentUser is null || !IsReady || channelId == 0) return;
        if (_client.GetChannel(channelId) is not IMessageChannel channel) return;

        var messages = await channel.GetMessagesAsync(100).FlattenAsync();
        foreach (var message in messages.OfType<IUserMessage>().Where(x => x.Author.Id == _client.CurrentUser.Id))
        {
            var isMarkedChallenge = message.Embeds.Any(e =>
                e.Footer?.Text?.StartsWith("RR-Herausforderung-ID: ", StringComparison.OrdinalIgnoreCase) == true);
            if (isMarkedChallenge) await message.DeleteAsync();
        }
    }
    public async Task DeleteInactiveWeeklyTasksAsync(ulong channelId, IReadOnlyCollection<string> activeTaskIds)
    {
        if (_client?.CurrentUser is null || !IsReady || channelId == 0) return;
        if (_client.GetChannel(channelId) is not IMessageChannel channel) return;

        var active = new HashSet<string>(activeTaskIds.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
        var messages = await channel.GetMessagesAsync(100).FlattenAsync();
        foreach (var message in messages.OfType<IUserMessage>().Where(x => x.Author.Id == _client.CurrentUser.Id))
        {
            var challengeEmbed = message.Embeds.FirstOrDefault(e =>
                e.Footer?.Text?.StartsWith("RR-Herausforderung-ID: ", StringComparison.OrdinalIgnoreCase) == true);
            if (challengeEmbed is not null)
            {
                var footer = challengeEmbed.Footer?.Text ?? string.Empty;
                var idPart = footer["RR-Herausforderung-ID: ".Length..];
                var separator = idPart.IndexOf(" |", StringComparison.Ordinal);
                var taskId = (separator >= 0 ? idPart[..separator] : idPart).Trim();
                if (!active.Contains(taskId)) await message.DeleteAsync();
                continue;
            }

        }
    }
    private static async Task<bool> IsItemImageAvailableAsync(string url)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            request.Headers.TryAddWithoutValidation("User-Agent", "RedRavenRconTool/1.0");
            using var response = await ItemImageClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            return response.IsSuccessStatusCode && mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
    private static string BuildChallengeRuntimeText(WeeklyCommunityTaskProgress progress, bool isGerman)
    {
        var configuredStartUtc = WeeklyCommunityTaskService.GetTaskStartUtc(progress.Definition);
        var startUtc = configuredStartUtc ?? (progress.Baseline.CreatedUtc == default ? progress.UpdatedUtc : progress.Baseline.CreatedUtc);
        var endUtc = WeeklyCommunityTaskService.GetTaskEndUtc(progress.Definition, startUtc);
        if (endUtc is null)
        {
            return isGerman ? "Keine feste Laufzeit konfiguriert." : "No fixed duration configured.";
        }

        var nowUtc = DateTime.UtcNow;
        var remaining = endUtc.Value - nowUtc;
        var unixEnd = new DateTimeOffset(DateTime.SpecifyKind(endUtc.Value, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var unixStart = new DateTimeOffset(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();

        if (progress.IsCompleted)
        {
            return isGerman ? $"Abgeschlossen. Lief seit <t:{unixStart}:f> bis <t:{unixEnd}:f>." : $"Completed. Ran from <t:{unixStart}:f> until <t:{unixEnd}:f>.";
        }

        if (remaining <= TimeSpan.Zero)
        {
            return isGerman ? $"Abgelaufen seit <t:{unixEnd}:R>. Ende war <t:{unixEnd}:f>." : $"Expired <t:{unixEnd}:R>. Ended at <t:{unixEnd}:f>.";
        }

        return isGerman ? $"Endet <t:{unixEnd}:R> (<t:{unixEnd}:f>)\nVerbleibend: {FormatDuration(remaining)}" : $"Ends <t:{unixEnd}:R> (<t:{unixEnd}:f>)\nRemaining: {FormatDuration(remaining)}";
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

    private static string BuildChallengeGoalText(WeeklyCommunityTaskProgress progress)
    {
        var goals = progress.GoalProgresses.Count > 0
            ? progress.GoalProgresses
            : new List<WeeklyCommunityTaskGoalProgress>
            {
                new()
                {
                    StatTable = progress.Definition.StatTable,
                    StatColumn = progress.Definition.StatColumn,
                    DisplayName = progress.Definition.StatColumn,
                    Target = Math.Max(1, progress.Definition.Target),
                    Progress = progress.Progress,
                    Percent = progress.Percent,
                    IsCompleted = progress.IsCompleted
                }
            };
        if (WeeklyCommunityTaskService.IsPersonalGoal(progress.Definition))
        {
            return Truncate(string.Join(Environment.NewLine, goals.Select(goal =>
                $"• **{CleanDiscordName(goal.DisplayName)}**: Ziel {Math.Max(1, goal.Target):N0}")), 1024);
        }

        var checkMark = char.ConvertFromUtf32(0x2705);
        return Truncate(string.Join(Environment.NewLine, goals.Select(goal =>
            $"• **{CleanDiscordName(goal.DisplayName)}**: {goal.Progress:N0}/{Math.Max(1, goal.Target):N0} ({goal.Percent:0.0}%)" +
            (goal.IsCompleted ? " " + checkMark : string.Empty))), 1024);
    }

    private static List<string> BuildPersonalGoalContributionFields(WeeklyCommunityTaskProgress progress, bool isGerman)
    {
        var goals = progress.GoalProgresses;
        if (goals.Count == 0) return new List<string> { BuildPlayerContributionText(progress, isGerman) };

        static string PlayerKey(WeeklyCommunityTaskPlayerProgress player) =>
            string.IsNullOrWhiteSpace(player.SteamId) ? "name:" + player.PlayerName : "steam:" + player.SteamId;

        var playerKeys = goals
            .SelectMany(goal => goal.PlayerProgress)
            .Where(player => player.Progress > 0)
            .Select(PlayerKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (playerKeys.Count == 0)
        {
            return new List<string> { isGerman ? "Noch keine Spielerbeiträge seit dem Start erfasst." : "No player contributions recorded since the start." };
        }

        var blocks = playerKeys.Select(key =>
            {
                var identity = goals.SelectMany(goal => goal.PlayerProgress).First(player => PlayerKey(player).Equals(key, StringComparison.OrdinalIgnoreCase));
                var lines = new List<string> { $"**{CleanDiscordName(identity.PlayerName)}**" };
                foreach (var goal in goals)
                {
                    var own = goal.PlayerProgress.FirstOrDefault(player => PlayerKey(player).Equals(key, StringComparison.OrdinalIgnoreCase));
                    var value = own?.Progress ?? 0;
                    var target = Math.Max(1, goal.Target);
                    var percent = own?.Percent ?? 0;
                    var check = own?.IsCompleted == true ? " " + char.ConvertFromUtf32(0x2705) : string.Empty;
                    lines.Add($"• {CleanDiscordName(goal.DisplayName)}: {value:N0}/{target:N0} ({percent:0.0}%){check}");
                }
                return new { Text = string.Join(Environment.NewLine, lines), Score = goals.Sum(goal => goal.PlayerProgress.FirstOrDefault(player => PlayerKey(player).Equals(key, StringComparison.OrdinalIgnoreCase))?.Percent ?? 0) };
            })
            .OrderByDescending(x => x.Score)
            .ToList();

        var fields = new List<string>();
        var current = new List<string>();
        foreach (var block in blocks)
        {
            var candidate = string.Join(Environment.NewLine + Environment.NewLine, current.Append(block.Text));
            if (candidate.Length > 1000 && current.Count > 0)
            {
                fields.Add(string.Join(Environment.NewLine + Environment.NewLine, current));
                current.Clear();
            }
            current.Add(block.Text);
            if (fields.Count >= 15) break;
        }
        if (current.Count > 0 && fields.Count < 16) fields.Add(Truncate(string.Join(Environment.NewLine + Environment.NewLine, current), 1024));
        return fields;
    }

    private static string BuildPlayerContributionText(WeeklyCommunityTaskProgress progress, bool isGerman)
    {
        var checkMark = char.ConvertFromUtf32(0x2705);
        var contributors = progress.PlayerProgress
            .Where(player => player.Progress > 0)
            .OrderByDescending(player => player.IsCompleted)
            .ThenByDescending(player => player.Progress)
            .ThenBy(player => player.PlayerName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (contributors.Count == 0)
        {
            return isGerman ? "Noch keine Spielerbeitraege seit dem Start erfasst." : "No player contributions recorded since the start.";
        }

        var target = Math.Max(1, progress.Definition.Target);
        var lines = new List<string>();
        foreach (var player in contributors)
        {
            var playerTarget = player.Target > 0 ? player.Target : target;
            var line = $"**{CleanDiscordName(player.PlayerName)}**: {player.Progress:N0}/{playerTarget:N0}" + (player.IsCompleted ? " " + checkMark : string.Empty);
            var candidate = string.Join(Environment.NewLine, lines.Append(line));
            if (candidate.Length > 960)
            {
                lines.Add(isGerman ? $"... und {contributors.Count - lines.Count:N0} weitere Spieler" : $"... and {contributors.Count - lines.Count:N0} more players");
                break;
            }
            lines.Add(line);
        }

        return Truncate(string.Join(Environment.NewLine, lines), 1024);
    }
    private static string BuildSquadAndSoloContributionText(WeeklyCommunityTaskProgress progress, bool isGerman)
    {
        var checkMark = char.ConvertFromUtf32(0x2705);
        var lines = progress.SquadProgress
            .Where(x => x.Progress > 0)
            .OrderByDescending(x => x.Progress)
            .ThenBy(x => x.SquadName, StringComparer.OrdinalIgnoreCase)
            .Select(x => $"Squad **{CleanDiscordName(x.SquadName)}**: {x.Progress:N0}" + (x.IsSuccessfulParticipant ? " " + checkMark : string.Empty))
            .ToList();

        foreach (var player in progress.PlayerProgress
                     .Where(x => !x.HasSquad && x.Progress > 0)
                     .OrderByDescending(x => x.Progress)
                     .ThenBy(x => x.PlayerName, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add($"**{CleanDiscordName(player.PlayerName)}**: {player.Progress:N0}" +
                      (player.Progress >= progress.MinimumParticipationValue ? " " + checkMark : string.Empty));
        }

        if (lines.Count == 0)
        {
            return isGerman ? "Noch keine Beitraege seit dem Start erfasst." : "No contributions recorded since the start.";
        }

        var result = new List<string>();
        foreach (var line in lines)
        {
            if (string.Join(Environment.NewLine, result.Append(line)).Length > 1000)
            {
                result.Add(isGerman ? "... weitere Teilnehmer" : "... more participants");
                break;
            }
            result.Add(line);
        }
        return Truncate(string.Join(Environment.NewLine, result), 1024);
    }
    private static string BuildSquadContributionText(WeeklyCommunityTaskProgress progress, bool isGerman)
    {
        if (progress.SquadProgress.Count == 0)
        {
            return isGerman ? "Keine Squads in der DB gefunden oder keine Spieler sind einem Squad zugeordnet." : "No squads found in the database, or no players are assigned to a squad.";
        }

        var qualifiedSquads = progress.SquadProgress
            .Where(x => x.IsSuccessfulParticipant)
            .OrderByDescending(x => x.Progress)
            .ThenBy(x => x.SquadName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var minimumLine = isGerman ? $"Mindestbeitrag fuer erfolgreiche Teilnahme: **{progress.MinimumParticipationValue:N0}**." : $"Minimum contribution for successful participation: **{progress.MinimumParticipationValue:N0}**.";

        if (qualifiedSquads.Count == 0)
        {
            var bestSquad = progress.SquadProgress
                .OrderByDescending(x => x.Progress)
                .FirstOrDefault();

            if (bestSquad is null || bestSquad.Progress <= 0)
            {
                return minimumLine + (isGerman ? "\nNoch kein Squad hat seit dem gespeicherten Startwert beigetragen." : "\nNo squad has contributed since the saved baseline yet.");
            }

            return minimumLine + (isGerman ? $"\nNoch kein Squad hat die Mindestbeteiligung erreicht. Bester Stand: **{CleanDiscordName(bestSquad.SquadName)}** mit {bestSquad.Progress:N0}." : $"\nNo squad has reached the minimum contribution yet. Best progress: **{CleanDiscordName(bestSquad.SquadName)}** with {bestSquad.Progress:N0}.");
        }

        var lines = qualifiedSquads
            .Take(10)
            .Select((squad, index) => $"{index + 1}. **{CleanDiscordName(squad.SquadName)}**: {squad.Progress:N0}")
            .ToList();

        lines.Insert(0, minimumLine);

        if (qualifiedSquads.Count > 10)
        {
            var rest = qualifiedSquads.Skip(10).Sum(x => x.Progress);
            lines.Add(isGerman ? $"Weitere erfolgreiche Squads: {rest:N0}" : $"Additional successful squads: {rest:N0}");
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
            var messages = await channel.GetMessagesAsync(100).FlattenAsync();
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
