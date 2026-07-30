using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class KnowledgeEntryConfiguration : IEntityTypeConfiguration<KnowledgeEntry>
{
    public void Configure(EntityTypeBuilder<KnowledgeEntry> builder)
    {
        builder.ToTable("knowledge_entries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("knowledge_entry_id");

        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(300).IsRequired();
        builder.Property(x => x.Content).HasColumnName("content").IsRequired();
        builder.Property(x => x.Category).HasColumnName("category").HasMaxLength(40).IsRequired();
        builder.Property(x => x.Keywords)
            .HasColumnName("keywords")
            .HasColumnType("text[]")
            .HasDefaultValueSql("ARRAY[]::text[]");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(x => x.DisplayOrder).HasColumnName("display_order").IsRequired();
        builder.Property(x => x.AuthorId).HasColumnName("author_user_id").IsRequired();
        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModified);
        builder.Ignore(x => x.LastModifiedBy);

        // Trợ lý chỉ đọc entry Published; admin lọc theo status/category.
        builder.HasIndex(x => new { x.Status, x.Category });
        builder.HasIndex(x => new { x.Status, x.DisplayOrder });

        builder.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Restrict);
    }
}
