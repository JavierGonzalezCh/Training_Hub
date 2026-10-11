using GSM.Application.Communications.Abstractions;
using GSM.Domain.Communications;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Communications;

public sealed class WhatsAppTemplateConfiguration : IEntityTypeConfiguration<WhatsAppTemplate>
{
    public void Configure(EntityTypeBuilder<WhatsAppTemplate> builder)
    {
        builder.ToTable("whatsapp_templates", "communications");
        builder.Property(t => t.Kind).HasColumnName("kind");
        builder.Property(t => t.Text).HasMaxLength(WhatsAppTemplateDefaults.MaxLength).IsRequired();
        builder.HasIndex(t => new { t.GymId, t.Kind }).IsUnique();
    }
}

public sealed class WhatsAppContactConfiguration : IEntityTypeConfiguration<WhatsAppContact>
{
    public void Configure(EntityTypeBuilder<WhatsAppContact> builder)
    {
        builder.ToTable("whatsapp_contacts", "communications");
        builder.Property(c => c.Kind).HasColumnName("kind");
        builder.HasIndex(c => new { c.MemberId, c.ContactedAt });
    }
}

public sealed class WhatsAppRepository(GsmDbContext dbContext) : IWhatsAppRepository
{
    private DbSet<WhatsAppTemplate> Templates => dbContext.Set<WhatsAppTemplate>();

    private DbSet<WhatsAppContact> Contacts => dbContext.Set<WhatsAppContact>();

    public async Task<IReadOnlyList<WhatsAppTemplate>> ListTemplatesAsync(CancellationToken cancellationToken = default)
        => await Templates.ToListAsync(cancellationToken);

    public Task<WhatsAppTemplate?> GetTemplateAsync(WhatsAppTemplateKind kind, CancellationToken cancellationToken = default)
        => Templates.FirstOrDefaultAsync(t => t.Kind == kind, cancellationToken);

    public void Add(WhatsAppTemplate template) => Templates.Add(template);

    public void Add(WhatsAppContact contact) => Contacts.Add(contact);

    public async Task<IReadOnlyDictionary<Guid, WhatsAppContact>> GetLastContactsAsync(
        IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken = default)
    {
        if (memberIds.Count == 0)
        {
            return new Dictionary<Guid, WhatsAppContact>();
        }

        var rows = await Contacts.AsNoTracking()
            .Where(c => memberIds.Contains(c.MemberId))
            .OrderByDescending(c => c.ContactedAt)
            .ToListAsync(cancellationToken);
        return rows.GroupBy(c => c.MemberId).ToDictionary(g => g.Key, g => g.First());
    }
}
