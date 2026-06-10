using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Infrastructure.Data;

public sealed class BookingCodeGenerator : IBookingCodeGenerator
{
    private static readonly char[] Chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();

    public Task<string> GenerateAsync(CancellationToken cancellationToken)
    {
        var suffix = new char[5];
        for (var i = 0; i < suffix.Length; i++)
            suffix[i] = Chars[Random.Shared.Next(Chars.Length)];

        return Task.FromResult($"BK-{DateTime.UtcNow:yyyyMMdd}-{new string(suffix)}");
    }
}
