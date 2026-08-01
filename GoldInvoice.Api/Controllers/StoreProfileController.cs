using GoldInvoice.Application.Security;
using GoldInvoice.Application.Settings;
using GoldInvoice.Contracts.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GoldInvoice.Api.Controllers;

[ApiController]
[Authorize]
[RequestSizeLimit(32 * 1024)]
[Route("api/v1/settings/store-profile")]
public sealed class StoreProfileController(IStoreProfileService storeProfileService) : ControllerBase
{
    [Authorize(Policy = SecurityPermissions.SettingsRead)]
    [HttpGet]
    public async Task<ActionResult<StoreProfileResponse>> Get(
        CancellationToken cancellationToken) =>
        Ok(Map(await storeProfileService.GetAsync(cancellationToken)));

    [Authorize(Policy = SecurityPermissions.SettingsManage)]
    [HttpPut]
    public async Task<ActionResult<StoreProfileResponse>> Upsert(
        UpdateStoreProfileRequest request,
        CancellationToken cancellationToken) =>
        Ok(Map(await storeProfileService.UpsertAsync(
            new UpdateStoreProfileCommand(
                request.TradeName,
                request.LegalName,
                request.NationalId,
                request.EconomicCode,
                request.RegistrationNumber,
                request.PhoneNumber,
                request.PostalCode,
                request.AddressLine,
                request.RowVersion),
            cancellationToken)));

    private static StoreProfileResponse Map(StoreProfileInfo profile) => new()
    {
        TradeName = profile.TradeName,
        LegalName = profile.LegalName,
        NationalId = profile.NationalId,
        EconomicCode = profile.EconomicCode,
        RegistrationNumber = profile.RegistrationNumber,
        PhoneNumber = profile.PhoneNumber,
        PostalCode = profile.PostalCode,
        AddressLine = profile.AddressLine,
        RowVersion = profile.RowVersion
    };
}
