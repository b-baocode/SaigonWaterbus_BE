using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Push;

public sealed class ExpoPushNotificationSender : IPushNotificationSender
{
    public const string HttpClientName = "ExpoPush";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IApplicationDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly ExpoPushOptions _options;
    private readonly ILogger<ExpoPushNotificationSender> _logger;

    public ExpoPushNotificationSender(
        IHttpClientFactory httpClientFactory,
        IApplicationDbContext dbContext,
        TimeProvider timeProvider,
        IOptions<ExpoPushOptions> options,
        ILogger<ExpoPushNotificationSender> logger)
    {
        _httpClientFactory = httpClientFactory;
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(
        Guid userId,
        PushNotificationPayload payload,
        CancellationToken cancellationToken)
    {
        var tokens = await _dbContext.Set<UserPushToken>()
            .Where(t => t.UserId == userId && t.IsActive)
            .Select(t => t.ExpoPushToken)
            .ToListAsync(cancellationToken);

        if (tokens.Count == 0) return;

        var client = _httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = _options.HttpTimeout;

        if (!string.IsNullOrWhiteSpace(_options.AccessToken))
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        }

        var messages = tokens.Select(token => new ExpoPushMessage(
            To: token,
            Title: payload.Title,
            Body: payload.Body,
            Data: new ExpoPushData(
                Type: payload.Type,
                RelatedEntityType: payload.RelatedEntityType,
                RelatedEntityId: payload.RelatedEntityId,
                DeepLink: payload.DeepLink),
            ChannelId: payload.ChannelId,
            Priority: payload.Priority,
            Sound: "default")).ToList();

        var disabledTokens = new HashSet<string>(StringComparer.Ordinal);

        for (var attempt = 0; attempt < _options.MaxRetries; attempt++)
        {
            try
            {
                using var response = await client.PostAsJsonAsync(
                    _options.Endpoint,
                    messages,
                    JsonOptions,
                    cancellationToken);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    if (attempt < _options.MaxRetries - 1)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(5, attempt)), cancellationToken);
                        continue;
                    }
                    _logger.LogWarning("Expo Push rate limit hit after {Attempts} attempts", attempt + 1);
                    return;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError(
                        "Expo Push failed with status {Status}: {Body}",
                        (int)response.StatusCode,
                        errorBody);
                    return;
                }

                var result = await response.Content.ReadFromJsonAsync<ExpoPushResponse>(
                    JsonOptions,
                    cancellationToken);

                if (result?.Data is null) return;

                foreach (var ticket in result.Data)
                {
                    if (ticket.Status == "ok") continue;

                    if (ticket.Details?.Error is "DeviceNotRegistered"
                        or "InvalidExpoPushToken"
                        or "InvalidCredentials")
                    {
                        disabledTokens.Add(ticket.ExpoPushToken);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Expo Push ticket error: token={Token} error={Error} message={Message}",
                            ticket.ExpoPushToken,
                            ticket.Details?.Error,
                            ticket.Message);
                    }
                }

                break;
            }
            catch (HttpRequestException ex) when (attempt < _options.MaxRetries - 1)
            {
                _logger.LogWarning(ex, "Expo Push HTTP error on attempt {Attempt}, retrying", attempt + 1);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(5, attempt)), cancellationToken);
            }
            catch (TaskCanceledException) when (attempt < _options.MaxRetries - 1 && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Expo Push timeout on attempt {Attempt}, retrying", attempt + 1);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(5, attempt)), cancellationToken);
            }
        }

        if (disabledTokens.Count > 0)
        {
            await DisableInvalidTokensAsync(disabledTokens, cancellationToken);
        }
    }

    private async Task DisableInvalidTokensAsync(
        HashSet<string> invalidTokens,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var toDisable = await _dbContext.Set<UserPushToken>()
            .Where(t => invalidTokens.Contains(t.ExpoPushToken) && t.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var token in toDisable)
        {
            token.IsActive = false;
            token.DisabledAt = now;
            token.UpdatedAt = now;
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Disabled {Count} invalid Expo push tokens", toDisable.Count);
    }

    private sealed record ExpoPushMessage(
        [property: JsonPropertyName("to")] string To,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("body")] string Body,
        [property: JsonPropertyName("data")] ExpoPushData Data,
        [property: JsonPropertyName("channelId")] string ChannelId,
        [property: JsonPropertyName("priority")] string Priority,
        [property: JsonPropertyName("sound")] string Sound);

    private sealed record ExpoPushData(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("relatedEntityType")] string? RelatedEntityType,
        [property: JsonPropertyName("relatedEntityId")] Guid? RelatedEntityId,
        [property: JsonPropertyName("deepLink")] string? DeepLink);

    private sealed record ExpoPushResponse(
        [property: JsonPropertyName("data")] List<ExpoPushTicket>? Data);

    private sealed record ExpoPushTicket(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("details")] ExpoPushTicketDetails? Details,
        [property: JsonPropertyName("__expoPushToken")] string ExpoPushToken);

    private sealed record ExpoPushTicketDetails(
        [property: JsonPropertyName("error")] string? Error);
}