namespace SaigonWaterbus.Application.Common.Interfaces;

/// <param name="Content">Phần prompt Admin sửa được (chưa ghép luật cứng).</param>
public sealed record AssistantPromptFile(string Content, DateTimeOffset UpdatedAt);

/// <param name="Id">Mốc thời gian dạng yyyyMMddTHHmmss, cũng là thứ client gửi lại khi rollback.</param>
public sealed record AssistantPromptVersion(string Id, DateTimeOffset CreatedAt, int Length);

/// <summary>
/// Nơi lưu system prompt do Admin sửa. Tách interface vì tầng Application không đụng file trực
/// tiếp; đồng thời để sau này đổi sang DB/blob mà không phải sửa command nào.
///
/// CỐ Ý KHÔNG DÙNG DB: sửa prompt là việc hiếm, mà thêm bảng thì kéo theo migration — mà migration
/// ở dự án này phải chạy tay trên Neon. File nằm ngoài thư mục deploy nên vừa sửa được lúc chạy,
/// vừa không bị lần deploy sau ghi đè.
/// </summary>
public interface IAssistantPromptStore
{
    /// <summary>
    /// Mô tả nơi đang lưu (đường dẫn thư mục). Hiện lên màn quản lý để Admin biết sửa ở đâu khi
    /// cần vào thẳng máy chủ — và để biết ngay mình đang xem môi trường nào.
    /// </summary>
    string Location { get; }

    /// <summary>Trả null khi chưa ai sửa gì — phía trên sẽ dùng bản mặc định biên dịch sẵn.</summary>
    Task<AssistantPromptFile?> ReadAsync(CancellationToken cancellationToken);

    /// <summary>Ghi bản mới. Tự sao lưu bản đang dùng thành một version trước khi ghi đè.</summary>
    Task<AssistantPromptFile> WriteAsync(string content, CancellationToken cancellationToken);

    /// <summary>Các bản đã lưu trước đó, mới nhất trước.</summary>
    Task<IReadOnlyList<AssistantPromptVersion>> ListVersionsAsync(CancellationToken cancellationToken);

    /// <summary>Đưa một version cũ trở lại. Trả null nếu không có version đó.</summary>
    Task<AssistantPromptFile?> RestoreAsync(string versionId, CancellationToken cancellationToken);

    /// <summary>Xoá bản đang dùng (vẫn sao lưu trước) để quay về bản mặc định trong code.</summary>
    Task ResetAsync(CancellationToken cancellationToken);
}
