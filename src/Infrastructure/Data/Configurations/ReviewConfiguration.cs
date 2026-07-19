using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable("reviews");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("review_id");

        builder.Property(x => x.CustomerId).HasColumnName("customer_user_id").IsRequired();
        builder.Property(x => x.BookingId).HasColumnName("booking_id");
        builder.Property(x => x.TripId).HasColumnName("trip_id");
        builder.Property(x => x.Rating).HasColumnName("rating").IsRequired();
        builder.Property(x => x.Comment).HasColumnName("comment").HasMaxLength(1000);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(30).IsRequired();
        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property<DateTimeOffset?>("UpdatedAt").HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModified);
        builder.Ignore(x => x.LastModifiedBy);

        // Mỗi khách chỉ được 1 review / 1 trip (Postgres cho phép nhiều NULL trip_id nên không chặn review cũ không gắn trip).
        builder.HasIndex(x => new { x.CustomerId, x.TripId })
            .IsUnique()
            .HasDatabaseName("ux_reviews_customer_trip");

        builder.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Booking).WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.Trip).WithMany().HasForeignKey(x => x.TripId).OnDelete(DeleteBehavior.SetNull);
    }
}
