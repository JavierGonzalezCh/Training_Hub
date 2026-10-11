using System.Collections.Concurrent;
using GSM.Application.Notifications.Abstractions;

namespace GSM.IntegrationTests;

/// <summary>Push de tests: guarda los mensajes en memoria en vez de hablar con Firebase.</summary>
public sealed class CapturingPushSender : IPushSender
{
    private readonly ConcurrentQueue<PushMessage> _sent = new();

    /// <summary>Tokens que el "proveedor" responde como inválidos (app desinstalada).</summary>
    public ConcurrentDictionary<string, bool> InvalidTokens { get; } = new();

    public bool IsConfigured => true;

    public IReadOnlyList<PushMessage> Sent => _sent.ToList();

    public IReadOnlyList<PushMessage> SentTo(string token) => _sent.Where(m => m.Token == token).ToList();

    public void Clear() => _sent.Clear();

    public Task<PushSendResult> SendAsync(PushMessage message, CancellationToken cancellationToken = default)
    {
        if (InvalidTokens.ContainsKey(message.Token))
        {
            return Task.FromResult(PushSendResult.InvalidToken);
        }

        _sent.Enqueue(message);
        return Task.FromResult(PushSendResult.Sent);
    }
}
