using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");

        builder.Property(x => x.Code)
            .HasMaxLength(10)
            .IsRequired();

        builder.HasIndex(x => x.Code)
            .IsUnique();

        builder.Property(x => x.SystemName)
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(x => x.SystemName)
            .IsUnique();

        builder.Property(x => x.DisplayName)
            .HasMaxLength(100)
            .IsRequired();
    }
}
