using System.Text.RegularExpressions;
using System.Windows.Input;
using Microsoft.Win32;
using ScumRconTool.Models;
using ScumRconTool.Services;

namespace ScumRconTool.ViewModels;

public sealed partial class MainViewModel
{
    private readonly QuizChallengeService _quizChallengeService = new();
    private readonly SemaphoreSlim _quizScheduleLock = new(1, 1);
    private CancellationTokenSource? _quizScheduleCts;

    public ICommand BrowseQuizChallengeImageCommand { get; private set; } = null!;
    public ICommand StartQuizChallengeCommand { get; private set; } = null!;
    public ICommand EndQuizChallengeCommand { get; private set; } = null!;

    private void InitializeQuizChallenge()
    {
        BrowseQuizChallengeImageCommand = new RelayCommand(parameter => BrowseQuizChallengeImage(parameter as WeeklyTaskEditorViewModel));
        StartQuizChallengeCommand = new RelayCommand(async parameter => await StartQuizChallengeAsync(parameter as WeeklyTaskEditorViewModel, forceRestart: true));
        EndQuizChallengeCommand = new RelayCommand(parameter => EndQuizChallenge(parameter as WeeklyTaskEditorViewModel));
        _quizScheduleCts = new CancellationTokenSource();
        _ = RunQuizScheduleLoopAsync(_quizScheduleCts.Token);
    }

    private void RefreshQuizLootPacks()
    {
        // Quiz cards bind directly to the shared GlobalLootPacks collection.
    }

    private void BrowseQuizChallengeImage(WeeklyTaskEditorViewModel? editor)
    {
        if (editor is null) return;
        var dialog = new OpenFileDialog
        {
            Title = T("QuizChooseImage"),
            Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.gif|All files|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() == true) editor.QuizImagePath = dialog.FileName;
    }

