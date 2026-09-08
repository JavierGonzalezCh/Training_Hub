using System.Collections.Concurrent;
using GSM.Application.Common.Abstractions;

namespace GSM.IntegrationTests;

/// <summary>Sender de tests: guarda los mensajes en memoria en vez de enviarlos.</summary>
public sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public IReadOnlyList<EmailMessage> Sent => _sent.ToList();

    public EmailMessage? LastTo(string address)
        => _sent.LastOrDefault(m => string.Equals(m.To, address, StringComparison.OrdinalIgnoreCase));

    public void Clear() => _sent.Clear();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }
}
