using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using SaigonWaterbus.Application.Stations;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.UnitTests.Stations;

public class StationImageTests
{
    private static readonly TestStationImageStorageService ImageStorage = new();

    [Test]
    public async Task CreateStationStoresImageUrls()
    {
        await using var context = SeatFlowTestData.CreateContext();

        var result = await new CreateStationCommandHandler(context, ImageStorage).Handle(
            new CreateStationCommand(
                "nvl",
                "Ben Nguyen Van Linh",
                "Q7, TP.HCM",
                10.7285m,
                106.7006m,
                ImageUrls:
                [
                    "https://cdn.example.com/stations/nvl-main.jpg",
                    "https://cdn.example.com/stations/nvl-pier.jpg"
                ]),
            CancellationToken.None);

        result.ImageUrl.ShouldBe("https://cdn.example.com/stations/nvl-main.jpg");
        result.ImageUrls.ShouldBe([
            "https://cdn.example.com/stations/nvl-main.jpg",
            "https://cdn.example.com/stations/nvl-pier.jpg"
        ]);
        context.Stations.Single().ImageUrl.ShouldBe("https://cdn.example.com/stations/nvl-main.jpg");
        context.StationImages.Count().ShouldBe(2);
    }

    [Test]
    public async Task CreateStationUploadsImageFiles()
    {
        await using var context = SeatFlowTestData.CreateContext();

        await using var file = CreateImageFile();
        var result = await new CreateStationCommandHandler(context, ImageStorage).Handle(
            new CreateStationCommand(
                "bd",
                "Bach Dang",
                "Q1, TP.HCM",
                10.773m,
                106.706m,
                ImageFiles: [new StationImageFileRequest("pier.jpg", "image/jpeg", file.Length, file)]),
            CancellationToken.None);

        result.ImageUrls.Count.ShouldBe(1);
        result.ImageUrl.ShouldStartWith("https://example.test/stations/");
        var stored = context.StationImages.Single();
        stored.Url.ShouldStartWith("https://example.test/stations/");
        stored.PublicId.ShouldNotBeNullOrWhiteSpace();
        stored.IsPrimary.ShouldBeTrue();
    }

    [Test]
    public async Task CreateStationMixesUrlsAndFiles()
    {
        await using var context = SeatFlowTestData.CreateContext();

        await using var file = CreateImageFile();
        var result = await new CreateStationCommandHandler(context, ImageStorage).Handle(
            new CreateStationCommand(
                "tt",
                "Thu Thiem",
                "Q2, TP.HCM",
                10.78m,
                106.72m,
                ImageUrl: "https://cdn.example.com/stations/tt-main.jpg",
                ImageFiles: [new StationImageFileRequest("extra.png", "image/png", file.Length, file)]),
            CancellationToken.None);

        // URL ảnh chính đứng đầu, file upload nối tiếp.
        result.ImageUrls.Count.ShouldBe(2);
        result.ImageUrl.ShouldBe("https://cdn.example.com/stations/tt-main.jpg");
        context.StationImages.Count().ShouldBe(2);
        context.StationImages.Count(x => x.PublicId != null).ShouldBe(1);
    }

    [Test]
    public async Task CreateStationRejectsInvalidContentType()
    {
        await using var context = SeatFlowTestData.CreateContext();

        await using var file = CreateImageFile();
        var act = async () => await new CreateStationCommandHandler(context, ImageStorage).Handle(
            new CreateStationCommand(
                "xx",
                "Invalid",
                null,
                null,
                null,
                ImageFiles: [new StationImageFileRequest("doc.pdf", "application/pdf", file.Length, file)]),
            CancellationToken.None);

        await act.ShouldThrowAsync<ValidationException>();
    }

    [Test]
    public void ValidatorRejectsMoreThanSixImages()
    {
        var command = new CreateStationCommand(
            "many",
            "Many Images",
            null,
            null,
            null,
            ImageUrls: Enumerable.Range(1, 7)
                .Select(i => $"https://cdn.example.com/stations/img-{i}.jpg")
                .ToArray());

        var result = new CreateStationCommandValidator().Validate(command);

        result.IsValid.ShouldBeFalse();
    }

    [Test]
    public async Task UpdateStationReplacesImageUrls()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var station = new Station
        {
            StationCode = "BD",
            StationName = "Bach Dang",
            Address = "Q1, TP.HCM",
            Latitude = 10.773m,
            Longitude = 106.706m,
            Status = StationStatus.Active,
            ImageUrl = "https://cdn.example.com/stations/old.jpg"
        };
        station.Images.Add(new StationImage
        {
            StationId = station.Id,
            Url = "https://cdn.example.com/stations/old.jpg",
            DisplayOrder = 1,
            IsPrimary = true
        });
        context.Stations.Add(station);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var result = await new UpdateStationCommandHandler(context, ImageStorage).Handle(
            new UpdateStationCommand(
                station.Id,
                "Bach Dang",
                "Q1, TP.HCM",
                "Updated",
                10.773m,
                106.706m,
                StationStatus.Active,
                null,
                [
                    "https://cdn.example.com/stations/new-main.jpg",
                    "https://cdn.example.com/stations/new-ticket-counter.jpg"
                ],
                true,
                true,
                true),
            CancellationToken.None);

        result.ImageUrl.ShouldBe("https://cdn.example.com/stations/new-main.jpg");
        result.ImageUrls.ShouldBe([
            "https://cdn.example.com/stations/new-main.jpg",
            "https://cdn.example.com/stations/new-ticket-counter.jpg"
        ]);
        (await context.Stations.SingleAsync(x => x.Id == station.Id))
            .ImageUrl
            .ShouldBe("https://cdn.example.com/stations/new-main.jpg");
        context.StationImages.Count(x => x.StationId == station.Id).ShouldBe(2);
    }

    [Test]
    public async Task UpdateStationReplacesWithUploadedFiles()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var station = new Station
        {
            StationCode = "TD",
            StationName = "Thu Duc",
            Status = StationStatus.Active,
            ImageUrl = "https://cdn.example.com/stations/old.jpg"
        };
        station.Images.Add(new StationImage
        {
            StationId = station.Id,
            Url = "https://cdn.example.com/stations/old.jpg",
            DisplayOrder = 1,
            IsPrimary = true
        });
        context.Stations.Add(station);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        await using var file = CreateImageFile();
        var result = await new UpdateStationCommandHandler(context, ImageStorage).Handle(
            new UpdateStationCommand(
                station.Id,
                "Thu Duc",
                null,
                null,
                null,
                null,
                StationStatus.Active,
                null,
                null,
                null,
                null,
                null,
                ImageFiles: [new StationImageFileRequest("new.jpg", "image/jpeg", file.Length, file)]),
            CancellationToken.None);

        result.ImageUrls.Count.ShouldBe(1);
        result.ImageUrl.ShouldStartWith("https://example.test/stations/");
        var images = context.StationImages.Where(x => x.StationId == station.Id).ToList();
        images.Count.ShouldBe(1);
        images.Single().PublicId.ShouldNotBeNullOrWhiteSpace();
    }

    private static MemoryStream CreateImageFile() => new([0x1, 0x2, 0x3, 0x4]);
}
