using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Knowledge;

[Authorize(Roles = "Admin")]
public sealed record CreateKnowledgeEntryCommand(
    string Title,
    string Content,
    string Category,
    IReadOnlyCollection<string>? Keywords = null,
    string? Status = null,
    int DisplayOrder = 0) : IRequest<KnowledgeEntryDto>;

public sealed class CreateKnowledgeEntryCommandValidator : AbstractValidator<CreateKnowledgeEntryCommand>
{
    public CreateKnowledgeEntryCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Content).NotEmpty();
        RuleFor(x => x.Category)
            .NotEmpty()
            .Must(KnowledgeCategories.IsValid)
            .WithMessage($"Category hop le: {string.Join(" | ", KnowledgeCategories.All)}.");
        RuleFor(x => x.Status)
            .Must(x => string.IsNullOrWhiteSpace(x) || KnowledgeEntry.IsValidStatus(x))
            .WithMessage("Status hop le: Draft | Published.");
        RuleFor(x => x.Keywords)
            .Must(KnowledgeEntrySupport.IsKeywordCountValid)
            .WithMessage($"Toi da {KnowledgeEntrySupport.MaxKeywords} tu khoa.")
            .Must(KnowledgeEntrySupport.IsKeywordLengthValid)
            .WithMessage($"Moi tu khoa toi da {KnowledgeEntrySupport.MaxKeywordLength} ky tu.");
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed class CreateKnowledgeEntryCommandHandler
    : IRequestHandler<CreateKnowledgeEntryCommand, KnowledgeEntryDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public CreateKnowledgeEntryCommandHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<KnowledgeEntryDto> Handle(
        CreateKnowledgeEntryCommand request,
        CancellationToken cancellationToken)
    {
        // Tác giả lấy từ JWT, không nhận từ payload — tránh gán bừa cho người khác.
        var authorId = _userContext.UserId
            ?? throw new ValidationException([new ValidationFailure(
                nameof(request.Title), "Khong xac dinh duoc nguoi dung hien tai.")]);

        var entry = new KnowledgeEntry
        {
            Title = request.Title.Trim(),
            Content = request.Content.Trim(),
            Category = KnowledgeEntrySupport.ResolveCategory(request.Category),
            Keywords = KnowledgeEntrySupport.SanitizeKeywords(request.Keywords),
            Status = KnowledgeEntrySupport.ResolveStatus(request.Status),
            DisplayOrder = request.DisplayOrder,
            AuthorId = authorId,
        };

        _context.Set<KnowledgeEntry>().Add(entry);
        await _context.SaveChangesAsync(cancellationToken);
        return KnowledgeEntrySupport.ToDto(entry);
    }
}
