using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Seats;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Application.Boats;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Boats;

public class BoatSeatFlowIntegrationTests
{
    [TestCase(SeatSetupType.FullStandard)]
    [TestCase(SeatSetupType.StandardAndVip)]
    public async Task CreateBoatDoesNotRequireService(SeatSetupType setupType)
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);

        var result = await CreateBoatUseCase(context, userContext).ExecuteAsync(
            CreateRequest(setupType),
            CancellationToken.None);

        result.SeatSetupType.ShouldBe(setupType);
        result.SeatCount.ShouldBe(0);
        result.Status.ShouldBe(BoatStatus.Inactive);
        result.SeatsConfigured.ShouldBeFalse();
    }

    [Test]
    public async Task UpdateSeatSetupTypeBeforeLayoutIsAllowed()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard);
        context.Add(boat);
        await context.SaveChangesAsync();

        var result = await UpdateBoatUseCase(context, userContext).ExecuteAsync(
            new UpdateBoatRequest(
                boat.Id,
                SeatSetupType: SeatSetupType.StandardAndVip),
            CancellationToken.None);

        result.SeatSetupType.ShouldBe(SeatSetupType.StandardAndVip);
    }

    [Test]
    public async Task UpdateSeatSetupTypeRejectsBoatWithExistingLayout()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var boat = SeatFlowTestData.Boat(
            SeatSetupType.FullStandard,
            seatsConfigured: true);
        AddSeats(boat, boat.SeatCount);
        context.Add(boat);
        await context.SaveChangesAsync();

        await Should.ThrowAsync<ValidationException>(() =>
            UpdateBoatUseCase(context, userContext).ExecuteAsync(
                new UpdateBoatRequest(
                    boat.Id,
                    SeatSetupType: SeatSetupType.StandardAndVip),
                CancellationToken.None));

        boat.SeatSetupType.ShouldBe(SeatSetupType.FullStandard);
    }

    [Test]
    public async Task UpdateBoatUpdatesEditableProfileFields()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard);
        context.Add(boat);
        await context.SaveChangesAsync();

        var result = await UpdateBoatUseCase(context, userContext).ExecuteAsync(
            new UpdateBoatRequest(
                boat.Id,
                Name: "Waterbus updated"),
            CancellationToken.None);

        result.Name.ShouldBe("Waterbus updated");
    }

    [Test]
    public async Task ActivateRejectsBoatWithoutConfiguredSeats()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var boat = SeatFlowTestData.Boat(SeatSetupType.StandardAndVip);
        context.Add(boat);
        await context.SaveChangesAsync();

        await Should.ThrowAsync<ValidationException>(() =>
            new UpdateBoatStatusRequestUseCase(
                    context,
                    userContext,
                    new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero)))
                .ExecuteAsync(
                new UpdateBoatStatusRequest(boat.Id, BoatStatus.Active),
                CancellationToken.None));

        boat.Status.ShouldBe(BoatStatus.Inactive);
    }

    [Test]
    public async Task ActivateAcceptsConfiguredBoatWithoutService()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var now = new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero);
        var boat = SeatFlowTestData.Boat(
            SeatSetupType.StandardAndVip,
            seatsConfigured: true);
        SeatFlowTestData.AddRequiredDocuments(boat, now);
        AddSeats(boat, boat.SeatCount);
        context.Add(boat);
        await context.SaveChangesAsync();

        var result = await new UpdateBoatStatusRequestUseCase(context, userContext, new FixedTimeProvider(now))
            .ExecuteAsync(
                new UpdateBoatStatusRequest(boat.Id, BoatStatus.Active),
                CancellationToken.None);

        result.Status.ShouldBe(BoatStatus.Active);
        result.IsReadyForOperation.ShouldBeTrue();
    }

    [Test]
    public async Task ActivateRejectsConfiguredBoatWithoutRequiredDocuments()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var boat = SeatFlowTestData.Boat(
            SeatSetupType.StandardAndVip,
            seatsConfigured: true);
        AddSeats(boat, boat.SeatCount);
        context.Add(boat);
        await context.SaveChangesAsync();

        await Should.ThrowAsync<ValidationException>(() =>
            new UpdateBoatStatusRequestUseCase(
                    context,
                    userContext,
                    new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero)))
                .ExecuteAsync(
                    new UpdateBoatStatusRequest(boat.Id, BoatStatus.Active),
                    CancellationToken.None));

        boat.Status.ShouldBe(BoatStatus.Inactive);
    }

    [Test]
    public async Task ActivateRejectsUnderMaintenanceBoatUntilInspectionIsUpdated()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var maintenanceStartedAt = new DateTimeOffset(2030, 1, 10, 1, 0, 0, TimeSpan.Zero);
        var boat = SeatFlowTestData.Boat(
            SeatSetupType.StandardAndVip,
            seatsConfigured: true,
            status: BoatStatus.UnderMaintenance);
        boat.MaintenanceStartedAt = maintenanceStartedAt;
        SeatFlowTestData.AddRequiredDocuments(boat, maintenanceStartedAt.AddDays(-1));
        AddSeats(boat, boat.SeatCount);
        context.Add(boat);
        await context.SaveChangesAsync();

        var staleDto = await new GetBoatByIdRequestUseCase(context, userContext)
            .ExecuteAsync(new GetBoatByIdRequest(boat.Id), CancellationToken.None);
        staleDto.MaintenanceStartedAt.ShouldBe(maintenanceStartedAt);
        staleDto.DocumentsRequireRefresh.ShouldBeTrue();

        var staleDocuments = await new GetBoatDocumentsRequestUseCase(
                context,
                userContext,
                new TestBoatDocumentStorageService())
            .ExecuteAsync(new GetBoatDocumentsRequest(boat.Id), CancellationToken.None);
        staleDocuments.Single(x => x.Type == BoatDocumentType.Inspection).RequiresRefresh.ShouldBeTrue();
        staleDocuments.Single(x => x.Type == BoatDocumentType.Inspection).FileUrl
            .ShouldStartWith("https://example.test/signed-boat-documents/");

        await Should.ThrowAsync<ValidationException>(() =>
            new UpdateBoatStatusRequestUseCase(
                    context,
                    userContext,
                    new FixedTimeProvider(maintenanceStartedAt.AddDays(1)))
                .ExecuteAsync(
                    new UpdateBoatStatusRequest(boat.Id, BoatStatus.Active),
                    CancellationToken.None));

        await using var file = CreateDocumentFile();
        var result = await new UpdateBoatDocumentRequestUseCase(
                context,
                userContext,
                new FixedTimeProvider(maintenanceStartedAt.AddMinutes(1)),
                new TestBoatDocumentStorageService())
            .ExecuteAsync(
                new UpdateBoatDocumentRequest(
                    boat.Id,
                    BoatDocumentType.Inspection,
                    new BoatDocumentFileRequest("inspection-after-maintenance.pdf", "application/pdf", file.Length, file)),
                CancellationToken.None);

        result.Type.ShouldBe(BoatDocumentType.Inspection);
        boat.Status.ShouldBe(BoatStatus.Active);

        var refreshedDto = await new GetBoatByIdRequestUseCase(context, userContext)
            .ExecuteAsync(new GetBoatByIdRequest(boat.Id), CancellationToken.None);
        refreshedDto.DocumentsRequireRefresh.ShouldBeFalse();

        var refreshedDocuments = await new GetBoatDocumentsRequestUseCase(
                context,
                userContext,
                new TestBoatDocumentStorageService())
            .ExecuteAsync(new GetBoatDocumentsRequest(boat.Id), CancellationToken.None);
        refreshedDocuments.Single(x => x.Type == BoatDocumentType.Inspection).RequiresRefresh.ShouldBeFalse();
    }

    [Test]
    public async Task GetBoatsCanSearchByBoatId()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var boat = SeatFlowTestData.Boat(SeatSetupType.StandardAndVip);
        context.Add(boat);
        await context.SaveChangesAsync();

        var result = await new GetBoatsRequestUseCase(context, userContext)
            .ExecuteAsync(new GetBoatsRequest(Search: boat.Id.ToString()), CancellationToken.None);

        result.Single().Id.ShouldBe(boat.Id);
    }

    [Test]
    public async Task CreateBoatStoresPrimaryImageUrl()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);

        var result = await CreateBoatUseCase(context, userContext).ExecuteAsync(
            new CreateBoatRequest(
                $"IMG_{Guid.NewGuid():N}"[..20],
                "Image boat",
                BoatStatus.Inactive,
                4,
                1,
                SeatSetupType: SeatSetupType.FullStandard,
                ImageUrls:
                [
                    "https://example.test/boats/main.jpg",
                    "https://example.test/boats/deck.jpg"
                ]),
            CancellationToken.None);

        result.ImageUrl.ShouldBe("https://example.test/boats/main.jpg");
        result.ImageUrls.ShouldBe([
            "https://example.test/boats/main.jpg",
            "https://example.test/boats/deck.jpg"
        ]);

        var boat = await context.Boats.SingleAsync(x => x.Id == result.Id);
        boat.ImageUrls.ShouldBe([
            "https://example.test/boats/main.jpg",
            "https://example.test/boats/deck.jpg"
        ]);
    }

    [Test]
    public async Task UpdateBoatDocumentReplacesTypeAndReturnsFourSlots()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var now = new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero);
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard);
        context.Add(boat);
        await context.SaveChangesAsync();
        await using var firstFile = CreateDocumentFile();
        await using var secondFile = CreateDocumentFile();
        var useCase = new UpdateBoatDocumentRequestUseCase(
            context,
            userContext,
            new FixedTimeProvider(now),
            new TestBoatDocumentStorageService());

        var firstResult = await useCase.ExecuteAsync(
            new UpdateBoatDocumentRequest(
                boat.Id,
                BoatDocumentType.Inspection,
                new BoatDocumentFileRequest("inspection-old.pdf", "application/pdf", firstFile.Length, firstFile),
                IssuedDate: new DateOnly(2029, 1, 1),
                ExpiryDate: new DateOnly(2030, 1, 1)),
            CancellationToken.None);

        var secondResult = await useCase.ExecuteAsync(
            new UpdateBoatDocumentRequest(
                boat.Id,
                BoatDocumentType.Inspection,
                new BoatDocumentFileRequest("inspection-new.pdf", "application/pdf", secondFile.Length, secondFile),
                IssuedDate: new DateOnly(2030, 1, 1),
                ExpiryDate: new DateOnly(2031, 1, 1)),
            CancellationToken.None);

        firstResult.Id.ShouldNotBe(secondResult.Id);

        var documents = await new GetBoatDocumentsRequestUseCase(
                context,
                userContext,
                new TestBoatDocumentStorageService())
            .ExecuteAsync(new GetBoatDocumentsRequest(boat.Id), CancellationToken.None);

        documents.Count.ShouldBe(4);
        var inspectionDocument = documents.Single(x => x.Type == BoatDocumentType.Inspection);
        inspectionDocument.FileName.ShouldBe("inspection-new.pdf");
        inspectionDocument.FileUrl.ShouldStartWith("https://example.test/signed-boat-documents/");
        documents.Count(x => x.IsUploaded).ShouldBe(1);
    }

    [Test]
    public async Task UpdateBoatDocumentAutoActivatesConfiguredBoatWhenFourthDocumentIsUploaded()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var now = new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero);
        var boat = SeatFlowTestData.Boat(
            SeatSetupType.FullStandard,
            seatsConfigured: true,
            status: BoatStatus.Inactive);
        SeatFlowTestData.AddRequiredDocuments(boat, now.AddDays(-1));
        boat.Documents = boat.Documents
            .Where(x => x.Type != BoatDocumentType.OperationLicense)
            .ToArray();
        AddSeats(boat, boat.SeatCount);
        context.Add(boat);
        await context.SaveChangesAsync();
        await using var file = CreateDocumentFile();

        var result = await new UpdateBoatDocumentRequestUseCase(
                context,
                userContext,
                new FixedTimeProvider(now),
                new TestBoatDocumentStorageService())
            .ExecuteAsync(
                new UpdateBoatDocumentRequest(
                    boat.Id,
                    BoatDocumentType.OperationLicense,
                    new BoatDocumentFileRequest("operation-license.pdf", "application/pdf", file.Length, file)),
                CancellationToken.None);

        result.Type.ShouldBe(BoatDocumentType.OperationLicense);
        boat.Status.ShouldBe(BoatStatus.Active);
    }

    [Test]
    public async Task CreateBoatUploadsImageFileAndStoresReturnedUrl()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        await using var file = CreateImageFile();

        var result = await CreateBoatUseCase(context, userContext).ExecuteAsync(
            new CreateBoatRequest(
                $"UPL_{Guid.NewGuid():N}"[..20],
                "Upload image boat",
                BoatStatus.Inactive,
                4,
                1,
                SeatSetupType: SeatSetupType.FullStandard,
                ImageFiles: [new BoatImageFileRequest("boat.jpg", "image/jpeg", file.Length, file)]),
            CancellationToken.None);

        var expectedUrl = $"https://example.test/boats/{result.Id}/{result.Id:N}";
        result.ImageUrl.ShouldBe(expectedUrl);
        result.ImageUrls.ShouldBe([expectedUrl]);

        var boat = await context.Boats.SingleAsync(x => x.Id == result.Id);
        boat.ImageUrl.ShouldBe(expectedUrl);
        boat.ImagePublicId.ShouldBe(result.Id.ToString("N"));
    }

    [Test]
    public async Task CreateBoatUploadsThreeImageFilesAndStoresReturnedUrls()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        await using var firstFile = CreateImageFile();
        await using var secondFile = CreateImageFile();
        await using var thirdFile = CreateImageFile();

        var result = await CreateBoatUseCase(context, userContext).ExecuteAsync(
            new CreateBoatRequest(
                $"UP3_{Guid.NewGuid():N}"[..20],
                "Upload three image boat",
                BoatStatus.Inactive,
                4,
                1,
                SeatSetupType: SeatSetupType.FullStandard,
                ImageFiles:
                [
                    new BoatImageFileRequest("boat-1.jpg", "image/jpeg", firstFile.Length, firstFile),
                    new BoatImageFileRequest("boat-2.png", "image/png", secondFile.Length, secondFile),
                    new BoatImageFileRequest("boat-3.webp", "image/webp", thirdFile.Length, thirdFile)
                ]),
            CancellationToken.None);

        result.ImageUrls.Count.ShouldBe(3);
        result.ImageUrls.Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(3);
        result.ImageUrls.ShouldAllBe(url => url.StartsWith($"https://example.test/boats/{result.Id}/"));
        result.ImageUrl.ShouldBe(result.ImageUrls.First());

        var boat = await context.Boats.SingleAsync(x => x.Id == result.Id);
        boat.ImageUrls.Length.ShouldBe(3);
        boat.ImagePublicId.ShouldBeNull();
    }

    [Test]
    public void CreateBoatValidatorRejectsMoreThanThreeImages()
    {
        var request = new CreateBoatRequest(
            $"MAX_{Guid.NewGuid():N}"[..20],
            "Too many image boat",
            BoatStatus.Inactive,
            4,
            1,
            SeatSetupType: SeatSetupType.FullStandard,
            ImageUrls:
            [
                "https://example.test/boats/1.jpg",
                "https://example.test/boats/2.jpg",
                "https://example.test/boats/3.jpg",
                "https://example.test/boats/4.jpg"
            ]);

        var result = new CreateBoatRequestValidator().Validate(request);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(error => error.ErrorMessage == "Mỗi tàu chỉ được gửi tối đa 3 ảnh.");
    }

    [Test]
    public async Task CreateBoatRejectsUnsupportedImageContentType()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        await using var file = CreateImageFile();

        var act = async () => await CreateBoatUseCase(context, userContext).ExecuteAsync(
            new CreateBoatRequest(
                $"BAD_{Guid.NewGuid():N}"[..20],
                "Invalid image boat",
                BoatStatus.Inactive,
                4,
                1,
                SeatSetupType: SeatSetupType.FullStandard,
                ImageFiles: [new BoatImageFileRequest("boat.pdf", "application/pdf", file.Length, file)]),
            CancellationToken.None);

        await act.ShouldThrowAsync<ValidationException>();
    }

    [Test]
    public async Task UpdateBoatReplacesImageUrls()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard);
        boat.ImageUrl = "https://example.test/boats/old-main.jpg";
        context.Add(boat);
        await context.SaveChangesAsync();

        var result = await UpdateBoatUseCase(context, userContext).ExecuteAsync(
            new UpdateBoatRequest(
                boat.Id,
                ImageUrls:
                [
                    "https://example.test/boats/new-main.jpg",
                    "https://example.test/boats/new-deck.jpg"
                ]),
            CancellationToken.None);

        result.ImageUrl.ShouldBe("https://example.test/boats/new-main.jpg");
        result.ImageUrls.ShouldBe([
            "https://example.test/boats/new-main.jpg",
            "https://example.test/boats/new-deck.jpg"
        ]);

        var updatedBoat = await context.Boats.SingleAsync(x => x.Id == boat.Id);
        updatedBoat.ImageUrls.ShouldBe([
            "https://example.test/boats/new-main.jpg",
            "https://example.test/boats/new-deck.jpg"
        ]);
    }

    [Test]
    public async Task UpdateBoatUploadsImageFileAndStoresReturnedUrl()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard);
        boat.ImageUrl = "https://example.test/boats/old-main.jpg";
        context.Add(boat);
        await context.SaveChangesAsync();
        await using var file = CreateImageFile();

        var result = await UpdateBoatUseCase(context, userContext).ExecuteAsync(
            new UpdateBoatRequest(
                boat.Id,
                ImageFiles: [new BoatImageFileRequest("new-boat.png", "image/png", file.Length, file)]),
            CancellationToken.None);

        var expectedUrl = $"https://example.test/boats/{boat.Id}/{boat.Id:N}";
        result.ImageUrl.ShouldBe(expectedUrl);
        result.ImageUrls.ShouldBe([expectedUrl]);

        var updatedBoat = await context.Boats.SingleAsync(x => x.Id == boat.Id);
        updatedBoat.ImageUrl.ShouldBe(expectedUrl);
        updatedBoat.ImagePublicId.ShouldBe(boat.Id.ToString("N"));
    }

    [Test]
    public async Task UpdateBoatAcceptsSwaggerPayloadWithImageUrl()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var boat = new Boat
        {
            Code = "WB_005",
            Name = "Waterbus 05",
            Status = BoatStatus.Inactive,
            SeatCount = 80,
            NumberOfDecks = 1,
            SeatSetupType = SeatSetupType.FullStandard,
            RegistrationNumber = "VN-005",
            ImageUrl = "https://example.test/boats/old-main.jpg"
        };
        context.Add(boat);
        await context.SaveChangesAsync();

        var result = await UpdateBoatUseCase(context, userContext).ExecuteAsync(
            new UpdateBoatRequest(
                boat.Id,
                Code: "WB_006",
                Name: "Waterbus 06",
                NumberOfDecks: 1,
                RegistrationNumber: "VN-006",
                MaxSpeedKmh: 50,
                YearBuilt: 2026,
                Description: "abcdef",
                ImageUrl: "https://i.pinimg.com/236x/bd/e3/14/bde3147fb7e955639478c55a0e050cd9.jpg",
                SeatSetupType: SeatSetupType.FullStandard),
            CancellationToken.None);

        result.Code.ShouldBe("WB_006");
        result.Name.ShouldBe("Waterbus 06");
        result.SeatCount.ShouldBe(80);
        result.NumberOfDecks.ShouldBe(1);
        result.RegistrationNumber.ShouldBe("VN-006");
        result.MaxSpeedKmh.ShouldBe(50);
        result.YearBuilt.ShouldBe(2026);
        result.Description.ShouldBe("abcdef");
        result.ImageUrl.ShouldBe("https://i.pinimg.com/236x/bd/e3/14/bde3147fb7e955639478c55a0e050cd9.jpg");
        result.ImageUrls.ShouldBe(["https://i.pinimg.com/236x/bd/e3/14/bde3147fb7e955639478c55a0e050cd9.jpg"]);
    }

    private static CreateBoatRequestUseCase CreateBoatUseCase(
        Infrastructure.Data.ApplicationDbContext context,
        TestUserContext userContext) =>
        new(
            context,
            userContext,
            new TestDatabaseExceptionClassifier(),
            new TestBoatImageStorageService());

    private static UpdateBoatRequestUseCase UpdateBoatUseCase(
        Infrastructure.Data.ApplicationDbContext context,
        TestUserContext userContext) =>
        new(
            context,
            userContext,
            new TestDatabaseExceptionClassifier(),
            new TestBoatImageStorageService());

    private static CreateBoatRequest CreateRequest(SeatSetupType setupType) =>
        new(
            $"NEW_{Guid.NewGuid():N}"[..20],
            "New boat",
            BoatStatus.Inactive,
            4,
            1,
            SeatSetupType: setupType);

    private static void AddSeats(Boat boat, int count)
    {
        for (var index = 0; index < count; index++)
        {
            var row = SeatSupport.RowLabel(index / 10);
            var column = (index % 10) + 1;
            boat.Seats.Add(new Seat
            {
                BoatId = boat.Id,
                Code = SeatSupport.SeatCode(1, row, column),
                SeatTypeCode = "STANDARD",
                Deck = 1,
                Row = row,
                Column = column,
                IsActive = true
            });
        }
    }

    private static MemoryStream CreateImageFile() => new([0x1, 0x2, 0x3, 0x4]);

    private static MemoryStream CreateDocumentFile() => new([0x25, 0x50, 0x44, 0x46]);
}
