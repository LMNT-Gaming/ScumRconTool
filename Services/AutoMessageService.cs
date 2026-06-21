namespace ScumRconTool.Services;

public sealed class AutoMessageService
{
    private readonly Action<string> _log;
    private int _nextStepIndex;
    private DateTime _nextQueueUtc = DateTime.MinValue;
    private readonly Dictionary<string, DateTime> _nextStandaloneUtc = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _cts;

    public AutoMessageService(Action<string> log)
    {
        _log = log;
    }

    public bool IsRunning { get; private set; }

    public void Start(
        BotSettings settings,
        Func<CancellationToken, Task<int>> getOnlinePlayersAsync,
        Func<CancellationToken, Task<string>> buildChallengeTextAsync,
        Func<string, string, CancellationToken, Task> sendBroadcastAsync)
    {
        Stop();
        IsRunning = true;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _nextQueueUtc = DateTime.UtcNow.AddMinutes(GetQueueIntervalMinutes(settings));

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), token);
                    await TickScheduledAsync(settings, getOnlinePlayersAsync, buildChallengeTextAsync, sendBroadcastAsync, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _log("Auto Messages Fehler: " + ex.Message);
                    AppLogService.WriteException("AutoMessages", ex);
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

    private async Task TickScheduledAsync(
        BotSettings settings,
        Func<CancellationToken, Task<int>> getOnlinePlayersAsync,
        Func<CancellationToken, Task<string>> buildChallengeTextAsync,
        Func<string, string, CancellationToken, Task> sendBroadcastAsync,
        CancellationToken cancellationToken)
    {
        var steps = AutoMessageFlow.Parse(settings.AutoMessagesFlowJson, settings.AutoMessagesBroadcastType).ToList();
        if (steps.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var queueSteps = steps.Where(x => !AutoMessageFlow.IsStandalone(x.Mode)).ToList();
        if (queueSteps.Count > 0 && now >= _nextQueueUtc)
        {
            await SendNextQueueStepAsync(settings, queueSteps, getOnlinePlayersAsync, buildChallengeTextAsync, sendBroadcastAsync, cancellationToken);
            _nextQueueUtc = now.AddMinutes(GetQueueIntervalMinutes(settings));
        }

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            if (!AutoMessageFlow.IsStandalone(step.Mode))
            {
                continue;
            }

            var key = BuildStandaloneKey(i, step);
            var interval = Math.Max(1, step.IntervalMinutes);
            if (!_nextStandaloneUtc.TryGetValue(key, out var due))
            {
                _nextStandaloneUtc[key] = now.AddMinutes(interval);
                continue;
            }

            if (now < due)
            {
                continue;
            }

            await SendStepAsync(settings, step, getOnlinePlayersAsync, buildChallengeTextAsync, sendBroadcastAsync, cancellationToken);
            _nextStandaloneUtc[key] = now.AddMinutes(interval);
        }
    }

    public async Task TickAsync(
        BotSettings settings,
        Func<CancellationToken, Task<int>> getOnlinePlayersAsync,
        Func<CancellationToken, Task<string>> buildChallengeTextAsync,
        Func<string, string, CancellationToken, Task> sendBroadcastAsync,
        CancellationToken cancellationToken = default)
    {
        var steps = AutoMessageFlow.Parse(settings.AutoMessagesFlowJson, settings.AutoMessagesBroadcastType).ToList();
        if (steps.Count == 0)
        {
            _log("Auto Messages: keine Flow-Schritte konfiguriert.");
            return;
        }

        var queueSteps = steps.Where(x => !AutoMessageFlow.IsStandalone(x.Mode)).ToList();
        if (queueSteps.Count == 0)
        {
            await SendStepAsync(settings, steps[0], getOnlinePlayersAsync, buildChallengeTextAsync, sendBroadcastAsync, cancellationToken);
            return;
        }

        await SendNextQueueStepAsync(settings, queueSteps, getOnlinePlayersAsync, buildChallengeTextAsync, sendBroadcastAsync, cancellationToken);
    }

    private async Task SendNextQueueStepAsync(
        BotSettings settings,
        List<AutoMessageStep> queueSteps,
        Func<CancellationToken, Task<int>> getOnlinePlayersAsync,
        Func<CancellationToken, Task<string>> buildChallengeTextAsync,
        Func<string, string, CancellationToken, Task> sendBroadcastAsync,
        CancellationToken cancellationToken)
    {
        if (_nextStepIndex < 0 || _nextStepIndex >= queueSteps.Count) _nextStepIndex = 0;
        var step = queueSteps[_nextStepIndex];
        _nextStepIndex = (_nextStepIndex + 1) % queueSteps.Count;

        await SendStepAsync(settings, step, getOnlinePlayersAsync, buildChallengeTextAsync, sendBroadcastAsync, cancellationToken);
    }

    private async Task SendStepAsync(
        BotSettings settings,
        AutoMessageStep step,
        Func<CancellationToken, Task<int>> getOnlinePlayersAsync,
        Func<CancellationToken, Task<string>> buildChallengeTextAsync,
        Func<string, string, CancellationToken, Task> sendBroadcastAsync,
        CancellationToken cancellationToken)
    {
        if (settings.AutoMessagesOnlyWhenPlayersOnline)
        {
            var online = await getOnlinePlayersAsync(cancellationToken);
            if (online <= 0)
            {
                _log("Auto Messages: keine Spieler online, Nachricht uebersprungen.");
                return;
            }
        }

        var messageType = string.IsNullOrWhiteSpace(step.MessageType) ? settings.AutoMessagesBroadcastType : step.MessageType;
        string text;

        if (AutoMessageFlow.IsChallengeStep(step.Type))
        {
            text = await buildChallengeTextAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(text))
            {
                text = settings.AutoMessagesNoChallengeText;
            }
        }
        else
        {
            text = step.Text;
        }

        text = NormalizeText(text, settings.AutoMessagesMaxLength);
        if (string.IsNullOrWhiteSpace(text))
        {
            _log("Auto Messages: leerer Flow-Schritt uebersprungen.");
            return;
        }

        await sendBroadcastAsync(messageType, text, cancellationToken);
        _log($"Auto Messages gesendet: {messageType} {text}");
    }

    public void ResetFlow()
    {
        _nextStepIndex = 0;
        _nextQueueUtc = DateTime.UtcNow;
        _nextStandaloneUtc.Clear();
        _log("Auto Messages Flow auf Anfang gesetzt.");
    }

    private static int GetQueueIntervalMinutes(BotSettings settings) =>
        Math.Max(1, settings.AutoMessagesIntervalMinutes <= 0 ? 15 : settings.AutoMessagesIntervalMinutes);

    private static string BuildStandaloneKey(int index, AutoMessageStep step) =>
        index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + step.Type + "|" + step.MessageType + "|" + step.Text;

    private static string NormalizeText(string text, int maxLength)
    {
        text = (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        maxLength = maxLength <= 0 ? 180 : maxLength;
        return text.Length <= maxLength ? text : text[..maxLength];
    }
}