    private async Task StartQuizChallengeAsync(WeeklyTaskEditorViewModel? editor, bool forceRestart = false)
    {
        if (editor is null) return;
        await _quizScheduleLock.WaitAsync();
        try
        {
            var definition = editor.ToDefinition();
            if (!definition.Type.Equals("Quiz", StringComparison.OrdinalIgnoreCase)) return;
            if (definition.QuizNumber <= 0) throw new InvalidOperationException(Texts.IsGerman ? "Die Quiz-ID muss größer 0 sein." : "The quiz ID must be greater than 0.");

            SyncWeeklyTaskEditorsToSettings();
            SettingsStore.Save(Settings);
            var nowUtc = DateTime.UtcNow;
            var configuredStartUtc = WeeklyCommunityTaskService.GetTaskStartUtc(definition);
            if (configuredStartUtc.HasValue && configuredStartUtc.Value > nowUtc)
            {
                WeeklyTaskStatus = Texts.IsGerman
                    ? $"Quiz {definition.QuizNumber} '{definition.Title}' ist für {configuredStartUtc.Value.ToLocalTime():dd.MM.yyyy HH:mm} geplant."
                    : $"Quiz {definition.QuizNumber} '{definition.Title}' is scheduled for {configuredStartUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm}.";
                return;
            }

            var endsUtc = configuredStartUtc.HasValue
                ? WeeklyCommunityTaskService.GetTaskEndUtc(definition, configuredStartUtc.Value)
                : nowUtc.AddHours(Math.Max(1, definition.DurationHours));
            if (endsUtc.HasValue && endsUtc.Value <= nowUtc)
                throw new InvalidOperationException(Texts.IsGerman ? "Das geplante Zeitfenster ist bereits abgelaufen." : "The scheduled time window has already ended.");

            var scheduleKey = configuredStartUtc.HasValue
                ? $"quiz:{definition.QuizNumber}|{definition.Id}|{configuredStartUtc.Value:O}"
                : $"manual|quiz:{definition.QuizNumber}|{definition.Id}|{Guid.NewGuid():N}";
            var existing = _quizChallengeService.GetByNumber(definition.QuizNumber);
            if (!forceRestart && configuredStartUtc.HasValue && existing?.ScheduleKey.Equals(scheduleKey, StringComparison.OrdinalIgnoreCase) == true) return;
            if (existing?.IsActive == true)
            {
                if (!existing.DefinitionId.Equals(definition.Id, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(Texts.IsGerman
                        ? $"Quiz-ID {definition.QuizNumber} wird bereits von '{existing.Title}' verwendet."
                        : $"Quiz ID {definition.QuizNumber} is already used by '{existing.Title}'.");
                }

                if (!forceRestart) return;
            }

            if (string.IsNullOrWhiteSpace(definition.Title)) throw new InvalidOperationException(Texts.IsGerman ? "Bitte einen Titel eintragen." : "Enter a title.");
            if (string.IsNullOrWhiteSpace(definition.QuizQuestion)) throw new InvalidOperationException(Texts.IsGerman ? "Bitte eine Frage eintragen." : "Enter a question.");
            if (!string.IsNullOrWhiteSpace(definition.QuizImagePath) && !File.Exists(definition.QuizImagePath))
                throw new FileNotFoundException(Texts.IsGerman ? "Das ausgewählte Bild wurde nicht gefunden." : "The selected image was not found.", definition.QuizImagePath);

            var answers = definition.QuizAcceptedAnswers
                .Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (answers.Length == 0) throw new InvalidOperationException(T("QuizAnswerMissing"));
            if (Settings.WeeklyTaskDiscordChannelId == 0) throw new InvalidOperationException(T("QuizChannelMissing"));

            var configuredItems = WeeklyRewardItems.GetConfigured(definition);
            var rewardPackNames = WeeklyRewardItems.GetConfiguredLootPackNames(definition);
            if (configuredItems.Count == 0 && definition.RewardMoney <= 0 && definition.RewardFame <= 0)
                throw new InvalidOperationException(Texts.IsGerman ? "Bitte Lootpack, Geld oder Fame als Belohnung konfigurieren." : "Configure a loot pack, money, or fame as the reward.");
            var lootItems = configuredItems.Select(x => new LootItem { Item = x.Item, Quantity = x.Quantity }).ToList();
            var rewardSummary = BuildQuizRewardSummary(configuredItems, definition.RewardMoney, definition.RewardFame, rewardPackNames);

            if (_discord is null || !_discord.IsReady) await StartDiscordAsync();
            if (_discord is null || !_discord.IsReady) throw new InvalidOperationException(T("QuizDiscordNotReady"));
            await _discord.SendQuizChallengeAsync(
                Settings.WeeklyTaskDiscordChannelId,
                definition.QuizNumber,
                definition.Title,
                definition.QuizQuestion,
                definition.QuizImagePath,
                rewardSummary,
                2,
                Texts.IsGerman);

            var state = _quizChallengeService.Start(
                definition.QuizNumber,
                definition.Id,
                definition.Title,
                definition.QuizQuestion,
                answers,
                definition.QuizImagePath,
                rewardPackNames,
                lootItems,
                definition.RewardMoney,
                definition.RewardFame,
                endsUtc,
                scheduleKey);
            WeeklyTaskStatus = Texts.IsGerman
                ? $"Quiz {state.QuizNumber} aktiv: {state.Title} · endet {state.EndsUtc?.ToLocalTime():dd.MM.yyyy HH:mm}"
                : $"Quiz {state.QuizNumber} active: {state.Title} · ends {state.EndsUtc?.ToLocalTime():yyyy-MM-dd HH:mm}";
            if (_chatCommands?.IsRunning != true) await StartChatCommandsAsync(persistAutoStart: false, ensureDefaultRules: false);
            Log($"Quiz {state.QuizNumber} gestartet: {state.Title} ({rewardSummary}), Ende: {state.EndsUtc:O}.");
        }
        catch (Exception ex)
        {
            WeeklyTaskStatus = ex.Message;
            Log("Quiz konnte nicht gestartet werden: " + ex.Message);
            AppLogService.WriteException("QuizChallenge.Start", ex);
        }
        finally
        {
            _quizScheduleLock.Release();
        }
    }

    private async Task RunQuizScheduleLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await EvaluateQuizScheduleAsync();
                }
                catch (Exception ex)
                {
                    Log("Quiz-Zeitplanfehler: " + ex.Message);
                    AppLogService.WriteException("QuizChallenge.Schedule", ex);
                }
                await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task EvaluateQuizScheduleAsync()
    {
        var nowUtc = DateTime.UtcNow;
        foreach (var expired in _quizChallengeService.GetSnapshots().Where(x => x.IsActive && x.EndsUtc.HasValue && x.EndsUtc.Value <= nowUtc))
        {
            EndQuizChallengeByNumber(expired.QuizNumber, automatic: true);
        }

        var dueEntries = WeeklyTaskEditors
            .Where(x => x.Enabled && x.IsQuiz)
            .Select(x => new { Editor = x, Definition = x.ToDefinition() })
            .Select(x => new { x.Editor, x.Definition, StartUtc = WeeklyCommunityTaskService.GetTaskStartUtc(x.Definition) })
            .Where(x => x.StartUtc.HasValue && x.StartUtc.Value <= nowUtc)
            .Select(x => new { x.Editor, x.Definition, x.StartUtc, EndUtc = WeeklyCommunityTaskService.GetTaskEndUtc(x.Definition, x.StartUtc!.Value) })
            .Where(x => !x.EndUtc.HasValue || x.EndUtc.Value > nowUtc)
            .OrderBy(x => x.StartUtc)
            .ToList();

        foreach (var due in dueEntries)
        {
            var state = _quizChallengeService.GetByNumber(due.Definition.QuizNumber);
            var key = $"quiz:{due.Definition.QuizNumber}|{due.Definition.Id}|{due.StartUtc!.Value:O}";
            if (state?.IsActive == true || state?.ScheduleKey.Equals(key, StringComparison.OrdinalIgnoreCase) == true) continue;
            await StartQuizChallengeAsync(due.Editor, forceRestart: false);
        }
    }

