using System.Globalization;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SaigonWaterbus.Application.CharterBookings;

namespace SaigonWaterbus.Web.Infrastructure;

public sealed class QuestPdfCharterBookingTicketPdfRenderer : ICharterBookingTicketPdfRenderer
{
    public byte[] Render(CharterBookingTicketExportDto export)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        const string navy = "#073B56";
        const string teal = "#057C9F";
        const string gold = "#F1C85D";
        const string ink = "#283640";
        const string muted = "#6B7780";
        const string pale = "#F3FAFC";
        const string line = "#D9E7EE";

        return Document.Create(document =>
        {
            foreach (var ticket in export.Tickets)
            {
                var qrBytes = BuildQrPngBytes(ticket.QrToken);
                var departureDate = FormatPdfDate(export.DepartureDate);
                var startTime = FormatPdfTime(export.StartTime);
                var fromStation = ResolvePdfText(export.FromStationName, "Diem di");
                var toStation = ResolvePdfText(export.ToStationName, "Diem den");
                var vesselName = ResolvePdfText(export.BoatName, "Waterbus");
                var passengerName = ResolvePdfText(ticket.PassengerName, "Khach hang");
                var passengerType = ResolvePdfText(ticket.PassengerType, "Passenger");
                var birthYearText = ticket.PassengerBirthYear?.ToString(CultureInfo.InvariantCulture) ?? "-";
                var seatCode = ResolvePdfText(ticket.SeatCode);

                document.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(28);
                    page.DefaultTextStyle(x => x.FontSize(10).FontColor(ink));

                    page.Content()
                        .Background("#EEF6F8")
                        .Padding(18)
                        .Column(column =>
                        {
                            column.Spacing(0);
                            column.Item().Background(gold).Height(8);

                            column.Item()
                                .Background(navy)
                                .PaddingHorizontal(24)
                                .PaddingVertical(20)
                                .Row(row =>
                                {
                                    row.RelativeItem().Column(header =>
                                    {
                                        header.Item().Text("WATERBUS")
                                            .FontSize(20)
                                            .Bold()
                                            .FontColor(Colors.White);
                                        header.Item().PaddingTop(4).Text("BOARDING PASS / VE LEN TAU")
                                            .FontSize(9)
                                            .SemiBold()
                                            .FontColor(gold);
                                    });

                                    row.ConstantItem(250).AlignRight().Column(code =>
                                    {
                                        code.Item().Text("TICKET CODE")
                                            .FontSize(7)
                                            .SemiBold()
                                            .FontColor("#B8D7E4");
                                        code.Item().Text(ticket.TicketCode)
                                            .FontSize(14)
                                            .Bold()
                                            .FontColor(Colors.White);
                                        code.Item().PaddingTop(4).Text($"Booking {export.BookingCode}")
                                            .FontSize(8)
                                            .FontColor("#B8D7E4");
                                    });
                                });

                            column.Item()
                                .Background(Colors.White)
                                .Padding(24)
                                .Column(ticketBody =>
                                {
                                    ticketBody.Spacing(18);

                                    ticketBody.Item()
                                        .Border(1)
                                        .BorderColor(line)
                                        .Background(pale)
                                        .Padding(16)
                                        .Row(route =>
                                        {
                                            route.RelativeItem().Column(from =>
                                            {
                                                from.Item().Text("FROM").FontSize(7).SemiBold().FontColor(muted);
                                                from.Item().Text(fromStation).FontSize(18).Bold().FontColor(teal);
                                            });

                                            route.ConstantItem(48).AlignCenter().Column(mid =>
                                            {
                                                mid.Item().AlignCenter().Text("->").FontSize(20).Bold().FontColor(gold);
                                                mid.Item().AlignCenter().Text("ROUTE").FontSize(6).FontColor(muted);
                                            });

                                            route.RelativeItem().AlignRight().Column(to =>
                                            {
                                                to.Item().AlignRight().Text("TO").FontSize(7).SemiBold().FontColor(muted);
                                                to.Item().AlignRight().Text(toStation).FontSize(18).Bold().FontColor(teal);
                                            });
                                        });

                                    ticketBody.Item().Row(main =>
                                    {
                                        main.RelativeItem().Column(details =>
                                        {
                                            details.Spacing(12);

                                            details.Item()
                                                .BorderLeft(4)
                                                .BorderColor(gold)
                                                .PaddingLeft(12)
                                                .Column(passenger =>
                                                {
                                                    passenger.Item().Text("PASSENGER / HANH KHACH")
                                                        .FontSize(7)
                                                        .SemiBold()
                                                        .FontColor(muted);
                                                    passenger.Item().Text(passengerName)
                                                        .FontSize(24)
                                                        .Bold()
                                                        .FontColor(ink);
                                                });

                                            details.Item().Table(table =>
                                            {
                                                table.ColumnsDefinition(columns =>
                                                {
                                                    columns.RelativeColumn();
                                                    columns.RelativeColumn();
                                                });

                                                AddPdfInfoCell(table.Cell(), "Date", departureDate, teal);
                                                AddPdfInfoCell(table.Cell(), "Time", startTime, teal);
                                                AddPdfInfoCell(table.Cell(), "Vessel", vesselName, ink);
                                                AddPdfInfoCell(table.Cell(), "Seat / Ghe", seatCode, teal);
                                                AddPdfInfoCell(table.Cell(), "Passenger type", passengerType, ink);
                                                AddPdfInfoCell(table.Cell(), "Birth year", birthYearText, ink);
                                                AddPdfInfoCell(table.Cell(), "Ticket status", ticket.TicketStatus, ink);
                                            });
                                        });

                                        main.ConstantItem(170)
                                            .Border(1)
                                            .BorderColor(line)
                                            .Padding(14)
                                            .AlignCenter()
                                            .Column(qr =>
                                            {
                                                qr.Spacing(8);
                                                qr.Item().Text("SCAN TO BOARD")
                                                    .FontSize(8)
                                                    .SemiBold()
                                                    .FontColor(navy)
                                                    .AlignCenter();
                                                qr.Item().Width(136).Height(136).AlignCenter().Image(qrBytes).FitArea();
                                                qr.Item().Text("Quet QR de check-in")
                                                    .FontSize(7)
                                                    .FontColor(muted)
                                                    .AlignCenter();
                                            });
                                    });

                                    if (export.ItineraryStops.Count > 0)
                                    {
                                        ticketBody.Item()
                                            .BorderTop(1)
                                            .BorderColor(line)
                                            .PaddingTop(14)
                                            .Column(stops =>
                                            {
                                                stops.Spacing(6);
                                                stops.Item().Text("ITINERARY / LICH TRINH")
                                                    .FontSize(9)
                                                    .Bold()
                                                    .FontColor(navy);

                                                foreach (var stop in export.ItineraryStops.OrderBy(x => x.StopOrder))
                                                {
                                                    var duration = stop.StayDurationMinutes > 0
                                                        ? $" - {stop.StayDurationMinutes} min"
                                                        : string.Empty;
                                                    var note = string.IsNullOrWhiteSpace(stop.Note)
                                                        ? string.Empty
                                                        : $" - {stop.Note.Trim()}";

                                                    stops.Item().Row(stopRow =>
                                                    {
                                                        stopRow.ConstantItem(20)
                                                            .Background(gold)
                                                            .PaddingVertical(3)
                                                            .AlignCenter()
                                                            .Text(stop.StopOrder.ToString(CultureInfo.InvariantCulture))
                                                            .FontSize(8)
                                                            .Bold()
                                                            .FontColor(navy);
                                                        stopRow.RelativeItem()
                                                            .PaddingLeft(8)
                                                            .Text($"{stop.StationName}{duration}{note}")
                                                            .FontSize(9)
                                                            .FontColor(ink);
                                                    });
                                                }
                                            });
                                    }

                                    ticketBody.Item()
                                        .BorderTop(1)
                                        .BorderColor(line)
                                        .PaddingTop(10)
                                        .Row(footer =>
                                        {
                                            footer.RelativeItem().Text("Please keep this boarding pass for verification. Vui long giu ve nay de lam thu tuc len tau.")
                                                .FontSize(8)
                                                .FontColor(muted);
                                            footer.ConstantItem(120).AlignRight().Text("waterbus")
                                                .FontSize(8)
                                                .Bold()
                                                .FontColor(teal);
                                        });
                                });
                        });
                });
            }
        }).GeneratePdf();
    }

    private static byte[] BuildQrPngBytes(string qrToken)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrData = qrGenerator.CreateQrCode(qrToken, QRCodeGenerator.ECCLevel.Q);
        var qrCode = new PngByteQRCode(qrData);
        return qrCode.GetGraphic(20);
    }

    private static void AddPdfInfoCell(IContainer container, string label, string value, string valueColor)
    {
        container
            .PaddingBottom(10)
            .PaddingRight(10)
            .Column(column =>
            {
                column.Item().Text(label.ToUpperInvariant()).FontSize(7).SemiBold().FontColor("#7C8B95");
                column.Item().Text(ResolvePdfText(value)).FontSize(11).Bold().FontColor(valueColor);
            });
    }

    private static string FormatPdfDate(DateOnly? date) =>
        date?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "-";

    private static string FormatPdfTime(TimeOnly? time) =>
        time?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "-";

    private static string ResolvePdfText(string? value, string fallback = "-") =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
