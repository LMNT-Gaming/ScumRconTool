using System.Security.Cryptography;
using System.Text.Json;
using ScumRconTool.Models;

namespace ScumRconTool.Services;

public sealed class WeeklyRewardClaim
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CompletionKey { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string TaskTitle { get; set; } = string.Empty;
    public string SquadId { get; set; } = string.Empty;
    public string SquadName { get; set; } = string.Empty;
    public string SteamId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string RewardMode { get; set; } = "FreeText";
    public string RewardText { get; set; } = string.Empty;
    public string RewardItem { get; set; } = string.Empty;
    public int RewardItemQuantity { get; set; } = 1;
    public int RewardItemStackCount { get; set; }
    public List<WeeklyRewardClaimItem> RewardItems { get; set; } = new();
    public int RewardMoney { get; set; }
    public int RewardFame { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? NotifiedUtc { get; set; }
    public DateTime? ItemDeliveredUtc { get; set; }
    public DateTime? MoneyDeliveredUtc { get; set; }
    public DateTime? FameDeliveredUtc { get; set; }
    public DateTime? TextClaimedUtc { get; set; }
    public DateTime? ClaimedUtc { get; set; }
    public DateTime? AdminAcknowledgedUtc { get; set; }
    public string LastError { get; set; } = string.Empty;

    public List<WeeklyRewardClaimItem> GetOrCreateRewardItems()
    {
        RewardItems ??= new List<WeeklyRewardClaimItem>();
        if (RewardItems.Count == 0 && !string.IsNullOrWhiteSpace(RewardItem))
        {
            RewardItems.Add(new WeeklyRewardClaimItem
            {
                Item = RewardItem.Trim(),
                Quantity = Math.Max(1, RewardItemQuantity),
                StackCount = Math.Max(0, RewardItemStackCount),
                DeliveredUtc = ItemDeliveredUtc
            });
        }

        return RewardItems;
    }

    public bool NeedsItem => GetOrCreateRewardItems().Any(x => !string.IsNullOrWhiteSpace(x.Item));
    public bool NeedsMoney => RewardMoney > 0;
    public bool NeedsFame => RewardFame > 0;
    public bool NeedsText => !NeedsItem && !string.IsNullOrWhiteSpace(RewardText);
    public bool IsComplete => (!NeedsItem || GetOrCreateRewardItems().Where(x => !string.IsNullOrWhiteSpace(x.Item)).All(x => x.DeliveredUtc.HasValue)) && (!NeedsMoney || MoneyDeliveredUtc.HasValue) && (!NeedsFame || FameDeliveredUtc.HasValue) && (!NeedsText || TextClaimedUtc.HasValue);
    public string RewardSummary => string.Join(" + ", GetOrCreateRewardItems()
        .Where(x => NeedsItem && !string.IsNullOrWhiteSpace(x.Item))
        .Select(x => $"{Math.Max(1, x.Quantity)}x {x.Item}" + (x.StackCount > 0 ? $" (Stack {x.StackCount})" : string.Empty))
        .Concat(new[] { NeedsMoney ? $"{RewardMoney}$" : string.Empty, NeedsFame ? $"⭐ {RewardFame} Fame" : string.Empty, NeedsText ? RewardText : string.Empty })
        .Where(x => !string.IsNullOrWhiteSpace(x)));
}

public sealed class WeeklyRewardClaimItem
{
    public string Item { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public int StackCount { get; set; }
    public DateTime? DeliveredUtc { get; set; }
}

public sealed class WeeklyRewardState
{
    public List<WeeklyRewardClaim> Claims { get; set; } = new();
    public HashSet<string> GeneratedCompletionKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> DeactivatedCompletionKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> CompletedCompletionKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class WeeklyRewardStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly object _sync = new();
    private readonly string _path;
    private WeeklyRewardState _state;

    public WeeklyRewardStore()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScumRconTool", "State");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "weekly-reward-claims.json");
        _state = Load();
        if (ArchiveCompletedClaimsCore() > 0) SaveCore();
    }

    public IReadOnlyList<WeeklyRewardClaim> GetAll()
    {
        lock (_sync) return _state.Claims.OrderByDescending(x => x.CreatedUtc).ToList();
    }

    public bool HasPendingClaims()
    {
        lock (_sync) return _state.Claims.Any(x => !x.IsComplete);
    }

    public WeeklyRewardClaim? FindByCode(string code)
    {
        lock (_sync) return _state.Claims.FirstOrDefault(x => !x.IsComplete && x.Code.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<WeeklyRewardClaim> GetPendingFor(IEnumerable<string> steamIds)
    {
        var ids = steamIds.Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (_sync) return _state.Claims.Where(x => !x.IsComplete && ids.Contains(x.SteamId)).ToList();
    }

    public IReadOnlyList<WeeklyRewardClaim> GetPendingForSteamId(string steamId)
    {
        var id = (steamId ?? string.Empty).Trim();
        if (id.Length == 0) return Array.Empty<WeeklyRewardClaim>();
        lock (_sync) return _state.Claims
            .Where(x => !x.IsComplete && x.SteamId.Equals(id, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.CreatedUtc)
            .ToList();
    }

    public bool Remove(string claimId)
    {
        lock (_sync)
        {
            var claim = _state.Claims.FirstOrDefault(x => x.Id.Equals(claimId, StringComparison.OrdinalIgnoreCase));
            if (claim is null) return false;
            _state.Claims.Remove(claim);
            ArchiveCompletionKeyIfFinished(claim.CompletionKey);
            SaveCore();
            return true;
        }
    }

    public int ClearInactiveCodes(IEnumerable<WeeklyCommunityTaskDefinition> definitions, DateTime nowUtc)
    {
        var definitionsById = definitions
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        lock (_sync)
        {
            var removable = _state.Claims
                .Where(claim => !claim.IsComplete && !IsClaimCardEnabled(claim, definitionsById))
                .ToList();
            foreach (var claim in removable)
            {
                if (!string.IsNullOrWhiteSpace(claim.CompletionKey)) _state.DeactivatedCompletionKeys.Add(claim.CompletionKey);
            }

            var removableIds = removable.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var removed = _state.Claims.RemoveAll(claim => removableIds.Contains(claim.Id));
            if (removed > 0) SaveCore();
            return removed;
        }
    }

    private static bool IsClaimCardEnabled(WeeklyRewardClaim claim, IReadOnlyDictionary<string, WeeklyCommunityTaskDefinition> definitions)
    {
        if (claim.TaskId.StartsWith("quiz-", StringComparison.OrdinalIgnoreCase)) return true;
        return definitions.TryGetValue(claim.TaskId, out var definition) && definition.Enabled;
    }
    public bool EnsureClaims(WeeklyCommunityTaskProgress progress, IReadOnlyList<GgconSquadResponse> squads, Action<string> log)
    {
        var definition = progress.Definition;
        var completionKey = $"{definition.Id}|{progress.Baseline.CreatedUtc:O}";
        lock (_sync)
        {
            if (_state.DeactivatedCompletionKeys.Contains(completionKey)) return false;
            if (_state.CompletedCompletionKeys.Contains(completionKey)) return false;

            if (_state.GeneratedCompletionKeys.Contains(completionKey))
            {
                if (_state.Claims.Any(x => x.CompletionKey.Equals(completionKey, StringComparison.OrdinalIgnoreCase))) return false;
                _state.GeneratedCompletionKeys.Remove(completionKey);
                log($"Weekly Reward '{definition.Title}': Leeren Abschlussmarker repariert; Empfaenger werden erneut ermittelt.");
            }

            var eligible = progress.SquadProgress.Where(x => x.IsSuccessfulParticipant).ToList();
            if (eligible.Count == 0)
            {
                log($"Weekly Reward '{definition.Title}': Noch kein qualifiziertes Squad im eingefrorenen Abschlussstand; kein leerer Abschlussmarker wird gespeichert.");
                return false;
            }
            var plannedClaims = new List<WeeklyRewardClaim>();
            foreach (var squadProgress in eligible)
            {
                var squad = squads.FirstOrDefault(x => x.Id.ToString().Equals(squadProgress.SquadId, StringComparison.OrdinalIgnoreCase))
                    ?? squads.FirstOrDefault(x => x.Name.Equals(squadProgress.SquadName, StringComparison.OrdinalIgnoreCase));
                if (squad is null)
                {
                    log($"Weekly Reward '{definition.Title}': Squad nicht in ggCON gefunden: {squadProgress.SquadName} ({squadProgress.SquadId}).");
                    return false;
                }

                IEnumerable<GgconSquadMemberResponse> recipients = squad.Members
                    .Where(x => !string.IsNullOrWhiteSpace(x.SteamId))
                    .GroupBy(x => x.SteamId.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.First());
                if (definition.RewardDistribution.Equals("PerSquad", StringComparison.OrdinalIgnoreCase))
                {
                    recipients = recipients.OrderByDescending(x => x.Rank).Take(1);
                }

                var recipientList = recipients.ToList();
                if (recipientList.Count == 0)
                {
                    log($"Weekly Reward '{definition.Title}': Keine Spieler für Squad '{squad.Name}' von ggCON geliefert; Erzeugung wird später erneut versucht.");
                    return false;
                }

                foreach (var member in recipientList)
                {
                    plannedClaims.Add(new WeeklyRewardClaim
                    {
                        CompletionKey = completionKey,
                        TaskId = definition.Id,
                        TaskTitle = definition.Title,
                        SquadId = squad.Id.ToString(),
                        SquadName = squad.Name,
                        SteamId = member.SteamId.Trim(),
                        PlayerName = string.IsNullOrWhiteSpace(member.CharacterName) ? member.SteamId : member.CharacterName,
                        Code = CreateUniqueCode(_state.Claims.Concat(plannedClaims)),
                        RewardMode = string.IsNullOrWhiteSpace(definition.RewardMode) ? "FreeText" : definition.RewardMode,
                        RewardText = string.Empty,
                        RewardItem = definition.RewardItem ?? string.Empty,
                        RewardItemQuantity = Math.Max(1, definition.RewardItemQuantity),
                        RewardItemStackCount = Math.Max(0, definition.RewardItemStackCount),
                        RewardItems = CreateRandomRewardItems(definition),
                        RewardMoney = Math.Max(0, definition.RewardMoney),
                        RewardFame = Math.Max(0, definition.RewardFame)
                    });
                }
            }

            _state.Claims.AddRange(plannedClaims);
            _state.GeneratedCompletionKeys.Add(completionKey);
            SaveCore();
            return true;
        }
    }

    public bool EnsurePlayerClaims(WeeklyCommunityTaskProgress progress, Action<string> log)
    {
        var definition = progress.Definition;
        var created = false;
        lock (_sync)
        {
            foreach (var player in progress.PlayerProgress.Where(x => x.IsCompleted && !string.IsNullOrWhiteSpace(x.SteamId)))
            {
                var completionKey = $"{definition.Id}|{progress.Baseline.CreatedUtc:O}|player:{player.SteamId.Trim()}";
                if (_state.GeneratedCompletionKeys.Contains(completionKey) || _state.CompletedCompletionKeys.Contains(completionKey)) continue;

                _state.Claims.Add(new WeeklyRewardClaim
                {
                    CompletionKey = completionKey,
                    TaskId = definition.Id,
                    TaskTitle = definition.Title,
                    SteamId = player.SteamId.Trim(),
                    PlayerName = string.IsNullOrWhiteSpace(player.PlayerName) ? player.SteamId : player.PlayerName,
                    Code = CreateUniqueCode(_state.Claims),
                    RewardMode = string.IsNullOrWhiteSpace(definition.RewardMode) ? "FreeText" : definition.RewardMode,
                    RewardText = string.Empty,
                    RewardItem = definition.RewardItem ?? string.Empty,
                    RewardItemQuantity = Math.Max(1, definition.RewardItemQuantity),
                    RewardItemStackCount = Math.Max(0, definition.RewardItemStackCount),
                    RewardItems = CreateRandomRewardItems(definition),
                    RewardMoney = Math.Max(0, definition.RewardMoney),
                        RewardFame = Math.Max(0, definition.RewardFame)
                });
                _state.GeneratedCompletionKeys.Add(completionKey);
                created = true;
                log($"Weekly Reward '{definition.Title}': Pro-Spieler-Code fuer {player.PlayerName} ({player.SteamId}) erstellt.");
            }

            if (created) SaveCore();
            return created;
        }
    }

    private static List<WeeklyRewardClaimItem> CreateRandomRewardItems(WeeklyCommunityTaskDefinition definition) =>
        WeeklyRewardItems.GetRandomConfigured(definition)
            .Select(item => new WeeklyRewardClaimItem
            {
                Item = item.Item,
                Quantity = item.Quantity,
                StackCount = item.StackCount
            }).ToList();
    public WeeklyRewardClaim? CreateLootPackClaim(
        string completionKey,
        string taskId,
        string taskTitle,
        string steamId,
        string playerName,
        IEnumerable<LootItem> items,
        int rewardMoney = 0,
        int rewardFame = 0)
    {
        lock (_sync)
        {
            var existing = _state.Claims.FirstOrDefault(x => x.CompletionKey.Equals(completionKey, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) return existing;
            if (_state.CompletedCompletionKeys.Contains(completionKey)) return null;

            var rewardItems = items
                .Where(x => !string.IsNullOrWhiteSpace(x.Item))
                .Select(x => new WeeklyRewardClaimItem { Item = x.Item.Trim(), Quantity = Math.Max(1, x.Quantity) })
                .ToList();
            if (rewardItems.Count == 0 && rewardMoney <= 0 && rewardFame <= 0) return null;

            var claim = new WeeklyRewardClaim
            {
                CompletionKey = completionKey,
                TaskId = taskId,
                TaskTitle = taskTitle,
                SteamId = steamId.Trim(),
                PlayerName = string.IsNullOrWhiteSpace(playerName) ? steamId.Trim() : playerName.Trim(),
                Code = CreateUniqueCode(_state.Claims),
                RewardMode = "Item",
                RewardItems = rewardItems,
                RewardMoney = Math.Max(0, rewardMoney),
                RewardFame = Math.Max(0, rewardFame)
            };
            _state.Claims.Add(claim);
            _state.GeneratedCompletionKeys.Add(completionKey);
            SaveCore();
            return claim;
        }
    }
    public void Save()
    {
        lock (_sync) SaveCore();
    }

    private WeeklyRewardState Load()
    {
        try
        {
            if (!File.Exists(_path)) return new WeeklyRewardState();
            var state = JsonSerializer.Deserialize<WeeklyRewardState>(File.ReadAllText(_path), Options) ?? new WeeklyRewardState();
            state.Claims ??= new List<WeeklyRewardClaim>();
            foreach (var claim in state.Claims) claim.GetOrCreateRewardItems();
            state.GeneratedCompletionKeys = new HashSet<string>(state.GeneratedCompletionKeys ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
            state.DeactivatedCompletionKeys = new HashSet<string>(state.DeactivatedCompletionKeys ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
            state.CompletedCompletionKeys = new HashSet<string>(state.CompletedCompletionKeys ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
            return state;
        }
        catch
        {
            return new WeeklyRewardState();
        }
    }

    private void SaveCore()
    {
        File.WriteAllText(_path, JsonSerializer.Serialize(_state, Options));
    }

    private int ArchiveCompletedClaimsCore()
    {
        var completed = _state.Claims.Where(x => x.IsComplete).ToList();
        if (completed.Count == 0) return 0;
        foreach (var claim in completed) _state.Claims.Remove(claim);
        foreach (var key in completed.Select(x => x.CompletionKey).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ArchiveCompletionKeyIfFinished(key);
        }
        return completed.Count;
    }

    private void ArchiveCompletionKeyIfFinished(string completionKey)
    {
        if (string.IsNullOrWhiteSpace(completionKey)) return;
        if (_state.Claims.Any(x => x.CompletionKey.Equals(completionKey, StringComparison.OrdinalIgnoreCase))) return;
        _state.CompletedCompletionKeys.Add(completionKey);
    }

    private static string CreateUniqueCode(IEnumerable<WeeklyRewardClaim> claims)
    {
        var existing = claims.Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string code;
        do
        {
            code = BuiltinChatCommandCatalog.RewardPrefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
        } while (existing.Contains(code));
        return code;
    }
}
