namespace SaigonWaterbus.Application.Common.Exceptions;

/// <summary>
/// Nhà cung cấp giọng nói từ chối request vì THAM SỐ SAI, không phải vì nó hỏng.
///
/// Tách riêng khỏi lỗi hạ tầng để tầng Web trả đúng mã: 400 kèm lý do thật (lỗi của người gọi,
/// sửa được) thay vì 503 "hệ thống đang bận" (khiến người ta ngồi chờ một sự cố không tồn tại).
///
/// Ví dụ có thật: gọi /speak với voice = "string" (giá trị Swagger điền sẵn) thì Google trả
/// 400 "Voice 'string' does not exist" — nhưng người dùng chỉ thấy "không đọc thành tiếng được".
/// </summary>
public sealed class SpeechRequestException : Exception
{
    public SpeechRequestException(string message) : base(message)
    {
    }
}
