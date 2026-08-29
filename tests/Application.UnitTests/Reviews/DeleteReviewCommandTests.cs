using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Reviews;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Reviews;

public class DeleteReviewCommandTests
{
    [Test]
    public void ValidatorRejectsEmptyReviewId()
    {
        var result = new DeleteReviewCommandValidator()
            .Validate(new DeleteReviewCommand(Guid.Empty));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(error => error.PropertyName == nameof(DeleteReviewCommand.ReviewId));
    }

    [Test]
    public async Task HandlerDeletesExistingReviewOnly()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var review = new Review
        {
            CustomerId = Guid.NewGuid(),
            Rating = 5,
            Comment = "Good trip"
        };
        context.Set<Review>().Add(review);
        await context.SaveChangesAsync();

        await new DeleteReviewCommandHandler(context)
            .Handle(new DeleteReviewCommand(review.Id), CancellationToken.None);

        (await context.Set<Review>().AnyAsync(x => x.Id == review.Id)).ShouldBeFalse();
    }

    [Test]
    public async Task HandlerReturnsNotFoundForUnknownReview()
    {
        await using var context = SeatFlowTestData.CreateContext();

        var action = async () => await new DeleteReviewCommandHandler(context)
            .Handle(new DeleteReviewCommand(Guid.NewGuid()), CancellationToken.None);

        var exception = await action.ShouldThrowAsync<NotFoundException>();
        exception.Message.ShouldBe("Review not found.");
    }
}
