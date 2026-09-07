using GSM.Domain.Members;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Members;

public sealed class MemberInvitationConfiguration : IEntityTypeConfiguration<MemberInvitation>
{
    public void Configure(EntityTypeBuilder<MemberInvitation> builder)
    {
        builder.ToTable("member_invitations", "members");

        builder.Property(i => i.Code).HasMaxLength(16).IsRequired();
        builder.HasIndex(i => i.Code).IsUnique();
        builder.HasIndex(i => i.MemberId);

        builder.HasOne<Member>()
            .WithMany()
            .HasForeignKey(i => i.MemberId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
