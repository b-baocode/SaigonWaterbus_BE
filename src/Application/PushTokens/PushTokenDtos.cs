using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.PushTokens;

public sealed record RegisterPushTokenCommand(
    string ExpoPushToken,
    PushPlatform Platform,
    string? DeviceId = null,
    string? AppVersion = null) : MediatR.IRequest<RegisterPushTokenResult>;

public sealed record RegisterPushTokenResult(Guid Id, bool AlreadyRegistered);

public sealed record UnregisterPushTokenCommand(Guid Id) : MediatR.IRequest<bool>;

public sealed record DisableMyPushTokensCommand : MediatR.IRequest<int>;
