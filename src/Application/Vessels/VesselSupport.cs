using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Vessels;

internal static class VesselSupport
{
    public static async Task<User> EnsureCurrentUserCanViewVesselsAsync(
        IApplicationDbContext context,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(
            context,
            userContext,
            cancellationToken);

        if (AuthSupport.IsAdmin(actor) || AuthSupport.IsManager(actor) || AuthSupport.IsStaff(actor))
        {
            return actor;
        }

        throw new ForbiddenAccessException();
    }

    public static async Task EnsureCurrentUserCanManageVesselsAsync(
        IApplicationDbContext context,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        await AuthSupport.EnsureCurrentUserIsAdminAsync(context, userContext, cancellationToken);
    }

    public static bool CanManageVessels(User actor) =>
        AuthSupport.IsAdmin(actor);

    public static IQueryable<Vessel> ApplyVisibilityFilter(
        IQueryable<Vessel> query,
        User actor)
    {
        return CanManageVessels(actor)
            ? query
            : query.Where(x => x.Status == VesselStatus.Active);
    }

    public static string NormalizeCode(string code) =>
        code.Trim().ToUpperInvariant();

    public static string? NormalizeRegistrationNumber(string? registrationNumber) =>
        string.IsNullOrWhiteSpace(registrationNumber)
            ? null
            : registrationNumber.Trim().ToUpperInvariant();

    public static VesselDto CreateDto(Vessel vessel, int generatedSeatCount = 0)
    {
        var service = vessel.WaterbusService;
        var serviceDto = new VesselWaterbusServiceDto(service.Id, service.Code, service.Name);

        var description = !string.IsNullOrWhiteSpace(vessel.Description)
            ? vessel.Description
            : service.Description;

        return new VesselDto(
            vessel.Id,
            serviceDto,
            vessel.Code,
            vessel.RegistrationNumber,
            vessel.Name,
            vessel.Status,
            vessel.SeatCount,
            generatedSeatCount,
            vessel.NumberOfDecks,
            vessel.SeatsConfigured,
            vessel.MaxSpeedKmh,
            vessel.YearBuilt,
            vessel.ImageUrl ?? string.Empty,
            description);
    }

    public static void EnsureValidImage(
        string propertyPrefix,
        string? fileName,
        string? contentType,
        long? length,
        IVesselImageStorageService vesselImageStorage)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw AuthSupport.CreateValidationException($"{propertyPrefix}FileName", "Tên file ảnh tàu là bắt buộc.");
        }

        if (!length.HasValue || length <= 0)
        {
            throw AuthSupport.CreateValidationException($"{propertyPrefix}Length", "Ảnh tàu là bắt buộc.");
        }

        if (length > vesselImageStorage.MaxImageBytes)
        {
            throw AuthSupport.CreateValidationException(
                $"{propertyPrefix}Length",
                $"Ảnh tàu không được vượt quá {vesselImageStorage.MaxImageBytes / 1024 / 1024} MB.");
        }

        if (string.IsNullOrWhiteSpace(contentType)
            || !vesselImageStorage.AllowedImageContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
        {
            throw AuthSupport.CreateValidationException(
                $"{propertyPrefix}ContentType",
                "Ảnh tàu chỉ hỗ trợ JPEG, PNG hoặc WebP.");
        }
    }
}
