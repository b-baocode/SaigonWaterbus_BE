using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SaigonWaterbus.Application.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Tickets;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Infrastructure.Data;

/// <summary>
/// Job nền: mỗi phút kiểm tra vé Active/CheckedIn đã quá cửa sổ check-in/check-out
/// và tự động chuyển sang Expired.
/// </summary>
public sealed class TicketExpirationHostedService : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TicketExpirationHostedService> _logger;

    public TicketExpirationHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<TicketExpirationHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ScanInterval);

        await SafeExpireAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SafeExpireAsync(stoppingToken);
        }
    }

    private async Task SafeExpireAsync(CancellationToken stoppingToken)
    {
        try
        {
            await ExpireOverdueTicketsAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host đang shutdown — bình thường.
            throw;
        }
        catch (Exception ex)
        {
            // Log và tiếp tục — KHÔNG để exception bubble lên Host (mặc định StopHost
            // sẽ tắt cả app). Job retry sau ScanInterval tiếp theo.
            _logger.LogError(ex, "Ticket expiration scan failed; will retry on next interval.");
        }
    }

    private async Task ExpireOverdueTicketsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            if (!await dbContext.Database.CanConnectAsync(cancellationToken))
            {
                _logger.LogWarning("Skipping ticket expiration scan because the database is not reachable.");
                return;
            }

            var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var timeProvider = scope.ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
            var now = timeProvider.GetUtcNow();

            var expiredCount = await ExpireOverdueActiveTicketsAsync(context, now, cancellationToken);
            expiredCount += await ExpireOverdueCheckedInTicketsAsync(context, now, cancellationToken);

            if (expiredCount > 0)
            {
                _logger.LogInformation("Expired {ExpiredTicketCount} overdue tickets.", expiredCount);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while expiring overdue tickets.");
        }
    }

    /// <summary>
    /// Nạp vé kèm ĐỦ dữ liệu để phán xét từng chặng.
    ///
    /// Bắt buộc phải có <c>BookingPassenger.Trip.TripStops</c>: chặng của vé nằm ở hành khách
    /// (booking khứ hồi có hai chặng, hai khung giờ khác nhau), còn giờ đến/đi THẬT nằm ở
    /// trip_stops. Thiếu TripStops thì rơi về giờ dự kiến, và vé của chuyến đang trễ sẽ bị huỷ
    /// oan trong khi quầy vẫn check-in được cho khách.
    /// </summary>
    private static Task<List<Ticket>> LoadTicketsForExpiryAsync(
        IApplicationDbContext context,
        TicketStatus status,
        CancellationToken cancellationToken)
    {
        return context.Set<Ticket>()
            .Include(t => t.BookingPassenger)
                .ThenInclude(p => p!.Trip!)
                    .ThenInclude(tr => tr.TripStops)
            .Include(t => t.Booking)
                .ThenInclude(b => b.Trip!)
                    .ThenInclude(tr => tr.TripStops)
            .Where(t => t.TicketStatus == status
                     && t.Booking!.BookingStatus == BookingStatus.Confirmed)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Vé Active đã quá cửa sổ check-in → tự động expire.
    /// Check-in window: [giờ tàu rời bến lên - 10 phút, giờ tàu rời bến lên].
    /// </summary>
    private async Task<int> ExpireOverdueActiveTicketsAsync(
        IApplicationDbContext context,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Load tất cả vé Active có booking đã Confirmed
        var activeTickets = await LoadTicketsForExpiryAsync(
            context, TicketStatus.Active, cancellationToken);

        var expiredTickets = new List<Ticket>();

        foreach (var ticket in activeTickets)
        {
            if (ticket.Booking is not null
                && TicketAttendanceWindowSupport.IsPastCheckInWindow(ticket, ticket.Booking, now))
            {
                expiredTickets.Add(ticket);
            }
        }

        if (expiredTickets.Count == 0)
        {
            return 0;
        }

        foreach (var ticket in expiredTickets)
        {
            ticket.TicketStatus = TicketStatus.Expired;
        }

        await context.SaveChangesAsync(cancellationToken);
        return expiredTickets.Count;
    }

    /// <summary>
    /// Vé CheckedIn đã quá cửa sổ check-out → tự động expire.
    /// Check-out window: [giờ tàu đến bến xuống, giờ tàu đến bến xuống + 10 phút].
    /// </summary>
    private async Task<int> ExpireOverdueCheckedInTicketsAsync(
        IApplicationDbContext context,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Load tất cả vé CheckedIn có booking đã Confirmed
        var checkedInTickets = await LoadTicketsForExpiryAsync(
            context, TicketStatus.CheckedIn, cancellationToken);

        var expiredTickets = new List<Ticket>();

        foreach (var ticket in checkedInTickets)
        {
            if (ticket.Booking is not null
                && TicketAttendanceWindowSupport.IsPastCheckOutWindow(ticket, ticket.Booking, now))
            {
                expiredTickets.Add(ticket);
            }
        }

        if (expiredTickets.Count == 0)
        {
            return 0;
        }

        foreach (var ticket in expiredTickets)
        {
            ticket.TicketStatus = TicketStatus.Expired;
        }

        await context.SaveChangesAsync(cancellationToken);
        return expiredTickets.Count;
    }
}
