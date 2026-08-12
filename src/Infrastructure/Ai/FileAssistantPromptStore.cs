using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Infrastructure.Ai;

/// <summary>
/// Lưu system prompt của trợ lý thành file text, kèm các bản sao lưu để quay lui.
///
/// Thư mục: ưu tiên cấu hình <c>AssistantPrompt:Directory</c>; không có thì lấy
/// <c>%HOME%/data/prompts</c> khi biến môi trường HOME tồn tại (Azure App Service đặt sẵn
/// HOME=D:\home) — chỗ đó ghi được và deploy không đụng tới; còn lại thì nằm cạnh app.
///
/// Ghi kiểu ATOMIC (ghi .tmp rồi Move đè): mỗi lượt chat đều đọc file này, ghi trực tiếp thì có
/// lúc request đọc trúng file mới ghi được một nửa.
/// </summary>
public sealed class FileAssistantPromptStore : IAssistantPromptStore
{
    private const string FileName = "assistant.chat.txt";
    private const string BackupPrefix = "assistant.chat.bak-";
    private const string BackupSuffix = ".txt";
    private const string VersionFormat = "yyyyMMddTHHmmss";

    /// <summary>Ghi và đọc phải loại trừ nhau trong cùng tiến trình, tránh đọc trúng lúc Move.</summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly AssistantPromptOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FileAssistantPromptStore> _logger;

    public FileAssistantPromptStore(
        IOptions<AssistantPromptOptions> options,
        TimeProvider timeProvider,
        ILogger<FileAssistantPromptStore> logger)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        Directory = ResolveDirectory(_options.Directory);
    }

    /// <summary>Công khai để endpoint quản lý (chỉ Admin) cho biết prompt đang nằm ở đâu.</summary>
    public string Directory { get; }

    public string Location => Directory;

    private string FilePath => Path.Combine(Directory, FileName);

    public async Task<AssistantPromptFile?> ReadAsync(CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadCoreAsync(cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<AssistantPromptFile> WriteAsync(string content, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            BackupCurrent();

            var temporaryPath = FilePath + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, content, cancellationToken);
            File.Move(temporaryPath, FilePath, overwrite: true);

            TrimOldVersions();

            var updatedAt = new DateTimeOffset(File.GetLastWriteTimeUtc(FilePath), TimeSpan.Zero);
            _logger.LogInformation("Assistant prompt updated ({Length} chars) at {Path}", content.Length, FilePath);
            return new AssistantPromptFile(content, updatedAt);
        }
        finally
        {
            Gate.Release();
        }
    }

    public Task<IReadOnlyList<AssistantPromptVersion>> ListVersionsAsync(CancellationToken cancellationToken)
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return Task.FromResult<IReadOnlyList<AssistantPromptVersion>>([]);
        }

        var versions = EnumerateBackups()
            .Select(path => new AssistantPromptVersion(
                VersionIdOf(path),
                new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero),
                (int)new FileInfo(path).Length))
            .OrderByDescending(version => version.Id, StringComparer.Ordinal)
            .ToArray();

        return Task.FromResult<IReadOnlyList<AssistantPromptVersion>>(versions);
    }

    public async Task<AssistantPromptFile?> RestoreAsync(string versionId, CancellationToken cancellationToken)
    {
        // Chặn "../" và mọi thứ không phải mốc thời gian: versionId đến từ client.
        if (!IsValidVersionId(versionId))
        {
            return null;
        }

        var backupPath = Path.Combine(Directory, BackupPrefix + versionId + BackupSuffix);
        if (!File.Exists(backupPath))
        {
            return null;
        }

        var content = await File.ReadAllTextAsync(backupPath, cancellationToken);

        // Đi qua WriteAsync để bản đang chạy cũng được sao lưu — quay lui rồi vẫn quay lại được.
        return await WriteAsync(content, cancellationToken);
    }

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(FilePath))
            {
                return;
            }

            BackupCurrent();
            File.Delete(FilePath);
            _logger.LogInformation("Assistant prompt reset to the built-in default");
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<AssistantPromptFile?> ReadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        var content = await File.ReadAllTextAsync(FilePath, cancellationToken);
        var updatedAt = new DateTimeOffset(File.GetLastWriteTimeUtc(FilePath), TimeSpan.Zero);
        return new AssistantPromptFile(content, updatedAt);
    }

    private void BackupCurrent()
    {
        if (!File.Exists(FilePath))
        {
            return;
        }

        var versionId = _timeProvider.GetUtcNow().ToString(VersionFormat, CultureInfo.InvariantCulture);
        var backupPath = Path.Combine(Directory, BackupPrefix + versionId + BackupSuffix);

        // Trùng tên khi lưu hai lần trong cùng một giây: ghi đè là đúng, hai bản chỉ cách nhau
        // vài trăm ms thì bản sau mới là thứ đáng giữ.
        File.Copy(FilePath, backupPath, overwrite: true);
    }

    private void TrimOldVersions()
    {
        var backups = EnumerateBackups()
            .OrderByDescending(VersionIdOf, StringComparer.Ordinal)
            .Skip(Math.Max(1, _options.MaxVersions))
            .ToArray();

        foreach (var path in backups)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException ex)
            {
                // Dọn rác thất bại thì kệ, không được làm hỏng thao tác lưu của admin.
                _logger.LogWarning(ex, "Could not delete old prompt backup {Path}", path);
            }
        }
    }

    private IEnumerable<string> EnumerateBackups() =>
        System.IO.Directory.Exists(Directory)
            ? System.IO.Directory.EnumerateFiles(Directory, BackupPrefix + "*" + BackupSuffix)
            : [];

    private static string VersionIdOf(string path) =>
        Path.GetFileNameWithoutExtension(path)[BackupPrefix.Length..];

    private static bool IsValidVersionId(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && DateTimeOffset.TryParseExact(
            value, VersionFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _);

    private static string ResolveDirectory(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var home = Environment.GetEnvironmentVariable("HOME");
        return string.IsNullOrWhiteSpace(home)
            ? Path.Combine(AppContext.BaseDirectory, "prompts")
            : Path.Combine(home, "data", "prompts");
    }
}
