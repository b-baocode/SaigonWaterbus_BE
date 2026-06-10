using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class TicketTypeConfiguration : IEntityTypeConfiguration<TicketType>
{
    public void Configure(EntityTypeBuilder<TicketType> builder)
    {
        builder.ToTable("ticket_types");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("ticket_type_id");

        builder.Property(x => x.TicketTypeCode).HasColumnName("ticket_type_code").HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.TicketTypeCode).IsUnique();

        builder.Property(x => x.TicketTypeName).HasColumnName("ticket_type_name").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description");
        builder.Property(x => x.PriceModifier).HasColumnName("price_modifier").HasColumnType("numeric(6,2)").IsRequired();
        builder.Property(x => x.PointsEarnedRate).HasColumnName("points_earned_rate").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();

        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property(x => x.LastModified).HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModifiedBy);
    }
}
