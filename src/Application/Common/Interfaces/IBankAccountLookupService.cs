namespace SaigonWaterbus.Application.Common.Interfaces;

public sealed record BankAccountLookupServiceRequest(
    string BankBin,
    string AccountNumber);

public sealed record BankAccountLookupServiceResult(
    string BankBin,
    string AccountNumber,
    string AccountName,
    string Provider,
    DateTimeOffset VerifiedAt,
    string? Description = null);

public interface IBankAccountLookupService
{
    Task<BankAccountLookupServiceResult> LookupAsync(
        BankAccountLookupServiceRequest request,
        CancellationToken cancellationToken);
}
