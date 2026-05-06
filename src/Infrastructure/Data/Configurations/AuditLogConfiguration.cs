using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");

        builder.Property(x => x.Action)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.TargetTable)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.OldValues)
            .HasColumnType("jsonb");

        builder.Property(x => x.NewValues)
            .HasColumnType("jsonb");

        builder.Property(x => x.IpAddress)
            .HasMaxLength(64);

        builder.Property(x => x.UserAgent)
            .HasMaxLength(500);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.HasIndex(x => new { x.TargetTable, x.TargetId, x.CreatedAt });
        builder.HasIndex(x => new { x.ActorUserId, x.CreatedAt });

        builder.HasOne(x => x.ActorUser)
            .WithMany(x => x.AuditLogs)
            .HasForeignKey(x => x.ActorUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
