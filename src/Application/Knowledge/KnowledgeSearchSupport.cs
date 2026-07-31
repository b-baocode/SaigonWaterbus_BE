using SaigonWaterbus.Application.Common;

namespace SaigonWaterbus.Application.Knowledge;

/// <summary>
/// Kết quả chấm điểm một entry: tổng điểm, số token khác nhau khớp được, và có token dài nào
/// khớp trọn một từ khoá hay không (bằng chứng mạnh, đủ để nhận dù chỉ khớp một token).
/// </summary>
public readonly record struct KnowledgeMatchScore(int Score, int MatchedTokens, bool HasStrongKeywordHit);

/// <summary>Một entry đã nạp từ DB, ở dạng thuần dữ liệu để chấm điểm được mà không cần DbContext.</summary>
public sealed record KnowledgeSearchCandidate(
    string Title,
    string Content,
    string Category,
    IReadOnlyList<string> Keywords,
    int DisplayOrder);

/// <summary>
/// Chấm điểm khớp từ khoá cho knowledge base. Tách riêng khỏi query/tool vì hai lý do:
/// test được không cần DB, và sau này muốn đổi sang full-text Postgres hay vector search thì
/// chỉ thay chỗ này, không đụng tới tool của trợ lý lẫn system prompt.
///
/// Đây là khớp TỪ, không hiểu từ đồng nghĩa: "trả lại vé" sẽ không tự khớp "hoàn vé".
/// Bù lại bằng field Keywords do admin khai.
/// </summary>
public static class KnowledgeSearchSupport
{
    public const int DefaultTake = 3;
    public const int MaxTake = 5;

    /// <summary>
    /// Cắt content MỖI hit để một entry viết quá dài không làm phình context của LLM.
    /// Đủ rộng cho admin gom cả một chính sách vào một mục (yêu cầu của ô cố định bên FE) —
    /// vượt mức này thì phần đuôi model KHÔNG nhìn thấy, dù web vẫn hiển thị đầy đủ.
    /// </summary>
    public const int MaxContentChars = 4000;

    /// <summary>
    /// Hạn mức TỔNG cho toàn bộ hit trả về một lượt. Cần vì tool trả tối đa
    /// <see cref="MaxTake"/> hit: không có hạn mức tổng thì mấy mục dài cùng khớp sẽ nhân lên
    /// thành hàng chục nghìn ký tự nhét vào mỗi lượt gọi LLM.
    /// </summary>
    public const int MaxTotalContentChars = 8000;

    /// <summary>Còn ít hơn ngần này thì thôi không kèm hit tiếp — một mẩu cụt chẳng giúp được model.</summary>
    private const int MinUsefulContentChars = 300;

    /// <summary>Điểm cho mỗi token khớp, theo độ tin cậy của vùng khớp.</summary>
    private const int KeywordWeight = 3;
    private const int TitleWeight = 2;
    private const int ContentWeight = 1;

    /// <summary>Token ngắn hơn mức này bị bỏ (nhiễu, khớp bừa vào mọi thứ).</summary>
    private const int MinTokenLength = 2;

    /// <summary>
    /// Câu hỏi có từ 2 token trở lên thì phải khớp được ít nhất 2 token khác nhau mới coi là
    /// liên quan. Chỉ dựa vào điểm &gt; 0 là không đủ: bỏ dấu tiếng Việt làm "thủ đô" thành
    /// "thu do", rồi token "do" khớp luôn vào từ khoá "mang đồ" của mục hành lý — một token
    /// 2 ký tự đủ để trả về một mục hoàn toàn lệch chủ đề.
    /// </summary>
    private const int MinMatchedTokens = 2;

    /// <summary>
    /// NGOẠI LỆ của ngưỡng trên: một token dài khớp TRỌN MỘT TỪ trong Keywords là bằng chứng
    /// đủ mạnh để nhận, vì Keywords do admin tự khai nên độ chính xác cao.
    ///
    /// Cần ngoại lệ này vì câu hỏi tiếng Anh thường chỉ khớp đúng một từ khoá: "What is your
    /// refund policy?" chỉ khớp "refund" (what/is/your/policy không có trong nội dung tiếng
    /// Việt) — không có ngoại lệ thì bị loại oan. Ngưỡng độ dài loại được token nhiễu ngắn
    /// như "do", và điều kiện khớp trọn từ loại được khớp lấp lửng giữa từ.
    /// </summary>
    private const int MinStrongKeywordTokenLength = 4;

