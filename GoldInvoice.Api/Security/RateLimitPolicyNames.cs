namespace GoldInvoice.Api.Security;

public static class RateLimitPolicyNames
{
    public const string Login = "auth-login";

    public const string Refresh = "auth-refresh";

    public const string Mfa = "auth-mfa";

    public const string PaymentCallback = "payment-callback";
}
