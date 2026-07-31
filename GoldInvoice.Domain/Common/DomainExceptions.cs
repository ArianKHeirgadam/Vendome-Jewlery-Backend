namespace GoldInvoice.Domain.Common;

public sealed class DomainConflictException : Exception
{
    public DomainConflictException(string message)
        : base(message)
    {
    }
}
