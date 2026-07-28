using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class VoiceConfiguration : IEntityTypeConfiguration<Voice>
{
    public void Configure(EntityTypeBuilder<Voice> builder)
    {
        builder.ToTable("voices");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("voice_id");

        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(150).IsRequired();
        builder.Property(x => x.Region).HasColumnName("region").HasMaxLength(20);
        builder.Property(x => x.Gender).HasColumnName("gender").HasMaxLength(10);
        builder.Property(x => x.Style).HasColumnName("style").HasMaxLength(30);
        builder.Property(x => x.VieneuVoiceId).HasColumnName("vieneu_voice_id").HasMaxLength(150);
        builder.Property(x => x.RefAudioUrl).HasColumnName("ref_audio_url").HasMaxLength(1000);
        builder.Property(x => x.SampleUrl).HasColumnName("sample_url").HasMaxLength(1000);
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.DisplayOrder).HasColumnName("display_order").IsRequired();
        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property<DateTimeOffset?>("UpdatedAt").HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModified);
        builder.Ignore(x => x.LastModifiedBy);
    }
}