    /// <summary>
    /// Hư từ tiếng Việt (đã bỏ dấu). Giữ danh sách NGẮN có chủ ý: cắt quá tay thì những câu
    /// hỏi ngắn như "vé trẻ em thế nào" sẽ hết token và không tra được gì.
    /// </summary>
    private static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        "la", "gi", "the", "nao", "co", "khong", "cua", "cho", "va", "minh", "ban",
        "toi", "duoc", "thi", "voi", "khi", "nhu", "vay", "a", "nhe", "khi nao",
    };

    /// <summary>Tách câu hỏi thành token đã bỏ dấu, đã lọc hư từ. Rỗng = không đủ căn cứ để tra.</summary>
    public static string[] Tokenize(string? query)
    {
        var normalized = VietnameseTextSupport.Normalize(query);
        if (normalized.Length == 0)
        {
            return [];
        }

        return normalized
            .Split([' ', ',', '.', '?', '!', ';', ':', '\n', '\r', '\t', '(', ')', '"', '\''],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length >= MinTokenLength && !Stopwords.Contains(t))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Sắp xếp corpus theo độ khớp với câu hỏi. Chỉ trả entry có điểm &gt; 0 — thà không trả gì
    /// để trợ lý nói "chưa có thông tin", hơn là trả entry lệch chủ đề rồi model tưởng là câu trả lời.
    /// </summary>
    public static IReadOnlyList<KnowledgeSearchCandidate> Rank(
        IEnumerable<KnowledgeSearchCandidate> corpus,
        string? query,
        int take = DefaultTake)
    {
        var tokens = Tokenize(query);
        if (tokens.Length == 0)
        {
            return [];
        }

        var limit = Math.Clamp(take, 1, MaxTake);
        var requiredMatches = Math.Min(MinMatchedTokens, tokens.Length);

        return corpus
            .Select(entry => new { entry, match = Score(entry, tokens) })
            .Where(x => x.match.MatchedTokens >= requiredMatches || x.match.HasStrongKeywordHit)
            .OrderByDescending(x => x.match.Score)
            .ThenBy(x => x.entry.DisplayOrder)
            .ThenBy(x => x.entry.Title, StringComparer.Ordinal)
            .Take(limit)
            .Select(x => x.entry)
            .ToList();
    }

    /// <summary>Điểm khớp và số token khác nhau khớp được — cần cả hai để lọc ra kết quả thật sự liên quan.</summary>
    public static KnowledgeMatchScore Score(KnowledgeSearchCandidate entry, string[] tokens)
    {
        var title = VietnameseTextSupport.Normalize(entry.Title);
        var content = VietnameseTextSupport.Normalize(entry.Content);
        var keywords = VietnameseTextSupport.Normalize(string.Join(' ', entry.Keywords));

        // Tách từ khoá thành tập TỪ để phân biệt khớp trọn từ ("refund") với khớp lấp lửng
        // giữa từ ("do" nằm trong "mang do").
        var keywordWords = keywords
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

        var score = 0;
        var matchedTokens = 0;
        var strongKeywordHit = false;
        foreach (var token in tokens)
        {
            var hit = false;

            if (keywords.Contains(token, StringComparison.Ordinal))
            {
                score += KeywordWeight;
                hit = true;

                if (token.Length >= MinStrongKeywordTokenLength && keywordWords.Contains(token))
                {
                    strongKeywordHit = true;
                }
            }

            if (title.Contains(token, StringComparison.Ordinal))
            {
                score += TitleWeight;
                hit = true;
            }

            if (content.Contains(token, StringComparison.Ordinal))
            {
                score += ContentWeight;
                hit = true;
            }

            if (hit)
            {
                matchedTokens++;
            }
        }

        return new KnowledgeMatchScore(score, matchedTokens, strongKeywordHit);
    }

    public static string TruncateContent(string content) => TruncateContent(content, MaxContentChars);

    private static string TruncateContent(string content, int limit) =>
        content.Length <= limit ? content : content[..limit] + "...";

    /// <summary>
    /// Cắt nội dung của cả loạt hit sao cho tổng không vượt <see cref="MaxTotalContentChars"/>.
    /// Hit xếp trước (khớp tốt hơn) được ưu tiên giữ nguyên; hết ngân sách thì DỪNG kèm hit
    /// tiếp thay vì trả về một mẩu cụt vô nghĩa.
    ///
    /// Trả về danh sách có thể NGẮN HƠN đầu vào — bên gọi ghép lại theo thứ tự (đã giữ nguyên).
    /// </summary>
    public static IReadOnlyList<string> ApplyContentBudget(IEnumerable<string> contents)
    {
        var result = new List<string>();
        var remaining = MaxTotalContentChars;

        foreach (var content in contents)
        {
            if (remaining < MinUsefulContentChars)
            {
                break;
            }

            var trimmed = TruncateContent(content, Math.Min(MaxContentChars, remaining));
            result.Add(trimmed);
            remaining -= trimmed.Length;
        }

        return result;
    }
}
