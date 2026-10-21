using System.Net;
using GSM.Application.Common.Abstractions;
using GSM.Application.Identity.Public;
using GSM.Application.Notifications.Abstractions;
using GSM.Application.Notifications.Public;
using GSM.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Notifications;

/// <summary>
/// <see cref="IPlatformAlertNotifier"/>: correo al SuperAdmin y push a sus dispositivos activos. Corre sin usuario
/// (lo llaman el servicio en segundo plano y la tarea periódica). Un dispositivo cuyo token el proveedor ya no
/// reconoce se revoca, igual que en el dispatcher de avisos.
/// </summary>
public sealed class PlatformAlertNotifier(
    IIdentityReader identity,
    IEmailSender emailSender,
    IPushSender pushSender,
    IPushDeviceRepository pushDevices,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<PlatformAlertNotifier> logger) : IPlatformAlertNotifier
{
    private static readonly PushPlatform[] AllPlatforms = [PushPlatform.Android, PushPlatform.WebPanel, PushPlatform.WebMember];

    public async Task<PlatformAlertDeliveryResult> DeliverAsync(
        PlatformAlertMessage message, bool sendEmail, bool sendPush, CancellationToken cancellationToken = default)
    {
        SuperAdminContactDto? contact;
        try
        {
            contact = await identity.GetSuperAdminContactAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudo resolver al SuperAdmin para la alerta {AlertId}", message.AlertId);
            var failed = Describe(ex);
            return new PlatformAlertDeliveryResult(
                sendEmail ? PlatformAlertChannelResult.Failed : PlatformAlertChannelResult.NotAttempted,
                sendPush ? PlatformAlertChannelResult.Failed : PlatformAlertChannelResult.NotAttempted,
                failed);
        }

        if (contact is null)
        {
            // Sin SuperAdmin activo no hay a quién avisar; reintentar no cambiaría nada.
            return new PlatformAlertDeliveryResult(
                sendEmail ? PlatformAlertChannelResult.Skipped : PlatformAlertChannelResult.NotAttempted,
                sendPush ? PlatformAlertChannelResult.Skipped : PlatformAlertChannelResult.NotAttempted,
                null);
        }

        string? error = null;
        var email = PlatformAlertChannelResult.NotAttempted;
        if (sendEmail)
        {
            try
            {
                var (html, text) = Compose(message);
                await emailSender.SendAsync(
                    new EmailMessage(contact.Email, $"[TrainingHub] {message.Title}", html, text), cancellationToken);
                email = PlatformAlertChannelResult.Sent;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Fallo el correo de la alerta {AlertId}", message.AlertId);
                email = PlatformAlertChannelResult.Failed;
                error = Describe(ex);
            }
        }

        var push = PlatformAlertChannelResult.NotAttempted;
        if (sendPush)
        {
            try
            {
                push = await SendPushAsync(message, contact.UserId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Fallo el push de la alerta {AlertId}", message.AlertId);
                push = PlatformAlertChannelResult.Failed;
                error ??= Describe(ex);
            }
        }

        return new PlatformAlertDeliveryResult(email, push, error);
    }

    private async Task<PlatformAlertChannelResult> SendPushAsync(
        PlatformAlertMessage message, Guid userId, CancellationToken cancellationToken)
    {
        if (!pushSender.IsConfigured)
        {
            return PlatformAlertChannelResult.Skipped;
        }

        var devices = await pushDevices.GetActiveForUserAsync(userId, AllPlatforms, cancellationToken);
        if (devices.Count == 0)
        {
            return PlatformAlertChannelResult.Skipped;
        }

        var sent = 0;
        Exception? lastFailure = null;
        var revoked = false;
        foreach (var device in devices)
        {
            try
            {
                var result = await pushSender.SendAsync(
                    new PushMessage(
                        device.Token, device.Platform, message.Title, message.Body, message.Link,
                        message.AlertId, Guid.Empty),
                    cancellationToken);
                switch (result)
                {
                    case PushSendResult.Sent:
                        sent++;
                        break;
                    case PushSendResult.InvalidToken:
                        device.Revoke(clock.UtcNow);
                        revoked = true;
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastFailure = ex;
            }
        }

        if (revoked)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        if (sent > 0)
        {
            return PlatformAlertChannelResult.Sent;
        }

        // Nada salió: si fue por fallas transitorias se reintenta; si todos los tokens estaban muertos, no hay a dónde.
        return lastFailure is not null ? throw lastFailure : PlatformAlertChannelResult.Skipped;
    }

    private static (string Html, string Text) Compose(PlatformAlertMessage message)
    {
        var paragraphs = string.Join(
            string.Empty,
            message.Body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => $"<p>{WebUtility.HtmlEncode(line.TrimEnd('\r'))}</p>"));
        var where = message.Link is null ? string.Empty : $"<p>Revísalo en el panel: <code>{WebUtility.HtmlEncode(message.Link)}</code></p>";
        var html = $"""
            <h2 style="font-size:18px;margin:0 0 8px">{WebUtility.HtmlEncode(message.Title)}</h2>
            {paragraphs}
            {where}
            <hr style="border:none;border-top:1px solid #ddd;margin:24px 0">
            <p style="font-size:12px;color:#666">Alerta automática de TrainingHub para el SuperAdmin.</p>
            """;

        var textWhere = message.Link is null ? string.Empty : "Revísalo en el panel: " + message.Link;
        var text = string.Join(
            "\n\n",
            new[] { message.Title, message.Body, textWhere, "—\nAlerta automática de TrainingHub para el SuperAdmin." }
                .Where(part => part.Length > 0));
        return (html, text);
    }

    private static string Describe(Exception ex)
    {
        var text = $"{ex.GetType().Name}: {ex.Message}";
        return text.Length > 500 ? text[..500] : text;
    }
}
