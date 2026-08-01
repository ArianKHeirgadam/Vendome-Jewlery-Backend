using GoldInvoice.Domain.Common;

namespace GoldInvoice.Domain.Customers;

public sealed class CustomerAddress : SoftDeletableEntity
{
    private CustomerAddress()
    {
    }

    public CustomerAddress(
        Guid customerId,
        string title,
        string recipientName,
        string phoneNumber,
        string province,
        string city,
        string postalCode,
        string addressLine,
        bool isDefault)
    {
        Guard.AgainstEmpty(customerId, nameof(customerId));
        CustomerId = customerId;
        SetValues(
            title,
            recipientName,
            phoneNumber,
            province,
            city,
            postalCode,
            addressLine,
            isDefault);
    }

    public Guid CustomerId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string RecipientName { get; private set; } = string.Empty;

    public string PhoneNumber { get; private set; } = string.Empty;

    public string Province { get; private set; } = string.Empty;

    public string City { get; private set; } = string.Empty;

    public string PostalCode { get; private set; } = string.Empty;

    public string AddressLine { get; private set; } = string.Empty;

    public bool IsDefault { get; private set; }

    public void Update(
        string title,
        string recipientName,
        string phoneNumber,
        string province,
        string city,
        string postalCode,
        string addressLine,
        bool isDefault) =>
        SetValues(
            title,
            recipientName,
            phoneNumber,
            province,
            city,
            postalCode,
            addressLine,
            isDefault);

    public void ClearDefault() => IsDefault = false;

    private void SetValues(
        string title,
        string recipientName,
        string phoneNumber,
        string province,
        string city,
        string postalCode,
        string addressLine,
        bool isDefault)
    {
        Title = Guard.Required(title, nameof(title), 100);
        RecipientName = Guard.Required(recipientName, nameof(recipientName), 200);
        PhoneNumber = Guard.Required(phoneNumber, nameof(phoneNumber), 32);
        Province = Guard.Required(province, nameof(province), 100);
        City = Guard.Required(city, nameof(city), 100);
        PostalCode = Guard.Required(postalCode, nameof(postalCode), 20);
        AddressLine = Guard.Required(addressLine, nameof(addressLine), 1000);
        IsDefault = isDefault;
    }
}
