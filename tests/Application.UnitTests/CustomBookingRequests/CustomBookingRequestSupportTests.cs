using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.CustomBookingRequests;
using NUnit.Framework;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.CustomBookingRequests;

public class CustomBookingRequestSupportTests
{
    [Test]
    public void EnsurePreferredTimeRangeIsValidRejectsEndTimeBeforeStartTime()
    {
        var exception = Should.Throw<ValidationException>(() =>
            CustomBookingRequestSupport.EnsurePreferredTimeRangeIsValid(
                new TimeOnly(11, 0),
                new TimeOnly(10, 0)));

        exception.Errors.Keys.ShouldContain("preferredEndTime");
    }

    [Test]
    public void EnsureDepartureDateIsNotPastRejectsPastVietnamDate()
    {
        var exception = Should.Throw<ValidationException>(() =>
            CustomBookingRequestSupport.EnsureDepartureDateIsNotPast(
                new DateOnly(2026, 6, 9),
                new DateOnly(2026, 6, 10)));

        exception.Errors.Keys.ShouldContain("departureDate");
    }

    [Test]
    public void EnsureQuoteIsValidRejectsExpiredValidUntil()
    {
        var now = new DateTimeOffset(2026, 6, 10, 8, 0, 0, TimeSpan.Zero);

        var exception = Should.Throw<ValidationException>(() =>
            CustomBookingRequestSupport.EnsureQuoteIsValid(now, now));

        exception.Errors.Keys.ShouldContain("validUntil");
    }

    [Test]
    public void NormalizeUtcConvertsOffsetDateTimeToUtc()
    {
        var value = new DateTimeOffset(2026, 6, 30, 23, 59, 59, TimeSpan.FromHours(7));

        var normalized = CustomBookingRequestSupport.NormalizeUtc(value);

        normalized.ShouldNotBeNull();
        normalized.Value.Offset.ShouldBe(TimeSpan.Zero);
        normalized.Value.ShouldBe(new DateTimeOffset(2026, 6, 30, 16, 59, 59, TimeSpan.Zero));
    }

    [TestCase(null, true)]
    [TestCase("vnd", true)]
    [TestCase("VND", true)]
    [TestCase("VN", false)]
    [TestCase("VN1", false)]
    [TestCase("VND1", false)]
    public void IsValidCurrencyCodeValidatesThreeLetterCurrencyCodes(string? currency, bool expected)
    {
        CustomBookingRequestSupport.IsValidCurrencyCode(currency).ShouldBe(expected);
    }
}
