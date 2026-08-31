using FluentValidation;
using NUnit.Framework;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.InsurancePackages;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.InsurancePackages;

public sealed class InsurancePackageImageTests
{
    [Test]
    public async Task UpdateImage_UploadsThroughStorageAndPersistsReturnedUrl()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var package = new InsurancePackage
        {
            Code = "IMAGE_TEST",
            Name = "Image test",
            BookingType = "PassengerInsurance",
            ProviderName = "Provider",
            UnitPremiumAmount = 5_000,
            CoverageAmount = 100_000,
            IsActive = true
        };
        context.Set<InsurancePackage>().Add(package);
        await context.SaveChangesAsync();

        var storage = new FakeInsurancePackageImageStorage();
        await using var content = new MemoryStream([1, 2, 3]);
        var result = await new UpdateInsurancePackageImageCommandHandler(context, storage).Handle(
            new UpdateInsurancePackageImageCommand(
                package.Id,
                new InsurancePackageImageFileRequest("logo.svg", "image/svg+xml", content.Length, content)),
            CancellationToken.None);

        result.ImageUrl.ShouldBe(FakeInsurancePackageImageStorage.Url);
        result.ProviderLogoUrl.ShouldBe(FakeInsurancePackageImageStorage.Url);
        context.Set<InsurancePackage>().Single().ImageUrl.ShouldBe(FakeInsurancePackageImageStorage.Url);
        context.Set<InsurancePackage>().Single().ProviderLogoUrl.ShouldBe(FakeInsurancePackageImageStorage.Url);
        storage.Upload.ShouldNotBeNull();
        storage.Upload!.InsurancePackageId.ShouldBe(package.Id);
        storage.Upload.ContentType.ShouldBe("image/svg+xml");
    }

    [Test]
    public async Task Validator_AllowsSvgImage()
    {
        var command = new UpdateInsurancePackageImageCommand(
            Guid.NewGuid(),
            new InsurancePackageImageFileRequest("logo.svg", "image/svg+xml", 100, new MemoryStream([1])));

        var result = await new UpdateInsurancePackageImageCommandValidator().ValidateAsync(command);

        result.IsValid.ShouldBeTrue();
    }

    private sealed class FakeInsurancePackageImageStorage : IInsurancePackageImageStorageService
    {
        public const string Url = "https://res.cloudinary.com/demo/image/upload/insurance/logo.svg";
        public long MaxImageBytes => 5 * 1024 * 1024;
        public IReadOnlyCollection<string> AllowedImageContentTypes => ["image/svg+xml"];
        public InsurancePackageImageUpload? Upload { get; private set; }

        public Task<StoredInsurancePackageImage> UploadImageAsync(
            InsurancePackageImageUpload upload,
            CancellationToken cancellationToken)
        {
            Upload = upload;
            return Task.FromResult(new StoredInsurancePackageImage(Url, "insurance/logo"));
        }
    }
}
