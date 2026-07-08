namespace RetailSuite.Shared;

public abstract class BaseEntity
{
    // Property initializers ensure new instances always have a valid Id + CreatedAt
    // even if a constructor forgets to set them. Setters kept public so the
    // DbContext SaveChangesAsync safety net can top up any entity that still has
    // default values (Guid.Empty / 01-01-0001).
    public Guid     Id        { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
