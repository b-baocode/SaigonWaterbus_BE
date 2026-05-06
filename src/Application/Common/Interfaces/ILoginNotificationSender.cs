namespace SaigonWaterbus.Application.Common.Interfaces;

public interface ILoginNotificationSender
{
    Task SendLoginSucceededAsync(LoginNotification notification, CancellationToken cancellationToken);
}

public sealed record LoginNotification(
    string Email,
    string FullName,
    string Provider,
    DateTimeOffset LoggedInAt,
    string? DeviceInfo);
