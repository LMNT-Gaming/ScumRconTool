namespace ScumRconTool.Services;

public sealed class AutoMessageService
{
    private readonly Action<string> _log;
    private int _nextStepIndex;
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

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var minutes = Math.Max(1, settings.AutoMessagesIntervalMinutes <= 0 ? 15 : settings.AutoMessagesIntervalMinutes);
                    await Task.Delay(TimeSpan.FromMinutes(minutes), token);
                    await TickAsync(settings, getOnlinePlayersAsync, buildChallengeTextAsync, sendBroadcastAsync, token);
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

    public async Task TickAsync(
        BotSettings settings,
        Func<CancellationToken, Task<int>> getOnlinePlayersAsync,
        Func<CancellationToken, Task<string>> buildChallengeTextAsync,
        Func<string, string, CancellationToken, Task> sendBroadcastAsync,
        CancellationToken cancellationToken = default)
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

        var steps = AutoMessageFlow.Parse(settings.AutoMessagesFlowJson, settings.AutoMessagesBroadcastType).ToList();
        if (steps.Count == 0)
        {
            _log("Auto Messages: keine Flow-Schritte konfiguriert.");
            return;
        }

        if (_nextStepIndex < 0 || _nextStepIndex >= steps.Count) _nextStepIndex = 0;
        var step = steps[_nextStepIndex];
        _nextStepIndex = (_nextStepIndex + 1) % steps.Count;

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
        _log("Auto Messages Flow auf Anfang gesetzt.");
    }

    private static string NormalizeText(string text, int maxLength)
    {
        text = (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        maxLength = maxLength <= 0 ? 180 : maxLength;
        return text.Length <= maxLength ? text : text[..maxLength];
    }
}
