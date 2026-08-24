using FluentValidation.TestHelper;
using NUnit.Framework;
using SaigonWaterbus.Application.Promotions;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Promotions;

public class CreatePromotionCommandValidatorTests
{
    private static CreatePromotionCommand ValidCommand() => new(
        PromotionCode: "TEST" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        PromotionName: "Ten khuyen mai",
        PromotionType: PromotionType.Percent,
        DiscountValue: 10,
        MaxDiscountAmount: null,
        MinOrderValue: null,
        ValidFrom: DateTimeOffset.UtcNow.AddMinutes(1),
        ValidTo: DateTimeOffset.UtcNow.AddDays(30),
        UsageLimit: null,
        MaxUsesPerAccount: null,
        BudgetCap: null);

    [Test]
    public void ValidFrom_TrongQuaKhu_Fail()
    {
        var validator = new CreatePromotionCommandValidator();
        var cmd = ValidCommand() with { ValidFrom = DateTimeOffset.UtcNow.AddDays(-1) };

        var result = validator.TestValidate(cmd);

        result.ShouldHaveValidationErrorFor(x => x.ValidFrom)
            .WithErrorMessage("ValidFrom không được ở quá khứ.");
    }

    [Test]
    public void ValidFrom_NgayHienTai_Pass()
    {
        var validator = new CreatePromotionCommandValidator();
        var cmd = ValidCommand() with { ValidFrom = DateTimeOffset.UtcNow.AddSeconds(-2) };

        var result = validator.TestValidate(cmd);

        result.ShouldNotHaveValidationErrorFor(x => x.ValidFrom);
    }

    [Test]
    public void ValidFrom_TuongLai_Pass()
    {
        var validator = new CreatePromotionCommandValidator();
        var cmd = ValidCommand() with { ValidFrom = DateTimeOffset.UtcNow.AddDays(1) };

        var result = validator.TestValidate(cmd);

        result.ShouldNotHaveValidationErrorFor(x => x.ValidFrom);
    }

    [Test]
    public void ValidFrom_QuaKhuVaValidToNhoHon_PriPastOverride()
    {
        var validator = new CreatePromotionCommandValidator();
        var cmd = ValidCommand() with
        {
            ValidFrom = DateTimeOffset.UtcNow.AddDays(-1),
            ValidTo = DateTimeOffset.UtcNow.AddDays(-1).AddHours(2),
        };

        var result = validator.TestValidate(cmd);

        result.ShouldHaveValidationErrorFor(x => x.ValidFrom)
            .WithErrorMessage("ValidFrom không được ở quá khứ.");
    }
}
