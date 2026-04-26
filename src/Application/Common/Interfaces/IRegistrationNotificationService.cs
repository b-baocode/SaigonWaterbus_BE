namespace SaigonWaterbus.Application.Common.Interfaces;

public interface IRegistrationNotificationService
{
    Task SendRegistrationCreatedAsync(
        string recipientEmail,
        string recipientName,
        string roleName,
        CancellationToken cancellationToken);
}
