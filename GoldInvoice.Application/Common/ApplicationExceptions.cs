namespace GoldInvoice.Application.Common;

public class ApplicationResourceNotFoundException : Exception
{
    public ApplicationResourceNotFoundException()
        : base("The requested application resource was not found.")
    {
    }
}

public class ApplicationConflictException : Exception
{
    public ApplicationConflictException()
        : base("The requested operation conflicts with current state.")
    {
    }
}

public sealed class ApplicationConcurrencyException : ApplicationConflictException
{
}

public sealed class ManualPriceReviewRequiredException : ApplicationConflictException
{
}

public sealed class StoreProfileNotConfiguredException : Exception
{
    public StoreProfileNotConfiguredException()
        : base("The store profile must be configured before an order can be created.")
    {
    }
}
