using System;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

namespace ScumRconTool.Services;

public sealed class DiscordStatusService : IAsyncDisposable
{
    private readonly Action<string> _log;
    private DiscordSocketClient? _client;
    private TaskCompletionSource<bool>? _readyTcs;

    public bool IsReady => _client?.ConnectionState == ConnectionState.Connected && _client.LoginState == LoginState.LoggedIn;

    public DiscordStatusService(Action<string> log)
    {
        _log = log;
    }

    public async Task StartAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Discord Bot Token fehlt.", nameof(token));
        }

        if (_client is not null && IsReady)
        {
            return;
        }

        await DisposeAsync();

        _readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.None,
            AlwaysDownloadUsers = false,
            LogLevel = LogSeverity.Warning
        });

        _client.Log += message =>
        {
            if (message.Exception is not null)
            {
                _log("Discord: " + message.Exception.Message);
            }
            else if (!string.IsNullOrWhiteSpace(message.Message))
            {
                _log("Discord: " + message.Message);
            }
            return Task.CompletedTask;
        };
        _client.Ready += () =>
        {
            _readyTcs?.TrySetResult(true);
            return Task.CompletedTask;
        };

        await _client.LoginAsync(TokenType.Bot, token.Trim());
        await _client.StartAsync();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        await _readyTcs.Task.WaitAsync(timeout.Token);
    }

    public async Task SetStatusAsync(string text, ActivityType type = ActivityType.Playing)
    {
        if (_client is null || !IsReady)
        {
            throw new InvalidOperationException("Discord Bot ist nicht verbunden.");
        }

        text = string.IsNullOrWhiteSpace(text) ? "SCUM Server" : text.Trim();
        if (text.Length > 128)
        {
            text = text[..128];
        }

        await _client.SetGameAsync(text, type: type);
        await _client.SetStatusAsync(UserStatus.Online);
    }

    public static string FormatStatus(string template, int players, int maxPlayers, DateTime updated)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            template = "SCUM {players}/{max} Spieler online";
        }

        return template
            .Replace("{players}", players.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{max}", maxPlayers.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{updated}", updated.ToString("HH:mm"), StringComparison.OrdinalIgnoreCase);
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
            // Ignore shutdown errors; the app may be closing or the socket may already be gone.
        }
        finally
        {
            _client.Dispose();
            _client = null;
            _readyTcs = null;
        }
    }
}
