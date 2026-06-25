using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("role_id");

        builder.Property(x => x.Code)
            .HasColumnName("role_code")
            .HasMaxLength(30)
            .IsRequired();

        builder.HasIndex(x => x.Code)
            .IsUnique();

        builder.Ignore(x => x.SystemName);

        builder.Property(x => x.DisplayName)
            .HasColumnName("role_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Description)
            .HasColumnName("description")
            .HasMaxLength(255);

        builder.Property(x => x.Created)
            .HasColumnName("created_at");

        builder.Property<DateTimeOffset?>("UpdatedAt")
            .HasColumnName("updated_at");

        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModified);
        builder.Ignore(x => x.LastModifiedBy);
    }
}
