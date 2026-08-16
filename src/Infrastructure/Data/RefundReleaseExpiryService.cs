using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Infrastructure.Data;

namespace SaigonWaterbus.Infrastructure.Data;

/// <summary>
/// Job nền: quét các payment đã được admin "mở lại" cho customer tự nhập STK
/// (RefundReleasedAt != null) nhưng quá thời gian retry mà customer chưa refund (hoặc fail).
/// Đóng lại state về RefundFailed để admin xử lý tiếp (vd: ghi nhận hoàn thủ công).
/// </summary>
public sealed class RefundReleaseExpiryService : BackgroundService
{
    // Chạy mỗi 6 giờ — đủ để đóng các release quá hạn ~1 ngày sau deadline 7 ngày.
    private static readonly TimeSpan ScanInterval = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RefundReleaseExpiryService> _logger;
    private bool _databaseUnavailableWarningLogged;

    public RefundReleaseExpiryService(
        IServiceScopeFactory scopeFactory,
        ILogger<RefundReleaseExpiryService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ScanInterval);

        await ExpireOverdueReleasesAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ExpireOverdueReleasesAsync(stoppingToken);
        }
    }

    private async Task ExpireOverdueReleasesAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (!await dbContext.Database.CanConnectAsync(cancellationToken))
            {
                if (!_databaseUnavailableWarningLogged)
                {
                    _logger.LogWarning("Skipping refund-release expiry scan because the database is not reachable.");
                    _databaseUnavailableWarningLogged = true;
                }

                return;
            }

            _databaseUnavailableWarningLogged = false;
            var timeProvider = scope.ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
            var now = timeProvider.GetUtcNow();

            // Tìm các payment:
            // - Đã thanh toán (Paid)
            // - Đã được admin release (RefundReleasedAt != null)
            // - Sau khi release, customer KHÔNG hoàn thành refund (RefundStatus null = chưa attempt)
            // - HOẶC customer đã attempt fail (RefundStatus = "Failed") và chưa được admin xử lý tiếp
            // - Deadline = RefundReleasedAt + 7 ngày
            var expiredPayments = await dbContext.Payments
                .Where(p => p.PaymentStatus == "Paid"
                    && p.RefundReleasedAt != null
                    && p.RefundReleasedAt.Value.AddDays(7) < now
                    && (p.RefundStatus == null
                        || string.Equals(p.RefundStatus, "Failed", StringComparison.OrdinalIgnoreCase)))
                .ToListAsync(cancellationToken);

            if (expiredPayments.Count == 0)
            {
                return;
            }

            foreach (var payment in expiredPayments)
            {
                // Đóng state: RefundStatus = Failed, kèm lý do "quá thời gian retry" để admin thấy lý do rõ ràng.
                var previousStatus = payment.RefundStatus;
                payment.RefundStatus = "Failed";
                if (string.IsNullOrWhiteSpace(payment.RefundFailureReason))
                {
                    payment.RefundFailureReason = "Đã quá thời gian cho phép khách tự nhập lại STK sau khi admin mở lại.";
                }

                _logger.LogInformation(
                    "Closed refund release for payment {PaymentId} (paymentCode={PaymentCode}). PrevStatus={PreviousStatus}",
                    payment.Id,
                    payment.PaymentCode,
                    previousStatus ?? "<none>");
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while closing overdue refund releases.");
        }
    }
}
