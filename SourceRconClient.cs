using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ScumRconTool;

public sealed class SourceRconClient : IAsyncDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _password;

    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private bool _isAuthenticated;
    private bool _disposed;
    private int _requestId = 1000;
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private DateTime _lastActivityUtc = DateTime.MinValue;
    private DateTime _lastCommandUtc = DateTime.MinValue;
    private DateTime _nextReconnectAllowedUtc = DateTime.MinValue;
    private int _consecutiveReconnectFailures;
    private readonly TimeSpan _idleReconnectAfter = TimeSpan.FromMinutes(2);
    private readonly TimeSpan _minimumCommandGap = TimeSpan.FromMilliseconds(900);

    private const int ServerDataAuth = 3;
    private const int ServerDataAuthResponse = 2;
    private const int ServerDataExecCommand = 2;

    public bool IsConnected => !_disposed && _isAuthenticated && _stream is not null && _tcpClient is not null;

    public SourceRconClient(string host, int port, string password)
    {
        _host = host;
        _port = port;
        _password = password;
    }

    public bool Matches(string host, int port, string password)
    {
        return string.Equals(_host, host, StringComparison.OrdinalIgnoreCase)
               && _port == port
               && string.Equals(_password, password, StringComparison.Ordinal);
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        Close();

        _tcpClient = new TcpClient
        {
            NoDelay = true
        };

        await _tcpClient.ConnectAsync(_host, _port, cancellationToken);
        _stream = _tcpClient.GetStream();
    }

    public async Task AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var authId = NextRequestId();
        await SendPacketAsync(authId, ServerDataAuth, _password, cancellationToken);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

        var debug = new StringBuilder();

        try
        {
            for (var i = 0; i < 5; i++)
            {
                var packet = await ReadPacketAsync(timeoutCts.Token);
                debug.AppendLine($"Id={packet.Id}, Type={packet.Type}, Body={packet.Body}");

                if (packet.Type == ServerDataAuthResponse)
                {
                    if (packet.Id == authId)
                    {
                        _isAuthenticated = true;
                        _consecutiveReconnectFailures = 0;
                        _nextReconnectAllowedUtc = DateTime.MinValue;
                        _lastActivityUtc = DateTime.UtcNow;
                        return;
                    }

                    if (packet.Id == -1)
                    {
                        throw new InvalidOperationException("RCON-Authentifizierung fehlgeschlagen: Passwort falsch, Zugriff verweigert oder ggCON Rate-Limit aktiv.");
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("RCON-Authentifizierung: keine gueltige Antwort innerhalb von 5 Sekunden erhalten." + Environment.NewLine + debug);
        }

        throw new InvalidOperationException(
            "RCON-Authentifizierung fehlgeschlagen. Passwort, Port, AllowedIPs oder ggCON Rate-Limit pruefen." +
            Environment.NewLine +
            debug);
    }

    public async Task EnsureConnectedAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _commandLock.WaitAsync(cancellationToken);
        try
        {
            await EnsureConnectedCoreAsync(cancellationToken);
        }
        finally
        {
            _commandLock.Release();
        }
    }

    public async Task<string> SendCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _commandLock.WaitAsync(cancellationToken);
        try
        {
            await EnsureConnectedCoreAsync(cancellationToken);
            await ThrottleCommandAsync(cancellationToken);

            try
            {
                return await SendCommandCoreAsync(command, cancellationToken);
            }
            catch (Exception ex) when (IsConnectionException(ex) && !cancellationToken.IsCancellationRequested)
            {
                Close();
                await EnsureConnectedCoreAsync(cancellationToken, forceReconnect: true);
                await ThrottleCommandAsync(cancellationToken);
                return await SendCommandCoreAsync(command, cancellationToken);
            }
        }
        finally
        {
            _commandLock.Release();
        }
    }

    private async Task EnsureConnectedCoreAsync(CancellationToken cancellationToken, bool forceReconnect = false)
    {
        ThrowIfDisposed();
        var idleTooLong = _lastActivityUtc != DateTime.MinValue &&
                          DateTime.UtcNow - _lastActivityUtc > _idleReconnectAfter;

        if (forceReconnect || !IsConnected || idleTooLong)
        {
            await WaitForReconnectWindowAsync(cancellationToken);
            Close();

            try
            {
                await ConnectAsync(cancellationToken);
                await AuthenticateAsync(cancellationToken);
            }
            catch
            {
                Close();
                RegisterReconnectFailure();
                throw;
            }
        }
    }

    private async Task WaitForReconnectWindowAsync(CancellationToken cancellationToken)
    {
        var wait = _nextReconnectAllowedUtc - DateTime.UtcNow;
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, cancellationToken);
        }
    }

    private void RegisterReconnectFailure()
    {
        _consecutiveReconnectFailures = Math.Min(_consecutiveReconnectFailures + 1, 6);
        var seconds = _consecutiveReconnectFailures switch
        {
            1 => 15,
            2 => 30,
            3 => 60,
            4 => 120,
            _ => 300
        };

        _nextReconnectAllowedUtc = DateTime.UtcNow.AddSeconds(seconds);
    }

    private async Task ThrottleCommandAsync(CancellationToken cancellationToken)
    {
        if (_lastCommandUtc == DateTime.MinValue)
        {
            return;
        }

        var wait = _minimumCommandGap - (DateTime.UtcNow - _lastCommandUtc);
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, cancellationToken);
        }
    }

    private async Task<string> SendCommandCoreAsync(string command, CancellationToken cancellationToken)
    {
        if (_stream is null)
        {
            throw new InvalidOperationException("Nicht verbunden.");
        }

        var commandId = NextRequestId();
        await SendPacketAsync(commandId, ServerDataExecCommand, command, cancellationToken);
        _lastCommandUtc = DateTime.UtcNow;

        var bodies = new List<string>();
        var debugPackets = new List<string>();

        using (var firstCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            firstCts.CancelAfter(TimeSpan.FromSeconds(5));
            var firstPacket = await ReadPacketAsync(firstCts.Token);
            AddPacket(firstPacket, bodies, debugPackets, commandId);
        }

        for (var i = 0; i < 20; i++)
        {
            using var drainCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            drainCts.CancelAfter(TimeSpan.FromMilliseconds(750));

            try
            {
                var packet = await ReadPacketAsync(drainCts.Token);
                AddPacket(packet, bodies, debugPackets, commandId);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        _lastActivityUtc = DateTime.UtcNow;

        if (bodies.Count == 0)
        {
            return string.Join(Environment.NewLine, debugPackets);
        }

        return string.Join(Environment.NewLine, bodies);
    }

    private static bool IsConnectionException(Exception ex)
    {
        return ex is IOException || ex is SocketException ||
               ex.InnerException is IOException || ex.InnerException is SocketException;
    }

    private static void AddPacket(RconPacket packet, List<string> bodies, List<string> debugPackets, int commandId)
    {
        debugPackets.Add($"Id={packet.Id}, Type={packet.Type}, Body={packet.Body}");

        if (packet.Id == commandId && !string.IsNullOrWhiteSpace(packet.Body))
        {
            bodies.Add(packet.Body);
        }
        else if (!string.IsNullOrWhiteSpace(packet.Body) && packet.Id != -1)
        {
            bodies.Add(packet.Body);
        }
    }

    public async Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _commandLock.WaitAsync(cancellationToken);
        try
        {
            await EnsureConnectedCoreAsync(cancellationToken, forceReconnect: true);
        }
        finally
        {
            _commandLock.Release();
        }
    }

    private int NextRequestId()
    {
        return Interlocked.Increment(ref _requestId);
    }

    private async Task SendPacketAsync(int id, int type, string body, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_stream is null)
        {
            throw new InvalidOperationException("Nicht verbunden.");
        }

        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var size = 4 + 4 + bodyBytes.Length + 2;

        await using var ms = new MemoryStream();
        await ms.WriteAsync(BitConverter.GetBytes(size), cancellationToken);
        await ms.WriteAsync(BitConverter.GetBytes(id), cancellationToken);
        await ms.WriteAsync(BitConverter.GetBytes(type), cancellationToken);
        await ms.WriteAsync(bodyBytes, cancellationToken);
        await ms.WriteAsync(new byte[] { 0x00, 0x00 }, cancellationToken);

        var packetBytes = ms.ToArray();
        await _stream.WriteAsync(packetBytes, cancellationToken);
        await _stream.FlushAsync(cancellationToken);
    }

    private async Task<RconPacket> ReadPacketAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_stream is null)
        {
            throw new InvalidOperationException("Nicht verbunden.");
        }

        var sizeBytes = await ReadExactAsync(4, cancellationToken);
        var size = BitConverter.ToInt32(sizeBytes, 0);

        if (size < 10 || size > 1024 * 1024)
        {
            throw new InvalidOperationException($"Ungueltige RCON-Paketgroesse: {size}");
        }

        var packetBytes = await ReadExactAsync(size, cancellationToken);
        var id = BitConverter.ToInt32(packetBytes, 0);
        var type = BitConverter.ToInt32(packetBytes, 4);
        var bodyLength = size - 10;

        var body = bodyLength > 0
            ? Encoding.UTF8.GetString(packetBytes, 8, bodyLength)
            : string.Empty;

        return new RconPacket(id, type, body);
    }

    private async Task<byte[]> ReadExactAsync(int length, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_stream is null)
        {
            throw new InvalidOperationException("Nicht verbunden.");
        }

        var buffer = new byte[length];
        var offset = 0;

        while (offset < length)
        {
            var read = await _stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken);

            if (read == 0)
            {
                throw new IOException("RCON-Verbindung wurde geschlossen.");
            }

            offset += read;
        }

        return buffer;
    }

    public void Close()
    {
        try
        {
            _stream?.Dispose();
            _tcpClient?.Dispose();
        }
        catch
        {
            // Beim Schliessen ignorieren.
        }

        _isAuthenticated = false;
        _lastActivityUtc = DateTime.MinValue;
        _stream = null;
        _tcpClient = null;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        Close();
        _commandLock.Dispose();
        return ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SourceRconClient));
        }
    }

    private sealed record RconPacket(int Id, int Type, string Body);
}
