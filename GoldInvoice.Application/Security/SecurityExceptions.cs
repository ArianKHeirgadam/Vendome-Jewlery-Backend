namespace GoldInvoice.Application.Security;

public sealed class AuthenticationRejectedException : Exception
{
    public AuthenticationRejectedException()
        : base("Authentication was rejected.")
    {
    }
}

public sealed class SecurityAccessDeniedException : Exception
{
    public SecurityAccessDeniedException()
        : base("The requested security operation is not allowed.")
    {
    }
}

public sealed class SecurityResourceNotFoundException : Exception
{
    public SecurityResourceNotFoundException()
        : base("The requested security resource was not found.")
    {
    }
}
