namespace GoldInvoice.Application.Security;

public static class SecurityRoles
{
    public const string Owner = "Owner";

    public const string Admin = "Admin";



    public const string Employee = "Employee";
    public const string Customer = "Customer";

    public static readonly IReadOnlyList<string> All = [Owner, Admin, Employee, Customer];
}

public static class SecurityPermissions
{
    public const string UsersRead = "Users.Read";
    public const string UsersManage = "Users.Manage";
    public const string AdminsManage = "Admins.Manage";
    public const string ProductsRead = "Products.Read";
    public const string ProductsManage = "Products.Manage";
    public const string InventoryRead = "Inventory.Read";
    public const string InventoryAdjust = "Inventory.Adjust";
    public const string OrdersRead = "Orders.Read";
    public const string OrdersManage = "Orders.Manage";
    public const string PaymentsRead = "Payments.Read";
    public const string PaymentsManage = "Payments.Manage";
    public const string InvoicesRead = "Invoices.Read";
    public const string InvoicesPrint = "Invoices.Print";
    public const string InvoicesReprint = "Invoices.Reprint";
    public const string ReportsRead = "Reports.Read";
    public const string SuppliersRead = "Suppliers.Read";
    public const string SuppliersManage = "Suppliers.Manage";
    public const string CrmRead = "Crm.Read";
    public const string CrmManage = "Crm.Manage";
    public const string AuditLogsRead = "AuditLogs.Read";
    public const string SettingsRead = "Settings.Read";
    public const string SettingsManage = "Settings.Manage";
    public const string SessionsManage = "Sessions.Manage";
    public const string DesktopDevicesView = "DesktopDevices.View";
    public const string DesktopDevicesManage = "DesktopDevices.Manage";
    public const string DevicePrintersManage = "DevicePrinters.Manage";
    public const string DevicePrintProfilesManage = "DevicePrintProfiles.Manage";
    public const string InvoicePrintJobsView = "InvoicePrintJobs.View";
    public const string OutboxReprocess = "Outbox.Reprocess";

    public static readonly IReadOnlyList<PermissionDefinition> All =
    [
        new(UsersRead, "Read users", "Users"),
        new(UsersManage, "Manage users", "Users"),
        new(AdminsManage, "Manage administrators", "Users"),
        new(ProductsRead, "Read products", "Catalog"),
        new(ProductsManage, "Manage products", "Catalog"),
        new(InventoryRead, "Read inventory", "Inventory"),
        new(InventoryAdjust, "Adjust inventory", "Inventory"),
        new(OrdersRead, "Read orders", "Sales"),
        new(OrdersManage, "Manage orders", "Sales"),
        new(PaymentsRead, "Read payments", "Billing"),
        new(PaymentsManage, "Manage payments", "Billing"),
        new(InvoicesRead, "Read invoices", "Invoicing"),
        new(InvoicesPrint, "Print invoices", "Invoicing"),
        new(InvoicesReprint, "Reprint invoices", "Invoicing"),
        new(ReportsRead, "Read reports", "Reports"),
        new(SuppliersRead, "Read suppliers", "Suppliers"),
        new(SuppliersManage, "Manage suppliers", "Suppliers"),
        new(CrmRead, "Read customer interactions", "CRM"),
        new(CrmManage, "Manage customer interactions", "CRM"),
        new(AuditLogsRead, "Read audit logs", "Security"),
        new(SettingsRead, "Read settings", "Settings"),
        new(SettingsManage, "Manage settings", "Settings"),
        new(SessionsManage, "Manage sessions", "Security"),
        new(DesktopDevicesView, "View desktop devices", "Devices"),
        new(DesktopDevicesManage, "Manage desktop devices", "Devices"),
        new(DevicePrintersManage, "Manage device printers", "Devices"),
        new(DevicePrintProfilesManage, "Manage print profiles", "Devices"),
        new(InvoicePrintJobsView, "View invoice print jobs", "Invoicing"),
        new(OutboxReprocess, "Reprocess outbox messages", "Integration")
    ];
}

public sealed record PermissionDefinition(string Name, string DisplayName, string Group);

public static class SecurityClaimNames
{
    public const string Subject = "sub";
    public const string TokenId = "jti";
    public const string SessionId = "sid";
    public const string TokenUse = "token_use";
    public const string SecurityStampHash = "sst";
    public const string AuthenticationMethod = "amr";
    public const string Role = "role";
    public const string Permission = "permission";
    public const string DisplayName = "name";
}

public static class SecurityTokenUses
{
    public const string Access = "access";
    public const string MfaEnrollment = "mfa_enrollment";
}

public static class AuthenticationMethods
{
    public const string Password = "pwd";
    public const string Mfa = "mfa";
}
