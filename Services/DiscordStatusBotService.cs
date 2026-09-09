using Discord;
using Discord.WebSocket;

namespace ScumRconTool.Services;

/// <summary>
/// Deliberately restricted Discord client: it only publishes the player count as its Discord presence.
/// It cannot send messages and has no APIs for weather, challenges, chat logs, events, or the game bridge.
/// </summary>
public sealed class DiscordStatusBotService : IAsyncDisposable
{
    private readonly Action<string> _log;
    private readonly Action<bool>? _connectionChanged;
    private DiscordSocketClient? _client;
    private TaskCompletionSource<bool>? _readyTcs;

    public DiscordStatusBotService(Action<string> log, Action<bool>? connectionChanged = null)
    {
        _log = log;
        _connectionChanged = connectionChanged;
    }

    public bool IsReady => _client?.ConnectionState == ConnectionState.Connected && _client.LoginState == LoginState.LoggedIn;
    public bool IsStarted => _client is not null && _client.LoginState != LoginState.LoggedOut;

    public async Task StartAsync(string token, CancellationToken cancellationToken = default)
    {
        token = (token ?? string.Empty).Trim().Trim('"').Trim('\'');
        if (token.StartsWith("Bot ", StringComparison.OrdinalIgnoreCase)) token = token[4..].Trim();
        if (token.Length == 0) throw new InvalidOperationException("Discord Status-Bot Token fehlt.");
        await DisposeAsync();

        _readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds,
            AlwaysDownloadUsers = false,
            LogLevel = LogSeverity.Info,
            DefaultRetryMode = RetryMode.AlwaysRetry
        });
        _client.Log += message =>
        {
            var text = !string.IsNullOrWhiteSpace(message.Message) ? message.Message : message.Exception?.Message;
            if (!string.IsNullOrWhiteSpace(text)) _log("Discord Status-Bot: " + text);
            return Task.CompletedTask;
        };
        _client.Ready += () =>
        {
            _connectionChanged?.Invoke(true);
            _readyTcs?.TrySetResult(true);
            _log("Discord Status-Bot ist bereit.");
            return Task.CompletedTask;
        };
        _client.Disconnected += exception =>
        {
            _connectionChanged?.Invoke(false);
            if (exception is not null) _log("Discord Status-Bot getrennt: " + exception.Message);
            return Task.CompletedTask;
        };

        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            await _readyTcs.Task.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _log("Discord Status-Bot wartet weiter auf Ready.");
        }
    }

    public async Task SetPlayerCountPresenceAsync(string text)
    {
        if (_client is null || !IsReady) throw new InvalidOperationException("Discord Status-Bot ist nicht verbunden.");
        text = string.IsNullOrWhiteSpace(text) ? "SCUM Server" : text.Trim();
        if (text.Length > 128) text = text[..128];
        await _client.SetGameAsync(text, type: ActivityType.Playing);
        await _client.SetStatusAsync(UserStatus.Online);
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is null) return;
        try
        {
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