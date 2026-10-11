using GSM.Domain.Communications;

namespace GSM.Application.Communications.Abstractions;

public interface IWhatsAppRepository
{
    Task<IReadOnlyList<WhatsAppTemplate>> ListTemplatesAsync(CancellationToken cancellationToken = default);

    Task<WhatsAppTemplate?> GetTemplateAsync(WhatsAppTemplateKind kind, CancellationToken cancellationToken = default);

    void Add(WhatsAppTemplate template);

    void Add(WhatsAppContact contact);

    /// <summary>El contacto más reciente de cada miembro de <paramref name="memberIds"/> (los que nunca fueron contactados no aparecen).</summary>
    Task<IReadOnlyDictionary<Guid, WhatsAppContact>> GetLastContactsAsync(
        IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken = default);
}
