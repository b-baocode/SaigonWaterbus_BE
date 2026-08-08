using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;

namespace SaigonWaterbus.Infrastructure.Ai;

/// <summary>
/// Cấp access token cho các API Google Cloud từ service account.
///
/// Đăng ký SINGLETON: <see cref="GoogleCredential"/> tự giữ và tự làm mới token (hạn 1 tiếng)
/// bên trong nó. Đăng ký scoped thì mỗi request lại ký JWT rồi đổi token mới — thêm hẳn một
/// vòng gọi mạng vào mỗi lượt hỏi đáp, đúng chỗ đang đau nhất về độ trễ.
///
/// Chưa cấu hình service account thì <see cref="IsConfigured"/> = false và bên gọi tự lùi về
/// xác thực bằng API key.
/// </summary>
public sealed class GoogleCloudCredentials
{
    /// <summary>Scope chung, đủ cho cả Text-to-Speech lẫn Speech-to-Text.</summary>
    private const string CloudPlatformScope = "https://www.googleapis.com/auth/cloud-platform";

    private readonly Lazy<GoogleCredential?> _credential;

    public GoogleCloudCredentials(IOptions<GoogleCloudCredentialsOptions> options)
    {
        // Lazy: đọc file lúc dùng lần đầu chứ không phải lúc dựng DI container — cấu hình sai
        // đường dẫn thì chỉ tính năng giọng nói hỏng, không làm chết cả API.
        _credential = new Lazy<GoogleCredential?>(() => Load(options.Value));
    }

    public bool IsConfigured => _credential.Value is not null;

    /// <summary>
    /// Project id đọc từ chính file service account. Cloud STT v2 bắt buộc có nó trong URL —
    /// lấy ở đây để khỏi phải khai lại ở appsettings rồi hai chỗ lệch nhau.
    /// </summary>
    public string? ProjectId => (_credential.Value?.UnderlyingCredential as ServiceAccountCredential)?.ProjectId;

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var credential = _credential.Value
            ?? throw new InvalidOperationException("Chưa cấu hình service account Google Cloud.");

        return await credential.UnderlyingCredential.GetAccessTokenForRequestAsync(
            cancellationToken: cancellationToken);
    }

    private static GoogleCredential? Load(GoogleCloudCredentialsOptions options)
    {
        // CredentialsJson thắng: bản deploy không nên phụ thuộc đường dẫn file của máy nào cả.
        if (!string.IsNullOrWhiteSpace(options.CredentialsJson))
        {
            return GoogleCredential.FromJson(options.CredentialsJson).CreateScoped(CloudPlatformScope);
        }

        if (string.IsNullOrWhiteSpace(options.CredentialsPath))
        {
            return null;
        }

        if (!File.Exists(options.CredentialsPath))
        {
            throw new InvalidOperationException(
                $"Không tìm thấy file service account tại '{options.CredentialsPath}'. "
                + "Kiểm tra lại GoogleCloud:CredentialsPath trong appsettings.Local.json.");
        }

        return GoogleCredential.FromFile(options.CredentialsPath).CreateScoped(CloudPlatformScope);
    }
}
