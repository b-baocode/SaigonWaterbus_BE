using System.Globalization;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SaigonWaterbus.Application.Tickets;

namespace SaigonWaterbus.Web.Infrastructure;

public sealed class QuestPdfBookingTicketPdfRenderer : IBookingTicketPdfRenderer
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    private const string Navy = "#073B56";
    private const string Teal = "#057C9F";
    private const string Gold = "#F1C85D";
    private const string Ink = "#283640";
    private const string Muted = "#6B7780";
    private const string Pale = "#F3FAFC";
    private const string Line = "#D9E7EE";

    public byte[] Render(BookingTicketPdfExportDto export)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var departureDate = FormatPdfDate(export.DepartureTime);
        var departureTime = FormatPdfTime(export.DepartureTime);
        var fromStation = ResolvePdfText(export.FromStationName, "Diem di");
        var toStation = ResolvePdfText(export.ToStationName, "Diem den");
        var vesselName = ResolvePdfText(export.BoatName, "Waterbus");

        return Document.Create(document =>
        {
            if (!string.IsNullOrWhiteSpace(export.BookingQrToken))
            {
                RenderGroupPassPage(document, export, departureDate, departureTime, fromStation, toStation, vesselName);
            }

            foreach (var ticket in export.Tickets)
            {
                RenderTicketPage(document, export, ticket, departureDate, departureTime, fromStation, toStation, vesselName);
            }
        }).GeneratePdf();
    }

    /// <summary>Trang QR tổng: chỉ có trong bản PDF gửi người đặt vé; staff quét 1 lần check-in cả nhóm.</summary>
    private static void RenderGroupPassPage(
        IDocumentContainer document,
        BookingTicketPdfExportDto export,
        string departureDate,
        string departureTime,
        string fromStation,
        string toStation,
        string vesselName)
    {
        var qrBytes = BuildQrPngBytes(export.BookingQrToken!);

        document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(28);
            page.DefaultTextStyle(x => x.FontSize(10).FontColor(Ink));

            page.Content()
                .Background("#EEF6F8")
                .Padding(18)
                .Column(column =>
                {
                    column.Spacing(0);
                    column.Item().Background(Gold).Height(8);

                    column.Item()
                        .Background(Navy)
                        .PaddingHorizontal(24)
                        .PaddingVertical(20)
                        .Row(row =>
                        {
                            row.RelativeItem().Column(header =>
                            {
                                header.Item().Text("WATERBUS")
                                    .FontSize(20).Bold().FontColor(Colors.White);
                                header.Item().PaddingTop(4).Text("GROUP PASS / QR TONG CA NHOM")
                                    .FontSize(9).SemiBold().FontColor(Gold);
                            });

                            row.ConstantItem(250).AlignRight().Column(code =>
                            {
                                code.Item().Text("BOOKING CODE")
                                    .FontSize(7).SemiBold().FontColor("#B8D7E4");
                                code.Item().Text(export.BookingCode)
                                    .FontSize(14).Bold().FontColor(Colors.White);
                            });
                        });

                    column.Item()
                        .Background(Colors.White)
                        .Padding(24)
                        .Column(body =>
                        {
                            body.Spacing(18);

                            body.Item()
                                .Border(1).BorderColor(Line).Background(Pale).Padding(16)
                                .Row(route =>
                                {
                                    route.RelativeItem().Column(from =>
                                    {
                                        from.Item().Text("FROM").FontSize(7).SemiBold().FontColor(Muted);
                                        from.Item().Text(fromStation).FontSize(18).Bold().FontColor(Teal);
                                    });

                                    route.ConstantItem(48).AlignCenter().Column(mid =>
                                    {
                                        mid.Item().AlignCenter().Text("->").FontSize(20).Bold().FontColor(Gold);
                                        mid.Item().AlignCenter().Text("ROUTE").FontSize(6).FontColor(Muted);
                                    });

                                    route.RelativeItem().AlignRight().Column(to =>
                                    {
                                        to.Item().AlignRight().Text("TO").FontSize(7).SemiBold().FontColor(Muted);
                                        to.Item().AlignRight().Text(toStation).FontSize(18).Bold().FontColor(Teal);
                                    });
                                });

                            body.Item().Row(main =>
                            {
                                main.RelativeItem().Column(details =>
                                {
                                    details.Spacing(12);

                                    details.Item()
                                        .BorderLeft(4).BorderColor(Gold).PaddingLeft(12)
                                        .Column(info =>
                                        {
                                            info.Item().Text("CHECK-IN CA NHOM / GROUP CHECK-IN")
                                                .FontSize(7).SemiBold().FontColor(Muted);
                                            info.Item().Text($"{export.Tickets.Count} hanh khach")
                                                .FontSize(24).Bold().FontColor(Ink);
                                        });

                                    details.Item().Table(table =>
                                    {
                                        table.ColumnsDefinition(columns =>
                                        {
                                            columns.RelativeColumn();
                                            columns.RelativeColumn();
                                        });

                                        AddPdfInfoCell(table.Cell(), "Date", departureDate, Teal);
                                        AddPdfInfoCell(table.Cell(), "Time (GMT+7)", departureTime, Teal);
                                        AddPdfInfoCell(table.Cell(), "Vessel", vesselName, Ink);
                                        AddPdfInfoCell(table.Cell(), "Trip", ResolvePdfText(export.TripCode), Ink);
                                    });
                                });

                                main.ConstantItem(170)
                                    .Border(1).BorderColor(Line).Padding(14).AlignCenter()
                                    .Column(qr =>
                                    {
                                        qr.Spacing(8);
                                        qr.Item().Text("SCAN TO CHECK-IN ALL")
                                            .FontSize(8).SemiBold().FontColor(Navy).AlignCenter();
                                        qr.Item().Width(136).Height(136).AlignCenter().Image(qrBytes).FitArea();
                                        qr.Item().Text("Quet 1 lan check-in ca nhom")
                                            .FontSize(7).FontColor(Muted).AlignCenter();
                                    });
                            });

                            body.Item()
                                .BorderTop(1).BorderColor(Line).PaddingTop(14)
                                .Column(passengers =>
                                {
                                    passengers.Spacing(6);
                                    passengers.Item().Text("PASSENGERS / DANH SACH HANH KHACH")
                                        .FontSize(9).Bold().FontColor(Navy);

                                    var index = 1;
                                    foreach (var ticket in export.Tickets)
                                    {
                                        var seat = string.IsNullOrWhiteSpace(ticket.SeatCode)
                                            ? string.Empty
                                            : $" - Ghe {ticket.SeatCode}";
                                        var type = string.IsNullOrWhiteSpace(ticket.TicketTypeName)
                                            ? string.Empty
                                            : $" - {ticket.TicketTypeName}";

                                        passengers.Item().Row(passengerRow =>
                                        {
                                            passengerRow.ConstantItem(20)
                                                .Background(Gold).PaddingVertical(3).AlignCenter()
                                                .Text(index.ToString(CultureInfo.InvariantCulture))
                                                .FontSize(8).Bold().FontColor(Navy);
                                            passengerRow.RelativeItem()
                                                .PaddingLeft(8)
                                                .Text($"{ticket.PassengerName}{seat}{type} ({ticket.TicketCode})")
                                                .FontSize(9).FontColor(Ink);
                                        });
                                        index++;
                                    }
                                });

                            body.Item()
                                .BorderTop(1).BorderColor(Line).PaddingTop(10)
                                .Row(footer =>
                                {
                                    footer.RelativeItem().Text("QR tong chi danh cho nguoi dat ve. Tung hanh khach van co the check-in bang QR rieng o cac trang sau.")
                                        .FontSize(8).FontColor(Muted);
                                    footer.ConstantItem(120).AlignRight().Text("waterbus")
                                        .FontSize(8).Bold().FontColor(Teal);
                                });
                        });
                });
        });
    }

    private static void RenderTicketPage(
        IDocumentContainer document,
        BookingTicketPdfExportDto export,
        BookingTicketPdfItemDto ticket,
        string departureDate,
        string departureTime,
        string fromStation,
        string toStation,
        string vesselName)
    {
        var qrBytes = BuildQrPngBytes(ticket.QrToken);
        var passengerName = ResolvePdfText(ticket.PassengerName, "Khach hang");
        var ticketType = ResolvePdfText(ticket.TicketTypeName, "Passenger");
        var seatCode = ResolvePdfText(ticket.SeatCode, "Tu do");

        document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(28);
            page.DefaultTextStyle(x => x.FontSize(10).FontColor(Ink));

            page.Content()
                .Background("#EEF6F8")
                .Padding(18)
                .Column(column =>
                {
                    column.Spacing(0);
                    column.Item().Background(Gold).Height(8);

                    column.Item()
                        .Background(Navy)
                        .PaddingHorizontal(24)
                        .PaddingVertical(20)
                        .Row(row =>
                        {
                            row.RelativeItem().Column(header =>
                            {
                                header.Item().Text("WATERBUS")
                                    .FontSize(20).Bold().FontColor(Colors.White);
                                header.Item().PaddingTop(4).Text("BOARDING PASS / VE LEN TAU")
                                    .FontSize(9).SemiBold().FontColor(Gold);
                            });

                            row.ConstantItem(250).AlignRight().Column(code =>
                            {
                                code.Item().Text("TICKET CODE")
                                    .FontSize(7).SemiBold().FontColor("#B8D7E4");
                                code.Item().Text(ticket.TicketCode)
                                    .FontSize(14).Bold().FontColor(Colors.White);
                                code.Item().PaddingTop(4).Text($"Booking {export.BookingCode}")
                                    .FontSize(8).FontColor("#B8D7E4");
                            });
                        });

                    column.Item()
                        .Background(Colors.White)
                        .Padding(24)
                        .Column(ticketBody =>
                        {
                            ticketBody.Spacing(18);

                            ticketBody.Item()
                                .Border(1).BorderColor(Line).Background(Pale).Padding(16)
                                .Row(route =>
                                {
                                    route.RelativeItem().Column(from =>
                                    {
                                        from.Item().Text("FROM").FontSize(7).SemiBold().FontColor(Muted);
                                        from.Item().Text(fromStation).FontSize(18).Bold().FontColor(Teal);
                                    });

                                    route.ConstantItem(48).AlignCenter().Column(mid =>
                                    {
                                        mid.Item().AlignCenter().Text("->").FontSize(20).Bold().FontColor(Gold);
                                        mid.Item().AlignCenter().Text("ROUTE").FontSize(6).FontColor(Muted);
                                    });

                                    route.RelativeItem().AlignRight().Column(to =>
                                    {
                                        to.Item().AlignRight().Text("TO").FontSize(7).SemiBold().FontColor(Muted);
                                        to.Item().AlignRight().Text(toStation).FontSize(18).Bold().FontColor(Teal);
                                    });
                                });

                            ticketBody.Item().Row(main =>
                            {
                                main.RelativeItem().Column(details =>
                                {
                                    details.Spacing(12);

                                    details.Item()
                                        .BorderLeft(4).BorderColor(Gold).PaddingLeft(12)
                                        .Column(passenger =>
                                        {
                                            passenger.Item().Text("PASSENGER / HANH KHACH")
                                                .FontSize(7).SemiBold().FontColor(Muted);
                                            passenger.Item().Text(passengerName)
                                                .FontSize(24).Bold().FontColor(Ink);
                                        });

                                    details.Item().Table(table =>
                                    {
                                        table.ColumnsDefinition(columns =>
                                        {
                                            columns.RelativeColumn();
                                            columns.RelativeColumn();
                                        });

                                        AddPdfInfoCell(table.Cell(), "Date", departureDate, Teal);
                                        AddPdfInfoCell(table.Cell(), "Time (GMT+7)", departureTime, Teal);
                                        AddPdfInfoCell(table.Cell(), "Seat", seatCode, Teal);
                                        AddPdfInfoCell(table.Cell(), "Ticket type", ticketType, Ink);
                                        AddPdfInfoCell(table.Cell(), "Vessel", vesselName, Ink);
                                        AddPdfInfoCell(table.Cell(), "Trip", ResolvePdfText(export.TripCode), Ink);
                                    });
                                });

                                main.ConstantItem(170)
                                    .Border(1).BorderColor(Line).Padding(14).AlignCenter()
                                    .Column(qr =>
                                    {
                                        qr.Spacing(8);
                                        qr.Item().Text("SCAN TO BOARD")
                                            .FontSize(8).SemiBold().FontColor(Navy).AlignCenter();
                                        qr.Item().Width(136).Height(136).AlignCenter().Image(qrBytes).FitArea();
                                        qr.Item().Text("Quet QR de check-in")
                                            .FontSize(7).FontColor(Muted).AlignCenter();
                                    });
                            });

                            ticketBody.Item()
                                .BorderTop(1).BorderColor(Line).PaddingTop(10)
                                .Row(footer =>
                                {
                                    footer.RelativeItem().Text("Please keep this boarding pass for verification. Vui long giu ve nay de lam thu tuc len tau.")
                                        .FontSize(8).FontColor(Muted);
                                    footer.ConstantItem(120).AlignRight().Text("waterbus")
                                        .FontSize(8).Bold().FontColor(Teal);
                                });
                        });
                });
        });
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

    private static string FormatPdfDate(DateTimeOffset? value) =>
        value?.ToOffset(VietnamOffset).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "-";

    private static string FormatPdfTime(DateTimeOffset? value) =>
        value?.ToOffset(VietnamOffset).ToString("HH:mm", CultureInfo.InvariantCulture) ?? "-";

    private static string ResolvePdfText(string? value, string fallback = "-") =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
