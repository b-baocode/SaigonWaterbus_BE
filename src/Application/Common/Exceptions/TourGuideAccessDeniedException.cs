namespace SaigonWaterbus.Application.Common.Exceptions;

/// <summary>
/// Bị chặn cửa hướng dẫn viên AI, kèm LÝ DO máy đọc được.
///
/// VÌ SAO KHÔNG DÙNG THẲNG <see cref="ForbiddenAccessException"/>: cái đó trả 403 rỗng, mà ở đây
/// client bắt buộc phải phân biệt "chưa check-in" với "đã check-out" và "vé hết hạn" — ba câu nói
/// với khách hoàn toàn khác nhau. Không có lý do trong response thì app phải gọi thêm một vòng
/// nữa chỉ để biết mình vừa bị chặn vì cái gì.
///
/// Kế thừa <see cref="ForbiddenAccessException"/> để chỗ nào đang bắt lớp cha vẫn bắt được;
/// <c>ProblemDetailsExceptionHandler</c> xử nhánh này TRƯỚC nên mới giữ được phần lý do.
/// </summary>
public sealed class TourGuideAccessDeniedException : ForbiddenAccessException
{
    public TourGuideAccessDeniedException(string reasonCode) => ReasonCode = reasonCode;

    /// <summary>Một trong các giá trị của <c>TourGuideAccessReasons</c>.</summary>
    public string ReasonCode { get; }
}