    private void EndQuizChallenge(WeeklyTaskEditorViewModel? editor)
    {
        if (editor is null) return;
        EndQuizChallengeByNumber(editor.QuizNumber, automatic: false);
    }

    private void EndQuizChallengeByNumber(int quizNumber, bool automatic)
    {
        if (!_quizChallengeService.End(quizNumber)) return;
        WeeklyTaskStatus = automatic
            ? (Texts.IsGerman ? $"Zeitfenster für Quiz {quizNumber} beendet." : $"Time window for quiz {quizNumber} ended.")
            : (Texts.IsGerman ? $"Quiz {quizNumber} beendet." : $"Quiz {quizNumber} ended.");
        Log(automatic ? $"Quiz {quizNumber} automatisch nach Zeitablauf beendet." : $"Quiz {quizNumber} beendet.");
    }

    private async Task<bool> HandleQuizChallengeChatCommandAsync(ChatLogMessage message, CancellationToken cancellationToken)
    {
        var text = (message.Message ?? string.Empty).Trim();
        var match = Regex.Match(text, BuiltinChatCommandCatalog.QuizPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) return false;
        if (string.IsNullOrWhiteSpace(message.SteamId)) return true;

        var quizNumber = int.TryParse(match.Groups["number"].Value, out var parsedNumber) ? parsedNumber : 0;
        var answer = match.Groups["answer"].Value.Trim();
        var result = _quizChallengeService.SubmitAnswer(quizNumber, message.SteamId, message.PlayerName, answer);
        string response;
        switch (result.Status)
        {
            case QuizAnswerStatus.NoActiveChallenge:
                response = Tf("QuizNoActiveAnswer", quizNumber);
                break;
            case QuizAnswerStatus.MissingAnswer:
                response = Tf("QuizAnswerUsage", quizNumber);
                break;
            case QuizAnswerStatus.AlreadySolved:
                response = Tf("QuizAlreadySolved", quizNumber);
                break;
            case QuizAnswerStatus.NoAttemptsLeft:
                response = Tf("QuizNoAttempts", quizNumber);
                break;
            case QuizAnswerStatus.Incorrect:
                response = Tf("QuizIncorrect", quizNumber, result.RemainingAttempts);
                break;
            case QuizAnswerStatus.Correct:
                var completionKey = $"quiz:{result.ChallengeId}|player:{message.SteamId.Trim()}";
                var claim = _weeklyRewardStore.CreateLootPackClaim(
                    completionKey,
                    "quiz-" + result.ChallengeId,
                    result.Title,
                    message.SteamId,
                    message.PlayerName,
                    result.RewardItems,
                    result.RewardMoney,
                    result.RewardFame);
                var rewardSummary = BuildQuizRewardSummary(
                    result.RewardItems.Select(x => new WeeklyRewardItemDefinition { Item = x.Item, Quantity = x.Quantity }),
                    result.RewardMoney,
                    result.RewardFame);
                response = claim is null
                    ? Tf("QuizAlreadySolved", quizNumber)
                    : Tf("QuizCorrect", quizNumber, claim.Code, rewardSummary);
                RefreshWeeklyRewardClaims();
                WeeklyTaskStatus = Texts.IsGerman
                    ? $"Quiz {quizNumber} aktiv: {result.Title} · richtige Antwort von {message.PlayerName}"
                    : $"Quiz {quizNumber} active: {result.Title} · correct answer from {message.PlayerName}";
                break;
            default:
                response = Tf("QuizNoActiveAnswer", quizNumber);
                break;
        }

        await SendWeeklyRewardNoticeAsync(message.SteamId, response, cancellationToken);
        return true;
    }

    private static string BuildQuizRewardSummary(IEnumerable<WeeklyRewardItemDefinition> items, int money, int fame, IEnumerable<string>? randomPackNames = null)
    {
        var packNames = (randomPackNames ?? Array.Empty<string>()).Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
        var rewardParts = packNames.Count > 0
            ? new[] { "🎲 " + string.Join(" / ", packNames) }
            : items.Select(WeeklyRewardItems.Format);
        return string.Join(" + ", rewardParts
            .Concat(new[] { money > 0 ? $"{money}$" : string.Empty, fame > 0 ? $"⭐ {fame} Fame" : string.Empty })
            .Where(x => !string.IsNullOrWhiteSpace(x)));
    }
}
