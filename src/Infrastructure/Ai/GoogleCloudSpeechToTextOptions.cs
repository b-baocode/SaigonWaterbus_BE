namespace SaigonWaterbus.Infrastructure.Ai;

public sealed class GoogleCloudSpeechToTextOptions
{
    public const string SectionName = "GoogleCloudSpeechToText";

    /// <summary>
    /// API key nếu không dùng service account. Để trống khi đã khai
    /// <see cref="GoogleCloudCredentialsOptions.CredentialsPath"/> — service account được ưu tiên.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Dùng API v2 (nhanh gần gấp đôi v1 — xem ghi chú ở service).</summary>
    public string ApiBaseUrl { get; set; } = "https://speech.googleapis.com/v2";

    /// <summary>
    /// Bỏ trống thì lấy từ file service account. Chỉ khai khi muốn tính quota sang project khác.
    /// </summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>
    /// ĐÃ THỬ: `asia-southeast1` (Singapore, gần Việt Nam nhất) KHÔNG hỗ trợ vi-VN cho model
    /// short/long — trả 400. Nên phải để `global` dù đường mạng đi xa hơn.
    /// </summary>
    public string Location { get; set; } = "global";

    /// <summary>
    /// Model nhận dạng. Đo thật trên clip 5.3s tiếng Việt (trung vị): `long` 2.46s,
    /// `chirp_2` 6.50s (chép chuẩn hơn nhưng chậm), `short` nhanh nhưng CHÉP THIẾU — nó cắt mất
    /// đuôi câu, đừng dùng.
    /// </summary>
    public string Model { get; set; } = "long";

    public string VietnameseLanguageCode { get; set; } = "vi-VN";

    public string EnglishLanguageCode { get; set; } = "en-US";

    public int TimeoutSeconds { get; set; } = 15;
}
