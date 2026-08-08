namespace SaigonWaterbus.Infrastructure.Ai;

/// <summary>
/// Thông tin xác thực Google Cloud dùng CHUNG cho Text-to-Speech và Speech-to-Text.
///
/// Có hai kiểu xác thực, ưu tiên service account nếu khai:
/// 1. Service account (file JSON) — chuẩn production, phân quyền theo role, xoay khoá được.
/// 2. API key chuỗi — khai ở từng options riêng (<see cref="GoogleTextToSpeechOptions.ApiKey"/>,
///    <see cref="GoogleCloudSpeechToTextOptions.ApiKey"/>). Đơn giản hơn nhưng không phân quyền.
/// </summary>
public sealed class GoogleCloudCredentialsOptions
{
    public const string SectionName = "GoogleCloud";

    /// <summary>
    /// Đường dẫn tới file JSON service account. Dùng cho máy dev.
    ///
    /// ĐỂ FILE NGOÀI THƯ MỤC REPO (ví dụ C:\Users\&lt;bạn&gt;\.gcloud\swb-google.json). Để trong repo
    /// là sớm muộn cũng có người commit nhầm — private key lộ thì phải xoay lại toàn bộ.
    /// </summary>
    public string CredentialsPath { get; set; } = string.Empty;

    /// <summary>
    /// Nguyên nội dung JSON service account, nhét thẳng vào một app setting. Dùng khi deploy
    /// (Azure App Service) vì ở đó không có chỗ đặt file cho tiện.
    ///
    /// Khai cả hai thì cái này thắng — deploy không nên phụ thuộc đường dẫn file của máy ai đó.
    /// </summary>
    public string CredentialsJson { get; set; } = string.Empty;
}
