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
                ],
                OpeningTime: new TimeOnly(6, 0),
                ClosingTime: new TimeOnly(22, 0)),
            CancellationToken.None);

        result.ImageUrl.ShouldBe("https://cdn.example.com/stations/nvl-main.jpg");
        result.OpeningTime.ShouldBe(new TimeOnly(6, 0));
        result.ClosingTime.ShouldBe(new TimeOnly(22, 0));
        result.IsWaterbusStation.ShouldBeTrue();
        result.ImageUrls.ShouldBe([
            "https://cdn.example.com/stations/nvl-main.jpg",
            "https://cdn.example.com/stations/nvl-pier.jpg"
        ]);
        context.Stations.Single().OpeningTime.ShouldBe(new TimeOnly(6, 0));
        context.Stations.Single().ClosingTime.ShouldBe(new TimeOnly(22, 0));
        context.Stations.Single().IsWaterbusStation.ShouldBeTrue();
        context.Stations.Single().ImageUrl.ShouldBe("https://cdn.example.com/stations/nvl-main.jpg");
        context.Stations.Single().ImageUrls.ShouldBe([
            "https://cdn.example.com/stations/nvl-main.jpg",
            "https://cdn.example.com/stations/nvl-pier.jpg"
        ]);
    }

    [Test]
    public async Task CreateStationStoresUploadedImageFiles()
    {
        await using var context = SeatFlowTestData.CreateContext();

        await using var firstFile = CreateImageFile();
        await using var secondFile = CreateImageFile();
        var result = await new CreateStationCommandHandler(context, ImageStorage).Handle(
            new CreateStationCommand(
                "bd",
                "Bach Dang",
                "Q1, TP.HCM",
                10.773m,
                106.706m,
                ImageFiles:
                [
                    new StationImageFileRequest("pier.jpg", "image/jpeg", firstFile.Length, firstFile),
                    new StationImageFileRequest("ticket-counter.png", "image/png", secondFile.Length, secondFile)
                ]),
            CancellationToken.None);

        result.ImageUrl.ShouldStartWith("https://example.test/stations/");
        result.ImageUrls.Count.ShouldBe(2);
        result.ImageUrls.ShouldContain(result.ImageUrl!);
        context.Stations.Single().ImageUrl.ShouldBe(result.ImageUrl);
        context.Stations.Single().ImageUrls.Length.ShouldBe(2);
    }

    [Test]
    public async Task CreateStationStoresMixedUrlsAndFiles()
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

        result.ImageUrl.ShouldBe("https://cdn.example.com/stations/tt-main.jpg");
        result.ImageUrls.Count.ShouldBe(2);
        result.ImageUrls.ShouldContain("https://cdn.example.com/stations/tt-main.jpg");
        result.ImageUrls.Last().ShouldStartWith("https://example.test/stations/");
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
                true,
                OpeningTime: new TimeOnly(6, 30),
                ClosingTime: new TimeOnly(21, 30),
                IsWaterbusStation: false),
            CancellationToken.None);

        result.OpeningTime.ShouldBe(new TimeOnly(6, 30));
        result.ClosingTime.ShouldBe(new TimeOnly(21, 30));
        result.IsWaterbusStation.ShouldBeFalse();
        result.ImageUrl.ShouldBe("https://cdn.example.com/stations/new-main.jpg");
        result.ImageUrls.ShouldBe([
            "https://cdn.example.com/stations/new-main.jpg",
            "https://cdn.example.com/stations/new-ticket-counter.jpg"
        ]);
        var updatedStation = await context.Stations.SingleAsync(x => x.Id == station.Id);
        updatedStation.OpeningTime.ShouldBe(new TimeOnly(6, 30));
        updatedStation.ClosingTime.ShouldBe(new TimeOnly(21, 30));
        updatedStation.IsWaterbusStation.ShouldBeFalse();
        updatedStation.ImageUrl.ShouldBe("https://cdn.example.com/stations/new-main.jpg");
        updatedStation.ImageUrls.ShouldBe([
            "https://cdn.example.com/stations/new-main.jpg",
            "https://cdn.example.com/stations/new-ticket-counter.jpg"
        ]);
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

        result.ImageUrl.ShouldStartWith("https://example.test/stations/");
        result.ImageUrls.ShouldBe([result.ImageUrl!]);
        (await context.Stations.SingleAsync(x => x.Id == station.Id))
            .ImageUrl
            .ShouldBe(result.ImageUrl);
    }

    private static MemoryStream CreateImageFile() => new([0x1, 0x2, 0x3, 0x4]);
}
