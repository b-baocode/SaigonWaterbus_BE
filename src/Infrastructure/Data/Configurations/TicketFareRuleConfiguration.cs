using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class TicketFareRuleConfiguration : IEntityTypeConfiguration<TicketFareRule>
{
    public void Configure(EntityTypeBuilder<TicketFareRule> builder)
    {
        builder.ToTable("ticket_fare_rules");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("ticket_fare_rule_id");

        builder.Property(x => x.TicketTypeCode).HasColumnName("ticket_type_code").HasMaxLength(30).IsRequired();
        builder.Property(x => x.RouteType).HasColumnName("route_type").HasMaxLength(50).IsRequired();
        builder.Property(x => x.PriceModifier).HasColumnName("price_modifier").HasColumnType("numeric(8,4)").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();

        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property<DateTimeOffset?>("UpdatedAt").HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModified);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasIndex(x => new { x.TicketTypeCode, x.RouteType }).IsUnique();
    }
}
