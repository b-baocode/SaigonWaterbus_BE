using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Infrastructure.Ai;

/// <summary>
/// Cài đặt <see cref="IChatCompletionService"/> bằng Google Gemini (REST generateContent).
/// Đây là file DUY NHẤT biết đến định dạng của Gemini. Đổi provider = viết class khác
/// implement cùng interface, không đụng tầng Application.
/// </summary>
public sealed class GeminiChatCompletionService : IChatCompletionService
{
    public const string HttpClientName = "Gemini";

    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GeminiOptions _options;

    public GeminiChatCompletionService(IHttpClientFactory httpClientFactory, IOptions<GeminiOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException(
                "Chưa cấu hình Gemini:ApiKey. Đặt key vào appsettings.Local.json (dev) hoặc app settings (deploy).");
        }

        var body = BuildRequestBody(request);
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var url = $"{_options.ApiBaseUrl.TrimEnd('/')}/models/{_options.Model}:generateContent?key={_options.ApiKey}";

        using var response = await client.PostAsJsonAsync(url, body, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Gemini API lỗi {(int)response.StatusCode}: {json}");
        }

        return ParseResponse(json);
    }

    private JsonObject BuildRequestBody(ChatCompletionRequest request)
    {
        var contents = new JsonArray();
        foreach (var message in request.Messages)
        {
            switch (message.Role)
            {
                case ChatRole.User:
                    contents.Add(new JsonObject
                    {
                        ["role"] = "user",
                        ["parts"] = new JsonArray { new JsonObject { ["text"] = message.Text ?? string.Empty } },
                    });
                    break;

                case ChatRole.Assistant:
                    var parts = new JsonArray();
                    if (!string.IsNullOrWhiteSpace(message.Text))
                    {
                        parts.Add(new JsonObject { ["text"] = message.Text });
                    }

                    foreach (var call in message.ToolCalls)
                    {
                        var functionCallPart = new JsonObject
                        {
                            ["functionCall"] = new JsonObject
                            {
                                ["name"] = call.Name,
                                ["args"] = ToNode(call.Arguments),
                            },
                        };

                        // Model "thinking" (gemini-flash-latest) bắt buộc gửi lại
                        // thought_signature đã nhận, nếu không API trả 400.
                        if (!string.IsNullOrEmpty(call.Signature))
                        {
                            functionCallPart["thoughtSignature"] = call.Signature;
                        }

                        parts.Add(functionCallPart);
                    }

                    contents.Add(new JsonObject { ["role"] = "model", ["parts"] = parts });
                    break;

                case ChatRole.Tool:
                    contents.Add(new JsonObject
                    {
                        ["role"] = "user",
                        ["parts"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["functionResponse"] = new JsonObject
                                {
                                    ["name"] = message.ToolName,
                                    ["response"] = ToResponseObject(message.Text),
                                },
                            },
                        },
                    });
                    break;
            }
        }

        var declarations = new JsonArray();
        foreach (var tool in request.Tools)
        {
            var declaration = new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
            };

            // Gemini từ chối functionDeclaration có parameters rỗng (properties trống),
            // nên chỉ đính schema khi thực sự có tham số.
            if (HasProperties(tool.ParametersSchema))
            {
                declaration["parameters"] = ToNode(tool.ParametersSchema);
            }

            declarations.Add(declaration);
        }

        var generationConfig = new JsonObject { ["maxOutputTokens"] = _options.MaxOutputTokens };

        // Tắt/giới hạn "thinking" để giảm độ trễ. -1 = không gửi (model tự quyết);
        // 0 = tắt hẳn (chatbox routing không cần suy nghĩ) → nhanh hơn nhiều.
        if (_options.ThinkingBudget >= 0)
        {
            generationConfig["thinkingConfig"] = new JsonObject { ["thinkingBudget"] = _options.ThinkingBudget };
        }

        return new JsonObject
        {
            ["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray { new JsonObject { ["text"] = request.SystemPrompt } },
            },
            ["contents"] = contents,
            ["tools"] = new JsonArray { new JsonObject { ["functionDeclarations"] = declarations } },
            ["generationConfig"] = generationConfig,
        };
    }

    private static ChatCompletionResult ParseResponse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            return new ChatCompletionResult("Xin lỗi, mình chưa trả lời được ngay lúc này.", Array.Empty<ChatToolCall>());
        }

        var candidate = candidates[0];
        if (!candidate.TryGetProperty("content", out var content)
            || !content.TryGetProperty("parts", out var parts))
        {
            return new ChatCompletionResult(string.Empty, Array.Empty<ChatToolCall>());
        }

        string? text = null;
        var toolCalls = new List<ChatToolCall>();

        foreach (var part in parts.EnumerateArray())
        {
            // Part có "thought": true là phần SUY NGHĨ nội bộ của model, không phải câu trả lời.
            // Gộp nhầm vào text là khách thấy nguyên đoạn "Khách nói ngắn gọn 'mai', nghĩa là..."
            // hiện trong khung chat.
            if (IsThought(part))
            {
                continue;
            }

            if (part.TryGetProperty("text", out var textElement))
            {
                text = (text ?? string.Empty) + textElement.GetString();
            }
            else if (part.TryGetProperty("functionCall", out var functionCall))
            {
                var name = functionCall.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                var args = functionCall.TryGetProperty("args", out var a) ? a.Clone() : EmptyObject;

                // thought_signature nằm ngang cấp với functionCall trong part; phải
                // giữ lại để gửi trả ở lượt sau (yêu cầu của model thinking).
                var signature = part.TryGetProperty("thoughtSignature", out var ts) ? ts.GetString() : null;

                // Gemini không trả id cho functionCall; khớp bằng name, nên dùng name làm id.
                toolCalls.Add(new ChatToolCall(name, name, args, signature));
            }
        }

        return new ChatCompletionResult(text, toolCalls);
    }

    private static bool IsThought(JsonElement part) =>
        part.TryGetProperty("thought", out var thought)
        && thought.ValueKind == JsonValueKind.True;

    private static JsonNode? ToNode(JsonElement element) =>
        JsonNode.Parse(element.GetRawText());

    private static JsonNode ToResponseObject(string? toolResultJson)
    {
        // functionResponse.response bắt buộc là object. Tool của ta luôn trả object,
        // nhưng bọc phòng hờ trường hợp không parse được.
        if (string.IsNullOrWhiteSpace(toolResultJson))
        {
            return new JsonObject();
        }

        try
        {
            var node = JsonNode.Parse(toolResultJson);
            return node as JsonObject ?? new JsonObject { ["result"] = node };
        }
        catch (JsonException)
        {
            return new JsonObject { ["result"] = toolResultJson };
        }
    }

    private static bool HasProperties(JsonElement schema) =>
        schema.ValueKind == JsonValueKind.Object
        && schema.TryGetProperty("properties", out var properties)
        && properties.ValueKind == JsonValueKind.Object
        && properties.EnumerateObject().Any();
}
