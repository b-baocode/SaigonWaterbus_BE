using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");

        builder.Property(x => x.Code)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.HasIndex(x => x.Code)
            .IsUnique();

        builder.HasIndex(x => x.Name)
            .IsUnique();

        builder.HasData(
            new Role { Id = 1, Code = Roles.Administrator, Name = "Administrator" },
            new Role { Id = 2, Code = Roles.Manager, Name = "Manager" },
            new Role { Id = 3, Code = Roles.Staff, Name = "Staff" },
            new Role { Id = 4, Code = Roles.Customer, Name = "Customer" });
    }
}
