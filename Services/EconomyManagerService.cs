using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Renci.SshNet;

namespace ScumRconTool.Services;

public sealed class EconomyManagerService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly Regex TradePattern = new(
        @"^(?<time>\d{4}\.\d{2}\.\d{2}-\d{2}\.\d{2}\.\d{2}):.*?\[Trade\] Tradeable \((?<item>.+?) \([^\r\n]*?\)\) (?<kind>purchased|sold) by .*? (?:from|to) trader (?<trader>[^,]+), old amount in store (?:is|was) (?<old>-?\d+), new amount is (?<new>-?\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly BotSettings settings;
    private readonly Action<string> log;
    private readonly bool german;

    public EconomyManagerService(BotSettings settings, Action<string> log, bool german)
    {
        this.settings = settings; this.log = log; this.german = german;
    }

    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScumRconTool", "Economy");
    public static string PreviewFilePath => Path.Combine(DataDirectory, "EconomyOverride.preview.json");
    private static string StateFilePath => Path.Combine(DataDirectory, "economy-state.json");
    public static IReadOnlyList<TimeSpan> ParseSchedule(string? value, bool isGerman) => SettingRandomizerService.ParseSchedule(value, isGerman);
    public static string? GetDueScheduleKey(DateTime now, string? schedule, string? last, bool isGerman) => SettingRandomizerService.GetDueScheduleKey(now, schedule, last, isGerman);

    public async Task<EconomyUpdateResult> AnalyzeAsync(bool upload, CancellationToken token = default)
    {
        Validate();
        await Gate.WaitAsync(token);
        try { return await Task.Run(() => Analyze(upload, token), token); }
        finally { Gate.Release(); }
    }

    private EconomyUpdateResult Analyze(bool upload, CancellationToken token)
    {
        Directory.CreateDirectory(DataDirectory);
        var state = LoadState();
        var (host, port) = ParseHost(settings.FtpHost, settings.FtpPort);
        using var client = new SftpClient(host, port, settings.FtpUser, settings.FtpPassword ?? "");
        client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(30);
        client.OperationTimeout = TimeSpan.FromSeconds(60);
        client.Connect();
        try
        {
            var remote = Remote(settings.EconomyOverrideRemoteFilePath);
            var original = Download(client, remote);
            var hasBom = original.Length > 2 && original[0] == 0xef && original[1] == 0xbb && original[2] == 0xbf;
            var text = Encoding.UTF8.GetString(original, hasBom ? 3 : 0, original.Length - (hasBom ? 3 : 0));
            var root = JsonNode.Parse(text) as JsonObject ?? throw new InvalidOperationException("Invalid EconomyOverride.json.");
            var traders = root["economy-override"]?["traders"] as JsonObject ?? throw new InvalidOperationException("Missing economy-override.traders.");

            var transactions = ReadTransactions(client, state, token);
            foreach (var transaction in transactions.OrderBy(x => x.Timestamp)) AddTrade(state, transaction);

            var now = DateTime.UtcNow;
            var configuredTraders = traders.Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var configuredItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var changes = new List<EconomyPriceChange>();
            var entryCount = 0;
            foreach (var trader in traders)
            {
                if (trader.Value is not JsonArray entries) continue;
                var role = Role(trader.Key);
                foreach (var node in entries)
                {
                    if (node is not JsonObject entry) continue;
                    var itemName = ReadString(entry, "tradeable-code");
                    if (itemName.Length == 0) continue;
                    entryCount++;
                    var key = Key(trader.Key, itemName);
                    configuredItems.Add(key);
                    var buyExists = ReadPrice(entry, "base-purchase-price", out var currentBuy);
                    var sellExists = ReadPrice(entry, "base-sell-price", out var currentSell);
                    if (!buyExists && !sellExists) continue;

                    if (!state.Items.TryGetValue(key, out var item))
                    {
                        item = new EconomyItemState { BaseBuy = currentBuy, BaseSell = currentSell, UpdatedUtc = now };
                        state.Items[key] = item;
                    }
                    else
                    {
                        if (item.BaseBuy == 0 && currentBuy != 0) item.BaseBuy = currentBuy;
                        if (item.BaseSell == 0 && currentSell != 0) item.BaseSell = currentSell;
                    }
                    Decay(item, now, settings.EconomyRecoveryHalfLifeHours);
                    var factor = Math.Clamp(Math.Exp(item.Pressure * RoleFactor(role)), 0.35, Math.Max(1, settings.EconomyMaximumPurchaseMultiplier));
                    var buy = NewPrice(item.BaseBuy, factor, 0);
                    var sellFloor = item.BaseSell > 0 ? Math.Min(item.BaseSell, Math.Max(0, settings.EconomyMinimumSellPrice)) : 0;
                    var sell = NewPrice(item.BaseSell, factor, sellFloor);
                    if (buy > 0) sell = Math.Min(sell, Math.Max(0, Math.Floor(buy * 0.45m)));
                    if (buyExists) WritePrice(entry, "base-purchase-price", buy);
                    if (sellExists) WritePrice(entry, "base-sell-price", sell);
                    if (item.Trades > 0) changes.Add(new(trader.Key, role, itemName, item.Trades, Math.Round((factor - 1) * 100, 1), item.BaseBuy, buy, item.BaseSell, sell));
                }
            }

            var unknownTraders = state.Items.Keys.Select(SplitKey).Where(x => !configuredTraders.Contains(x.Trader))
                .Select(x => x.Trader).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            var unknownItems = state.Items.Keys.Count(x => !configuredItems.Contains(x));
            var updated = new UTF8Encoding(false).GetBytes(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            if (hasBom) updated = Encoding.UTF8.GetPreamble().Concat(updated).ToArray();
            File.WriteAllBytes(PreviewFilePath, updated);
            if (upload) Upload(client, remote, original, updated, token);
            SaveState(state);
            if (upload) log("EconomyOverride.json uploaded; active after the next server restart.");
            var orderedChanges = changes.OrderByDescending(x => Math.Abs(x.ChangePercent)).ToList();
            var marketItems = BuildMarketItems(orderedChanges);
            return new(entryCount, transactions.Count, orderedChanges, marketItems, unknownTraders, unknownItems, PreviewFilePath, upload);
        }
        finally { if (client.IsConnected) client.Disconnect(); }
    }

    private static IReadOnlyList<EconomyMarketItem> BuildMarketItems(IReadOnlyList<EconomyPriceChange> changes)
    {
        return changes
            .GroupBy(x => x.Item, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var buyRows = group.Where(x => x.BasePurchasePrice > 0).ToList();
                var sellRows = group.Where(x => x.BaseSellPrice > 0).ToList();
                var baseBuy = buyRows.Count == 0 ? 0 : buyRows.Average(x => x.BasePurchasePrice);
                var currentBuy = buyRows.Count == 0 ? 0 : buyRows.Average(x => x.NewPurchasePrice);
                var baseSell = sellRows.Count == 0 ? 0 : sellRows.Average(x => x.BaseSellPrice);
                var currentSell = sellRows.Count == 0 ? 0 : sellRows.Average(x => x.NewSellPrice);
                var percent = baseBuy > 0
                    ? (double)((currentBuy / baseBuy - 1m) * 100m)
                    : baseSell > 0 ? (double)((currentSell / baseSell - 1m) * 100m) : 0d;
                return new EconomyMarketItem(
                    group.Key,
                    group.Select(x => x.Trader).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    group.Sum(x => x.Transactions),
                    decimal.Round(baseBuy, 0, MidpointRounding.AwayFromZero),
                    decimal.Round(currentBuy, 0, MidpointRounding.AwayFromZero),
                    decimal.Round(baseSell, 0, MidpointRounding.AwayFromZero),
                    decimal.Round(currentSell, 0, MidpointRounding.AwayFromZero),
                    Math.Round(percent, 1));
            })
            .OrderByDescending(x => Math.Abs(x.ChangePercent))
            .ThenByDescending(x => x.Transactions)
            .ThenBy(x => x.Item, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
    private List<EconomyTransaction> ReadTransactions(SftpClient client, EconomyState state, CancellationToken token)
    {
        var files = client.ListDirectory(Remote(settings.EconomyLogsRemoteDirectory))
            .Where(x => !x.IsDirectory && !x.IsSymbolicLink && Wildcard(x.Name, settings.EconomyLogPattern))
            .OrderByDescending(x => x.LastWriteTimeUtc).Take(64).OrderBy(x => x.LastWriteTimeUtc).ToList();
        var result = new List<EconomyTransaction>();
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            var parsed = ParseTrades(DecodeLog(Download(client, file.FullName)));
            state.Processed.TryGetValue(file.Name, out var count);
            if (count > parsed.Count) count = 0;
            result.AddRange(parsed.Skip(count));
            state.Processed[file.Name] = parsed.Count;
        }
        return result;
    }

    public static List<EconomyTransaction> ParseTrades(string text)
    {
        var result = new List<EconomyTransaction>();
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = TradePattern.Match(line);
            if (!match.Success || !DateTime.TryParseExact(match.Groups["time"].Value, "yyyy.MM.dd-HH.mm.ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var time)) continue;
            long.TryParse(match.Groups["old"].Value, out var oldValue);
            long.TryParse(match.Groups["new"].Value, out var newValue);
            var quantity = oldValue == int.MaxValue || newValue == int.MaxValue ? 1 : (int)Math.Clamp(Math.Abs(newValue - oldValue), 1, 1000);
            result.Add(new(time.ToUniversalTime(), match.Groups["trader"].Value.Trim(), match.Groups["item"].Value.Trim(),
                match.Groups["kind"].Value.Equals("purchased", StringComparison.OrdinalIgnoreCase), quantity));
        }
        return result;
    }

    public static string DecodeLog(byte[] bytes)
    {
        if (bytes.Length > 1 && bytes[0] == 0xff && bytes[1] == 0xfe) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        var sample = Math.Min(bytes.Length, 256);
        var nulls = Enumerable.Range(0, sample / 2).Count(x => bytes[x * 2 + 1] == 0);
        return sample >= 8 && nulls >= sample / 8 ? Encoding.Unicode.GetString(bytes) : Encoding.UTF8.GetString(bytes);
    }

    private void AddTrade(EconomyState state, EconomyTransaction trade)
    {
        var key = Key(trade.Trader, trade.Item);
        if (!state.Items.TryGetValue(key, out var item)) state.Items[key] = item = new() { UpdatedUtc = trade.Timestamp };
        Decay(item, trade.Timestamp, settings.EconomyRecoveryHalfLifeHours);
        var amount = Math.Min(5, Math.Sqrt(Math.Max(1, trade.Quantity)));
        item.Pressure += trade.IsPurchase
            ? Math.Log(1 + Math.Max(0, settings.EconomyPurchaseImpactPercent) / 100) * amount
            : -Math.Log(1 + Math.Max(0, settings.EconomySaleImpactPercent) / 100) * amount;
        item.Trades++;
    }

    private static void Decay(EconomyItemState item, DateTime now, double halfLife)
    {
        if (item.UpdatedUtc == default) { item.UpdatedUtc = now; return; }
        var hours = Math.Max(0, (now - item.UpdatedUtc).TotalHours);
        if (hours > 0) item.Pressure *= Math.Pow(0.5, hours / halfLife);
        item.UpdatedUtc = now;
    }

    private static decimal NewPrice(decimal basis, double factor, decimal floor) =>
        basis <= 0 ? basis : Math.Max(floor, decimal.Round(basis * (decimal)factor, 0, MidpointRounding.AwayFromZero));
    private static string Role(string trader)
    {
        var first = trader.IndexOf('_'); var second = first < 0 ? -1 : trader.IndexOf('_', first + 1);
        return second < 0 ? trader : trader[(second + 1)..];
    }
    private static double RoleFactor(string role) => role.ToLowerInvariant() switch
    {
        "armory" => 1.2, "mechanic" or "master_hunter" => 1.1, "hospital" => 0.85,
        "trader" => 0.75, "saloon" => 0.6, "barber" => 0.4, _ => 1.0
    };

    private void Validate()
    {
        var (host, _) = ParseHost(settings.FtpHost, settings.FtpPort);
        if (host.Length == 0 || string.IsNullOrWhiteSpace(settings.FtpUser)) throw new InvalidOperationException(german ? "SFTP-Zugangsdaten fehlen." : "SFTP credentials are missing.");
        if (settings.EconomyPollMinutes < 1 || settings.EconomyRecoveryHalfLifeHours < 1 || settings.EconomyMaximumPurchaseMultiplier < 1)
            throw new InvalidOperationException(german ? "Economy-Zahlenwerte sind ungueltig." : "Economy numeric settings are invalid.");
        if (settings.EconomyAutoUpload) ParseSchedule(settings.EconomyUploadTimes, german);
    }

    private static byte[] Download(SftpClient client, string path) { using var stream = new MemoryStream(); client.DownloadFile(path, stream); return stream.ToArray(); }
    private static void Upload(SftpClient client, string path, byte[] original, byte[] updated, CancellationToken token)
    {
        var temp = path + ".rrrt-" + Guid.NewGuid().ToString("N") + ".tmp";
        using (var backup = new MemoryStream(original, false)) client.UploadFile(backup, path + ".rrrt-backup", true);
        try
        {
            using (var stream = new MemoryStream(updated, false)) client.UploadFile(stream, temp, true);
            token.ThrowIfCancellationRequested();
            try { client.RenameFile(temp, path, true); }
            catch { using var stream = new MemoryStream(updated, false); client.UploadFile(stream, path, true); }
        }
        finally { try { if (client.Exists(temp)) client.DeleteFile(temp); } catch { } }
    }

    private static string ReadString(JsonObject obj, string name) { try { return obj[name]?.GetValue<string>() ?? ""; } catch { return ""; } }
    private static bool ReadPrice(JsonObject obj, string name, out decimal value)
    {
        value = 0;
        try
        {
            if (obj[name] is JsonValue node && node.TryGetValue<string>(out var text)) return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            return obj[name] is JsonValue number && number.TryGetValue(out value);
        }
        catch { return false; }
    }
    private static void WritePrice(JsonObject obj, string name, decimal value)
    {
        if (obj[name] is JsonValue node && node.TryGetValue<string>(out _)) obj[name] = value.ToString("0", CultureInfo.InvariantCulture); else obj[name] = value;
    }
    private static bool Wildcard(string file, string pattern) => Regex.IsMatch(file, "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase);
    private static string Remote(string? path) { var value = (path ?? "").Trim().Replace('\\', '/'); return value.StartsWith('/') ? value : "/" + value; }
    private static (string Host, int Port) ParseHost(string? input, int port)
    {
        var host = (input ?? "").Trim(); port = port > 0 ? port : 22;
        var colon = host.LastIndexOf(':');
        if (colon > 0 && host.Count(x => x == ':') == 1 && int.TryParse(host[(colon + 1)..], out var parsed)) { port = parsed; host = host[..colon]; }
        return (host.TrimEnd('/'), port);
    }
    private static string Key(string trader, string item) => trader + "\u001f" + item;
    private static (string Trader, string Item) SplitKey(string key) { var i = key.IndexOf('\u001f'); return i < 0 ? (key, "") : (key[..i], key[(i + 1)..]); }
    private static EconomyState LoadState() { try { return File.Exists(StateFilePath) ? JsonSerializer.Deserialize<EconomyState>(File.ReadAllText(StateFilePath)) ?? new() : new(); } catch { return new(); } }
    private static void SaveState(EconomyState state)
    {
        var temp = StateFilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        File.Move(temp, StateFilePath, true);
    }
}

public sealed class EconomyState
{
    public Dictionary<string, int> Processed { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, EconomyItemState> Items { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
public sealed class EconomyItemState
{
    public decimal BaseBuy { get; set; }
    public decimal BaseSell { get; set; }
    public double Pressure { get; set; }
    public long Trades { get; set; }
    public DateTime UpdatedUtc { get; set; }
}
public sealed record EconomyTransaction(DateTime Timestamp, string Trader, string Item, bool IsPurchase, int Quantity);
public sealed record EconomyPriceChange(string Trader, string TraderRole, string Item, long Transactions, double ChangePercent,
    decimal BasePurchasePrice, decimal NewPurchasePrice, decimal BaseSellPrice, decimal NewSellPrice)
{
    public string ChangeDisplay => $"{ChangePercent:+0.0;-0.0;0.0}%";
    public string PurchaseDisplay => string.Format(CultureInfo.CurrentCulture, "{0:0} -> {1:0}", BasePurchasePrice, NewPurchasePrice);
    public string SellDisplay => string.Format(CultureInfo.CurrentCulture, "{0:0} -> {1:0}", BaseSellPrice, NewSellPrice);
}
public sealed record EconomyMarketItem(string Item, int TraderCount, long Transactions,
    decimal BasePurchasePrice, decimal CurrentPurchasePrice, decimal BaseSellPrice, decimal CurrentSellPrice, double ChangePercent);

public sealed record EconomyUpdateResult(int OverrideItemCount, int NewTransactionCount, IReadOnlyList<EconomyPriceChange> Changes,
    IReadOnlyList<EconomyMarketItem> MarketItems, IReadOnlyList<string> UnknownTraders, int UnknownItemCount, string PreviewFilePath, bool Uploaded);
