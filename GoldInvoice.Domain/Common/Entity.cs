namespace GoldInvoice.Domain.Common;

public interface IAuditableEntity
{
    DateTimeOffset CreatedAt { get; }

    DateTimeOffset UpdatedAt { get; }

    Guid? CreatedBy { get; }

    Guid? UpdatedBy { get; }
}

public interface ISoftDeletableEntity
{
    bool IsDeleted { get; }

    DateTimeOffset? DeletedAt { get; }

    Guid? DeletedBy { get; }
}

public interface IAppendOnlyEntity;

public interface IProtectedFromHardDelete;

public abstract class Entity
{
    protected Entity()
    {
        Id = Guid.NewGuid();
    }

    public Guid Id { get; private set; }
}

public abstract class AuditableEntity : Entity, IAuditableEntity
{
    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public byte[] RowVersion { get; private set; } = [];
}

public abstract class SoftDeletableEntity : AuditableEntity, ISoftDeletableEntity
{
    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public Guid? DeletedBy { get; private set; }
}
