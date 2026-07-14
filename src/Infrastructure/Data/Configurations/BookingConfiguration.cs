using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    private static readonly JsonSerializerOptions InsuranceSnapshotJsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly ValueComparer<BookingInsuranceSnapshot?> InsuranceSnapshotComparer = new(
        (left, right) => SerializeInsuranceSnapshot(left) == SerializeInsuranceSnapshot(right),
        snapshot => GetInsuranceSnapshotHashCode(snapshot),
        snapshot => DeserializeInsuranceSnapshot(SerializeInsuranceSnapshot(snapshot)));

    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("bookings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("booking_id");
        builder.Property(x => x.BookingType)
            .HasColumnName("booking_type")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.UserId).HasColumnName("customer_user_id");
        builder.Property(x => x.PromotionId).HasColumnName("promotion_id");
        builder.Property(x => x.TripId).HasColumnName("trip_id");
        builder.Property(x => x.CharterRouteId).HasColumnName("charter_route_id");
        builder.Property(x => x.AssignedManagerId).HasColumnName("assigned_manager_id");
        builder.Property(x => x.BookingCode).HasColumnName("booking_code").HasMaxLength(50).IsRequired();
        builder.Property(x => x.CharterBookingQrToken).HasColumnName("charter_booking_qr_token").HasMaxLength(100);
        builder.Property(x => x.ContactName).HasColumnName("contact_name").HasMaxLength(150).IsRequired();
        builder.Property(x => x.ContactPhone).HasColumnName("contact_phone").HasMaxLength(30).IsRequired();
        builder.Property(x => x.ContactEmail).HasColumnName("contact_email").HasMaxLength(255);
        builder.HasIndex(x => x.BookingCode).IsUnique();
        builder.HasIndex(x => x.CharterBookingQrToken)
            .IsUnique()
            .HasFilter("charter_booking_qr_token IS NOT NULL");

        builder.Property(x => x.BookingStatus)
            .HasColumnName("status")
            .HasConversion(
                value => ToDatabaseBookingStatus(value),
                value => FromDatabaseBookingStatus(value))
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(x => x.SubtotalAmount).HasColumnName("subtotal_amount").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.DiscountAmount).HasColumnName("discount_amount").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.TotalAmount).HasColumnName("total_amount").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.PaymentStatus).HasColumnName("payment_status").HasMaxLength(30).IsRequired();
        builder.Property(x => x.DepositAmount).HasColumnName("deposit_amount").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.RemainingAmount).HasColumnName("remaining_amount").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Ignore(x => x.PointsUsed);
        builder.Ignore(x => x.PointsEarned);
        builder.Property(x => x.BoatId).HasColumnName("boat_id");
        builder.Property(x => x.FromStationId).HasColumnName("custom_from_station_id");
        builder.Property(x => x.ToStationId).HasColumnName("custom_to_station_id");
        builder.Property(x => x.DepartureDate).HasColumnName("departure_date");
        builder.Property(x => x.StartTime).HasColumnName("start_time");
        builder.Property(x => x.RentalUnit)
            .HasColumnName("rental_unit")
            .HasConversion<string>()
            .HasMaxLength(10);
        builder.Property(x => x.DurationValue).HasColumnName("duration_value").IsRequired(false);
        builder.Property(x => x.PassengerCount).HasColumnName("passenger_count").IsRequired(false);
        builder.Property(x => x.AdultCount).HasColumnName("adult_count").IsRequired(false);
        builder.Property(x => x.ChildCount).HasColumnName("child_count").IsRequired(false);
        builder.Property(x => x.RequestedBoatCount).HasColumnName("requested_boat_count").IsRequired(false);
        builder.Property(x => x.RequestedBoatDecks).HasColumnName("requested_boat_decks").HasMaxLength(1000);
        builder.Property(x => x.RequestedBoatTypes).HasColumnName("requested_boat_types").HasMaxLength(1000);
        builder.Property(x => x.PreferredSeatSetupType)
            .HasColumnName("preferred_seat_setup_type")
            .HasConversion<string>()
            .HasMaxLength(30);
        builder.Property(x => x.BoatRequirements).HasColumnName("boat_requirements").HasMaxLength(1000);
        builder.Property(x => x.SpecialRequests).HasColumnName("special_requests").HasMaxLength(1000);
        builder.Property(x => x.InsuranceSnapshot)
            .HasColumnName("insurance_snapshot")
            .HasColumnType("jsonb")
            .HasConversion(
                snapshot => SerializeInsuranceSnapshot(snapshot),
                json => DeserializeInsuranceSnapshot(json))
            .Metadata.SetValueComparer(InsuranceSnapshotComparer);
        builder.Property(x => x.HoldExpiresAt).HasColumnName("hold_expires_at");
        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property<DateTimeOffset?>("UpdatedAt").HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModified);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasIndex(x => new { x.BoatId, x.DepartureDate })
            .HasDatabaseName("ux_bookings_boat_date_active")
            .IsUnique()
            .HasFilter("booking_type = 'CharterBooking' AND status IN ('Quoted', 'Confirmed')");
        builder.HasIndex(x => x.CharterRouteId);
        builder.HasIndex(x => x.AssignedManagerId);

        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.Promotion).WithMany(p => p.Bookings).HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.Trip).WithMany().HasForeignKey(x => x.TripId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.CharterRoute).WithMany().HasForeignKey(x => x.CharterRouteId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.AssignedManager).WithMany().HasForeignKey(x => x.AssignedManagerId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.Boat).WithMany().HasForeignKey(x => x.BoatId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.FromStation).WithMany().HasForeignKey(x => x.FromStationId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.ToStation).WithMany().HasForeignKey(x => x.ToStationId).OnDelete(DeleteBehavior.SetNull);
    }

    private static string ToDatabaseBookingStatus(BookingStatus status) =>
        status switch
        {
            BookingStatus.PendingPayment => "Pending",
            BookingStatus.Expired => "Expired",
            BookingStatus.Refunded => "Refunded",
            BookingStatus.Quoted => "Quoted",
            BookingStatus.Completed => "Completed",
            _ => status.ToString()
        };

    private static BookingStatus FromDatabaseBookingStatus(string status) =>
        status switch
        {
            "Pending" => BookingStatus.PendingPayment,
            "Quoted" => BookingStatus.Quoted,
            "Completed" => BookingStatus.Completed,
            _ => Enum.Parse<BookingStatus>(status, true)
        };

    private static string? SerializeInsuranceSnapshot(BookingInsuranceSnapshot? snapshot) =>
        snapshot is null
            ? null
            : JsonSerializer.Serialize(snapshot, InsuranceSnapshotJsonOptions);

    private static BookingInsuranceSnapshot? DeserializeInsuranceSnapshot(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<BookingInsuranceSnapshot>(json, InsuranceSnapshotJsonOptions);

    private static int GetInsuranceSnapshotHashCode(BookingInsuranceSnapshot? snapshot)
    {
        var json = SerializeInsuranceSnapshot(snapshot);
        return json is null ? 0 : json.GetHashCode();
    }
}
