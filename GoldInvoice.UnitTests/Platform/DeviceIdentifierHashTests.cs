using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace GoldInvoice.UnitTests.Platform;

public sealed class DeviceIdentifierHashTests
{
    [Fact]
    public void Sha256_IsDeterministic()
    {
        var hash1 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("printer|test"))).ToLowerInvariant();
        var hash2 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("printer|test"))).ToLowerInvariant();
        Assert.Equal(hash1, hash2);
    }
}
