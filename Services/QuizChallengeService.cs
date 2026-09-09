using System.Text;
using System.Text.Json;
using ScumRconTool.Models;

namespace ScumRconTool.Services;

public enum QuizAnswerStatus
{
    NoActiveChallenge,
    MissingAnswer,
    AlreadySolved,
    NoAttemptsLeft,
    Incorrect,
    Correct
}

public sealed class QuizChallengeState
{
    public string Id { get; set; } = string.Empty;
    public string DefinitionId { get; set; } = string.Empty;
    public int QuizNumber { get; set; } = 1;
    public string Title { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public List<string> AcceptedAnswers { get; set; } = new();
    public string ImagePath { get; set; } = string.Empty;
    public string LootPackName { get; set; } = string.Empty;
    public List<string> LootPackNames { get; set; } = new();
    public List<LootItem> RewardItems { get; set; } = new();
    public int RewardMoney { get; set; }
    public int RewardFame { get; set; }
    public int MaxAttempts { get; set; } = 2;
    public bool IsActive { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? EndsUtc { get; set; }
    public string ScheduleKey { get; set; } = string.Empty;
    public Dictionary<string, int> AttemptsBySteamId { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> PlayerNamesBySteamId { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Winners { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class QuizChallengeStoreState
{
    public List<QuizChallengeState> Challenges { get; set; } = new();
}

public sealed record QuizAnswerResult(
    QuizAnswerStatus Status,
    int QuizNumber,
    int RemainingAttempts,
    string ChallengeId,
    string Title,
    string LootPackName,
    IReadOnlyList<LootItem> RewardItems,
    int RewardMoney,
    int RewardFame);

public sealed class QuizChallengeService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly object _sync = new();
    private readonly string _path;
    private QuizChallengeStoreState _store;

    public QuizChallengeService()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScumRconTool", "State");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "quiz-challenge.json");
        _store = Load();
    }

    public bool HasActiveChallenge
    {
        get { lock (_sync) return _store.Challenges.Any(x => x.IsActive); }
    }

    public IReadOnlyList<QuizChallengeState> GetSnapshots()
    {
        lock (_sync) return _store.Challenges.Select(Clone).OrderBy(x => x.QuizNumber).ToList();
    }

    public QuizChallengeState? GetByNumber(int quizNumber)
    {
        lock (_sync)
        {
            var state = _store.Challenges.LastOrDefault(x => x.QuizNumber == quizNumber);
            return state is null ? null : Clone(state);
        }
    }

    public QuizChallengeState? GetByDefinitionId(string definitionId)
    {
        lock (_sync)
        {
            var state = _store.Challenges.LastOrDefault(x => x.DefinitionId.Equals(definitionId, StringComparison.OrdinalIgnoreCase));
            return state is null ? null : Clone(state);
        }
    }

    public QuizChallengeState Start(
        int quizNumber,
        string definitionId,
        string title,
        string question,
        IEnumerable<string> acceptedAnswers,
        string imagePath,
        IEnumerable<string> lootPackNames,
        IEnumerable<LootItem> rewardItems,
        int rewardMoney,
        int rewardFame,
        DateTime? endsUtc,
        string scheduleKey)
    {
        if (quizNumber <= 0) throw new InvalidOperationException("Die Quiz-ID muss größer 0 sein.");
        var answers = acceptedAnswers.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (string.IsNullOrWhiteSpace(title)) throw new InvalidOperationException("Ein Titel fehlt.");
        if (string.IsNullOrWhiteSpace(question)) throw new InvalidOperationException("Eine Frage fehlt.");
        if (answers.Count == 0) throw new InvalidOperationException("Mindestens eine richtige Antwort fehlt.");
        var items = rewardItems.Where(x => !string.IsNullOrWhiteSpace(x.Item)).Select(CloneItem).ToList();
        var packNames = lootPackNames.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (items.Count == 0 && rewardMoney <= 0 && rewardFame <= 0) throw new InvalidOperationException("Mindestens eine Belohnung fehlt.");

        lock (_sync)
        {
            _store.Challenges.RemoveAll(x => x.QuizNumber == quizNumber);
            var state = new QuizChallengeState
            {
                Id = Guid.NewGuid().ToString("N"),
                DefinitionId = (definitionId ?? string.Empty).Trim(),
                QuizNumber = quizNumber,
                Title = title.Trim(),
                Question = question.Trim(),
                AcceptedAnswers = answers,
                ImagePath = (imagePath ?? string.Empty).Trim(),
                LootPackName = packNames.FirstOrDefault() ?? string.Empty,
                LootPackNames = packNames,
                RewardItems = items,
                RewardMoney = Math.Max(0, rewardMoney),
                RewardFame = Math.Max(0, rewardFame),
                MaxAttempts = 2,
                IsActive = true,
                CreatedUtc = DateTime.UtcNow,
                EndsUtc = endsUtc,
                ScheduleKey = scheduleKey ?? string.Empty
            };
            _store.Challenges.Add(state);
            SaveCore();
            return Clone(state);
        }
    }

    public bool End(int quizNumber)
    {
        lock (_sync)
        {
            var state = _store.Challenges.LastOrDefault(x => x.QuizNumber == quizNumber && x.IsActive);
            if (state is null) return false;
            state.IsActive = false;
            SaveCore();
            return true;
        }
    }

    public int Reset(int quizNumber, string definitionId)
    {
        definitionId = (definitionId ?? string.Empty).Trim();
        lock (_sync)
        {
            var removed = _store.Challenges.RemoveAll(state =>
                state.QuizNumber == quizNumber &&
                (definitionId.Length == 0 || state.DefinitionId.Equals(definitionId, StringComparison.OrdinalIgnoreCase)));
            if (removed > 0) SaveCore();
            return removed;
        }
    }

    public QuizAnswerResult SubmitAnswer(int quizNumber, string steamId, string playerName, string answer)
    {
        steamId = (steamId ?? string.Empty).Trim();
        playerName = (playerName ?? string.Empty).Trim();
        answer = (answer ?? string.Empty).Trim();
        lock (_sync)
        {
            var state = _store.Challenges.LastOrDefault(x => x.QuizNumber == quizNumber && x.IsActive);
            if (state is null) return EmptyResult(QuizAnswerStatus.NoActiveChallenge, quizNumber);
            if (answer.Length == 0) return Result(state, QuizAnswerStatus.MissingAnswer, state.MaxAttempts);
            state.PlayerNamesBySteamId ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var nameChanged = playerName.Length > 0 &&
                              (!state.PlayerNamesBySteamId.TryGetValue(steamId, out var savedName) || !savedName.Equals(playerName, StringComparison.Ordinal));
            if (nameChanged) state.PlayerNamesBySteamId[steamId] = playerName;
            if (state.Winners.Contains(steamId))
            {
                if (nameChanged) SaveCore();
                return Result(state, QuizAnswerStatus.AlreadySolved, 0);
            }

            state.AttemptsBySteamId.TryGetValue(steamId, out var attempts);
            if (attempts >= state.MaxAttempts) return Result(state, QuizAnswerStatus.NoAttemptsLeft, 0);

            attempts++;
            state.AttemptsBySteamId[steamId] = attempts;
            var correct = state.AcceptedAnswers.Any(candidate => Normalize(candidate) == Normalize(answer));
            if (correct) state.Winners.Add(steamId);
            SaveCore();
            return correct
                ? CorrectResult(state, Math.Max(0, state.MaxAttempts - attempts))
                : Result(state, QuizAnswerStatus.Incorrect, Math.Max(0, state.MaxAttempts - attempts));
        }
    }

    private static QuizAnswerResult CorrectResult(QuizChallengeState state, int remaining)
    {
        var definition = new WeeklyCommunityTaskDefinition
        {
            RewardLootPackName = state.LootPackName,
            RewardLootPackNames = state.LootPackNames ?? new List<string>()
        };
        var selectedPack = WeeklyRewardItems.ResolveRandomLootPack(definition);
        var lootPackName = selectedPack?.Name ?? state.LootPackName;
        var items = selectedPack is null
            ? state.RewardItems.Select(CloneItem).ToList()
            : selectedPack.Items.Select(item => new LootItem { Item = item.Item, Quantity = item.Quantity }).ToList();
        return new QuizAnswerResult(
            QuizAnswerStatus.Correct, state.QuizNumber, remaining, state.Id, state.Title, lootPackName,
            items, state.RewardMoney, state.RewardFame);
    }

    private static QuizAnswerResult Result(QuizChallengeState state, QuizAnswerStatus status, int remaining) => new(
        status, state.QuizNumber, remaining, state.Id, state.Title, state.LootPackName,
        state.RewardItems.Select(CloneItem).ToList(), state.RewardMoney, state.RewardFame);

    private static QuizAnswerResult EmptyResult(QuizAnswerStatus status, int quizNumber) =>
        new(status, quizNumber, 0, string.Empty, string.Empty, string.Empty, Array.Empty<LootItem>(), 0, 0);

    private QuizChallengeStoreState Load()
    {
        try
        {
            if (!File.Exists(_path)) return new QuizChallengeStoreState();
            var json = File.ReadAllText(_path);
            using var document = JsonDocument.Parse(json);
            QuizChallengeStoreState store;
            if (document.RootElement.TryGetProperty("Challenges", out _))
            {
                store = JsonSerializer.Deserialize<QuizChallengeStoreState>(json, Options) ?? new QuizChallengeStoreState();
            }
            else
            {
                var legacy = JsonSerializer.Deserialize<QuizChallengeState>(json, Options);
                store = new QuizChallengeStoreState();
                if (legacy is not null && !string.IsNullOrWhiteSpace(legacy.Id)) store.Challenges.Add(legacy);
            }

            store.Challenges ??= new();
            foreach (var state in store.Challenges)
            {
                state.QuizNumber = Math.Max(1, state.QuizNumber);
                state.AcceptedAnswers ??= new();
                state.RewardItems ??= new();
                state.LootPackNames ??= new();
                if (state.LootPackNames.Count == 0 && !string.IsNullOrWhiteSpace(state.LootPackName)) state.LootPackNames.Add(state.LootPackName);
                state.AttemptsBySteamId = new Dictionary<string, int>(state.AttemptsBySteamId ?? new(), StringComparer.OrdinalIgnoreCase);
                state.PlayerNamesBySteamId = new Dictionary<string, string>(state.PlayerNamesBySteamId ?? new(), StringComparer.OrdinalIgnoreCase);
                state.Winners = new HashSet<string>(state.Winners ?? new(), StringComparer.OrdinalIgnoreCase);
            }
            return store;
        }
        catch
        {
            return new QuizChallengeStoreState();
        }
    }

    private void SaveCore() => File.WriteAllText(_path, JsonSerializer.Serialize(_store, Options));
    private static QuizChallengeState Clone(QuizChallengeState state) =>
        JsonSerializer.Deserialize<QuizChallengeState>(JsonSerializer.Serialize(state, Options), Options) ?? new QuizChallengeState();
    private static LootItem CloneItem(LootItem item) => new() { Item = item.Item, Quantity = Math.Max(1, item.Quantity), DelayMs = Math.Max(0, item.DelayMs) };
    private static string Normalize(string value)
    {
        var normalized = (value ?? string.Empty).Normalize(NormalizationForm.FormKC).Trim();
        return string.Join(' ', normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    }
}
