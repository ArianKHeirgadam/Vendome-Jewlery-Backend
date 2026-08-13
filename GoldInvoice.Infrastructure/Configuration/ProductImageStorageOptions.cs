namespace GoldInvoice.Infrastructure.Configuration;

public sealed class ProductImageStorageOptions
{
    public const string SectionName = "ProductImages";

    public string? RootPath { get; set; }
}
