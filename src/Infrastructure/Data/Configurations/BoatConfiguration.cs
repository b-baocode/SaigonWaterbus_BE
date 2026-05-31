using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class BoatConfiguration : IEntityTypeConfiguration<Boat>
{
    public void Configure(EntityTypeBuilder<Boat> builder)
    {
        builder.ToTable("boats");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("boat_id");

        builder.Property(x => x.BoatCode).HasColumnName("boat_code").HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.BoatCode).IsUnique();

        builder.Property(x => x.BoatName).HasColumnName("boat_name").HasMaxLength(150).IsRequired();
        builder.Property(x => x.Capacity).HasColumnName("capacity").IsRequired();
        builder.Property(x => x.BoatStatus).HasColumnName("boat_status").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description");

        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property(x => x.LastModified).HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModifiedBy);
    }
}
