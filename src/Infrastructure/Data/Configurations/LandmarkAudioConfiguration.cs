using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class LandmarkAudioConfiguration : IEntityTypeConfiguration<LandmarkAudio>
{
    public void Configure(EntityTypeBuilder<LandmarkAudio> builder)
    {
        builder.ToTable("landmark_audios");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("landmark_audio_id");

        builder.Property(x => x.LandmarkId).HasColumnName("landmark_id").IsRequired();
        builder.Property(x => x.VoiceId).HasColumnName("voice_id").IsRequired();
        builder.Property(x => x.AudioUrl).HasColumnName("audio_url").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.DurationSeconds).HasColumnName("duration_seconds").HasColumnType("numeric(8,2)");
        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property<DateTimeOffset?>("UpdatedAt").HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModified);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasOne(x => x.Landmark).WithMany(x => x.Audios)
            .HasForeignKey(x => x.LandmarkId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Voice).WithMany(x => x.LandmarkAudios)
            .HasForeignKey(x => x.VoiceId).OnDelete(DeleteBehavior.Restrict);

        // Mỗi landmark chỉ một bản thu cho mỗi giọng.
        builder.HasIndex(x => new { x.LandmarkId, x.VoiceId }).IsUnique();
    }
}
