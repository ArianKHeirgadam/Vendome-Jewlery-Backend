namespace VendomeJewleryDesktopApp.Services;

internal sealed class DesktopBridgeException : Exception
{
    public DesktopBridgeException(string code, string message, int? status = null)
        : base(message)
    {
        Code = code;
        Status = status;
    }

    public string Code { get; }

    public int? Status { get; }
}
