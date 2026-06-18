using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class CustomBookingRequestConfiguration : IEntityTypeConfiguration<CustomBookingRequest>
{
    public void Configure(EntityTypeBuilder<CustomBookingRequest> builder)
    {
        builder.ToTable("custom_booking_requests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("custom_booking_request_id");

        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.ContactUserId).HasColumnName("contact_user_id");
        builder.Property(x => x.ContactName).HasColumnName("contact_name").HasMaxLength(150).IsRequired();
        builder.Property(x => x.ContactPhone).HasColumnName("contact_phone").HasMaxLength(20).IsRequired();
        builder.Property(x => x.ContactEmail).HasColumnName("contact_email").HasMaxLength(255);
        builder.Property(x => x.WaterbusServiceId).HasColumnName("waterbus_service_id");
        builder.Property(x => x.RequestedNumberOfDecks).HasColumnName("requested_number_of_decks").IsRequired();
        builder.Property(x => x.RequestedSeatSetupType)
            .HasColumnName("requested_seat_setup_type")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.RentalUnit)
            .HasColumnName("rental_unit")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(x => x.PreferredVesselId).HasColumnName("preferred_vessel_id");
        builder.Property(x => x.AssignedVesselId).HasColumnName("assigned_vessel_id");
        builder.Property(x => x.AssignedAt).HasColumnName("assigned_at");
        builder.Property(x => x.AssignedByUserId).HasColumnName("assigned_by_user_id");
        builder.Property(x => x.AssignedManagerUserId).HasColumnName("assigned_manager_user_id");
        builder.Property(x => x.ManagerAssignedAt).HasColumnName("manager_assigned_at");
        builder.Property(x => x.ManagerAssignedByUserId).HasColumnName("manager_assigned_by_user_id");
        builder.Property(x => x.DepartureDate).HasColumnName("departure_date").IsRequired();
        builder.Property(x => x.PreferredStartTime).HasColumnName("preferred_start_time");
        builder.Property(x => x.PreferredEndTime).HasColumnName("preferred_end_time");
        builder.Property(x => x.EstimatedEndDate).HasColumnName("estimated_end_date");
        builder.Property(x => x.EstimatedTravelMinutes).HasColumnName("estimated_travel_minutes").IsRequired();
        builder.Property(x => x.EstimatedStayMinutes).HasColumnName("estimated_stay_minutes").IsRequired();
        builder.Property(x => x.BufferMinutes).HasColumnName("buffer_minutes").IsRequired();
        builder.Property(x => x.EstimatedDurationMinutes).HasColumnName("estimated_duration_minutes").IsRequired();
        builder.Property(x => x.FromLocation).HasColumnName("from_location").HasMaxLength(200).IsRequired();
        builder.Property(x => x.ToLocation).HasColumnName("to_location").HasMaxLength(200).IsRequired();
        builder.Property(x => x.FromStationId).HasColumnName("from_station_id");
        builder.Property(x => x.FromStationCode).HasColumnName("from_station_code").HasMaxLength(50);
        builder.Property(x => x.ToStationId).HasColumnName("to_station_id");
        builder.Property(x => x.ToStationCode).HasColumnName("to_station_code").HasMaxLength(50);
        builder.Property(x => x.ItineraryNote).HasColumnName("itinerary_note").HasMaxLength(1000);
        builder.Property(x => x.PassengerCount).HasColumnName("passenger_count").IsRequired();
        builder.Property(x => x.AdultCount).HasColumnName("adult_count").IsRequired();
        builder.Property(x => x.ChildCount).HasColumnName("child_count").IsRequired();
        builder.Property(x => x.SpecialRequests).HasColumnName("special_requests").HasMaxLength(1000);
        builder.Property(x => x.PassengerManifestStatus)
            .HasColumnName("passenger_manifest_status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(x => x.PassengerManifestCompletedAt).HasColumnName("passenger_manifest_completed_at");
        builder.Property(x => x.PassengerManifestCompletedByUserId).HasColumnName("passenger_manifest_completed_by_user_id");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.StatusReason).HasColumnName("status_reason").HasMaxLength(500);
        builder.Property(x => x.QuotedAt).HasColumnName("quoted_at");
        builder.Property(x => x.QuotedByUserId).HasColumnName("quoted_by_user_id");
        builder.Property(x => x.QuoteAcceptedAt).HasColumnName("quote_accepted_at");
        builder.Property(x => x.CancelledAt).HasColumnName("cancelled_at");
        builder.Property(x => x.CancelledByUserId).HasColumnName("cancelled_by_user_id");

        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property(x => x.LastModified).HasColumnName("updated_at").IsConcurrencyToken();
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasIndex(x => new { x.Status, x.DepartureDate });
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.ContactUserId);
        builder.HasIndex(x => x.WaterbusServiceId);
        builder.HasIndex(x => x.PreferredVesselId);
        builder.HasIndex(x => x.AssignedVesselId);
        builder.HasIndex(x => x.AssignedByUserId);
        builder.HasIndex(x => x.AssignedManagerUserId);
        builder.HasIndex(x => x.ManagerAssignedByUserId);
        builder.HasIndex(x => x.PassengerManifestCompletedByUserId);
        builder.HasIndex(x => x.CancelledByUserId);
        builder.HasIndex(x => x.FromStationId);
        builder.HasIndex(x => x.ToStationId);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.ContactUser)
            .WithMany()
            .HasForeignKey(x => x.ContactUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.WaterbusService)
            .WithMany()
            .HasForeignKey(x => x.WaterbusServiceId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.QuotedByUser)
            .WithMany()
            .HasForeignKey(x => x.QuotedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.AssignedVessel)
            .WithMany()
            .HasForeignKey(x => x.AssignedVesselId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.PreferredVessel)
            .WithMany()
            .HasForeignKey(x => x.PreferredVesselId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.AssignedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.AssignedManagerUser)
            .WithMany()
            .HasForeignKey(x => x.AssignedManagerUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.ManagerAssignedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CancelledByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.PassengerManifestCompletedByUser)
            .WithMany()
            .HasForeignKey(x => x.PassengerManifestCompletedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.FromStation)
            .WithMany()
            .HasForeignKey(x => x.FromStationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.ToStation)
            .WithMany()
            .HasForeignKey(x => x.ToStationId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
