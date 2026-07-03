using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

public class Notification : BaseGuidEntity
{
    public Guid UserId { get; set; }
    public string Title { get; set; } = null!;
    public string? Body { get; set; }
    public string Type { get; set; } = null!;
    public bool IsRead { get; set; } = false;
    public DateTimeOffset? ReadAt { get; set; }
    public string? RelatedEntityType { get; set; }
    public Guid? RelatedEntityId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public User User { get; set; } = null!;
}
