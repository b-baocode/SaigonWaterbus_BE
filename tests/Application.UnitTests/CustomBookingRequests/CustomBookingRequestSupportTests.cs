using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.CustomBookingRequests;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NUnit.Framework;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.CustomBookingRequests;

public class CustomBookingRequestSupportTests
{
    [Test]
    public void CalculateQuoteValidUntilUsesTwentyFourHoursWhenDepartureIsLater()
    {
        var now = new DateTimeOffset(2026, 6, 16, 3, 0, 0, TimeSpan.Zero);
        var request = new CustomBookingRequest
        {
            DepartureDate = new DateOnly(2026, 6, 20),
            PreferredStartTime = new TimeOnly(7, 0)
        };

        var validUntil = CustomBookingRequestSupport.CalculateQuoteValidUntil(request, now);

        validUntil.ShouldBe(now.AddHours(24));
    }

    [Test]
    public void CalculateQuoteValidUntilUsesDepartureWhenItIsSooner()
    {
        var now = new DateTimeOffset(2026, 6, 16, 3, 0, 0, TimeSpan.Zero);
        var request = new CustomBookingRequest
        {
            DepartureDate = new DateOnly(2026, 6, 16),
            PreferredStartTime = new TimeOnly(15, 0)
        };

        var validUntil = CustomBookingRequestSupport.CalculateQuoteValidUntil(request, now);

        validUntil.ShouldBe(new DateTimeOffset(2026, 6, 16, 8, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public void CalculateQuoteValidUntilRejectsPastDeparture()
    {
        var now = new DateTimeOffset(2026, 6, 16, 8, 0, 0, TimeSpan.Zero);
        var request = new CustomBookingRequest
        {
            DepartureDate = new DateOnly(2026, 6, 16),
            PreferredStartTime = new TimeOnly(14, 0)
        };

        var exception = Should.Throw<ValidationException>(() =>
            CustomBookingRequestSupport.CalculateQuoteValidUntil(request, now));

        exception.Errors.Keys.ShouldContain("departureDate");
    }

    [Test]
    public void EnsureCanQuoteRejectsConfirmedRequest()
    {
        var request = new CustomBookingRequest { Status = CustomBookingRequestStatus.Confirmed };

        var exception = Should.Throw<ValidationException>(() =>
            CustomBookingRequestSupport.EnsureCanQuote(request));

        exception.Errors.Keys.ShouldContain("status");
    }

    [Test]
    public void EnsureCanAcceptQuoteAcceptsQuotedRequestWithValidQuote()
    {
        var now = new DateTimeOffset(2026, 6, 10, 8, 0, 0, TimeSpan.Zero);
        var request = new CustomBookingRequest
        {
            Status = CustomBookingRequestStatus.Quoted,
            AssignedVesselId = Guid.NewGuid(),
            Quote = new CustomBookingQuote { ValidUntil = now.AddDays(1) }
        };

        Should.NotThrow(() => CustomBookingRequestSupport.EnsureCanAcceptQuote(request, now));
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
