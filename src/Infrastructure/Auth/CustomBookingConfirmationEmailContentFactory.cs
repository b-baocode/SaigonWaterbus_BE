using System.Globalization;
using System.Net;
using System.Text;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Auth;

internal static class CustomBookingConfirmationEmailContentFactory
{
    private static readonly CultureInfo ViCulture = CultureInfo.GetCultureInfo("vi-VN");

    public static string Subject(CustomBookingRequest request) =>
        $"Xac nhan thue tau custom #{request.Id:N}"[..Math.Min($"Xac nhan thue tau custom #{request.Id:N}".Length, 120)];

    public static string PlainText(CustomBookingRequest request)
    {
        var builder = new StringBuilder();
        AppendBody(builder, request, html: false);
        return builder.ToString();
    }

    public static string Html(CustomBookingRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html><html><body style=\"font-family:Arial,sans-serif;line-height:1.5;color:#111827\">");
        AppendBody(builder, request, html: true);
        builder.AppendLine("</body></html>");
        return builder.ToString();
    }

    private static void AppendBody(StringBuilder builder, CustomBookingRequest request, bool html)
    {
        var quote = request.Quote;

        AppendLine(builder, "Saigon Waterbus - Xac nhan yeu cau thue tau custom", html, strong: true);
        AppendLine(builder, $"Ma yeu cau: {request.Id}", html);
        AppendLine(builder, $"Trang thai: {request.Status}", html);
        if (request.QuoteAcceptedAt.HasValue)
        {
            AppendLine(builder, $"Thoi gian xac nhan: {request.QuoteAcceptedAt.Value.ToOffset(TimeSpan.FromHours(7)):dd/MM/yyyy HH:mm} (GMT+7)", html);
        }
        AppendBreak(builder, html);

        AppendLine(builder, "Thong tin khach hang", html, strong: true);
        AppendLine(builder, $"Ho ten: {request.ContactName}", html);
        AppendLine(builder, $"So dien thoai: {request.ContactPhone}", html);
        if (!string.IsNullOrWhiteSpace(request.ContactEmail))
        {
            AppendLine(builder, $"Email: {request.ContactEmail}", html);
        }
        AppendBreak(builder, html);

        AppendLine(builder, "Thong tin tau va chuyen di", html, strong: true);
        if (request.PreferredVessel is not null)
        {
            AppendLine(builder, $"Tau da chon: {request.PreferredVessel.Code} - {request.PreferredVessel.Name}", html);
            AppendLine(builder, $"Suc chua: {request.PreferredVessel.PassengerCapacity} khach", html);
        }

        var startTimeText = request.PreferredStartTime?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "Chua xac dinh";
        AppendLine(builder, $"Ngay di: {request.DepartureDate:dd/MM/yyyy}", html);
        AppendLine(builder, $"Gio bat dau: {startTimeText}", html);
        AppendLine(builder, $"Ben bat dau: {request.FromStation?.StationName ?? request.FromLocation}", html);
        AppendLine(builder, $"Ben ket thuc: {request.ToStation?.StationName ?? request.ToLocation}", html);
        AppendLine(builder, $"So khach: {request.PassengerCount} (Nguoi lon: {request.AdultCount}, Tre em: {request.ChildCount})", html);

        if (request.ItineraryStops.Count > 0)
        {
            AppendLine(builder, "Lich trinh diem ghe:", html);
            foreach (var stop in request.ItineraryStops.OrderBy(x => x.StopOrder))
            {
                var line = $"- {stop.StopOrder}. {stop.Station.StationName}: dung {stop.StayDurationMinutes} phut";
                if (!string.IsNullOrWhiteSpace(stop.Note))
                {
                    line += $" ({stop.Note})";
                }

                AppendLine(builder, line, html);
            }
        }

        if (quote is not null)
        {
            AppendBreak(builder, html);
            AppendLine(builder, "Thong tin chi phi da dong y", html, strong: true);
            AppendLine(builder, $"Tong tien: {Money(quote.QuotedPrice, quote.Currency)}", html);
            AppendLine(builder, $"Dat coc du kien: {quote.DepositPercent.ToString("0.##", CultureInfo.InvariantCulture)}% = {Money(quote.DepositAmount, quote.Currency)}", html);
            AppendLine(builder, $"Con lai: {Money(quote.RemainingAmount, quote.Currency)}", html);
            if (!string.IsNullOrWhiteSpace(quote.PriceNote))
            {
                AppendLine(builder, $"Ghi chu: {quote.PriceNote}", html);
            }
        }

        AppendBreak(builder, html);
        AppendLine(builder, "Yeu cau cua quy khach da duoc ghi nhan thanh cong. Doi ngu Saigon Waterbus se lien he de ho tro cac buoc tiep theo.", html);
    }

    private static void AppendLine(StringBuilder builder, string value, bool html, bool strong = false)
    {
        if (!html)
        {
            builder.AppendLine(value);
            return;
        }

        var encoded = WebUtility.HtmlEncode(value);
        if (strong)
        {
            builder.Append("<p><strong>").Append(encoded).AppendLine("</strong></p>");
            return;
        }

        builder.Append("<div>").Append(encoded).AppendLine("</div>");
    }

    private static void AppendBreak(StringBuilder builder, bool html)
    {
        builder.AppendLine(html ? "<br/>" : string.Empty);
    }

    private static string Money(decimal amount, string currency) =>
        string.Create(ViCulture, $"{amount:N0} {currency}");
}
