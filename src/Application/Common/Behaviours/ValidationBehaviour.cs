using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Common.Behaviours;

/// <summary>
/// Chạy toàn bộ FluentValidation validator (IValidator&lt;TRequest&gt;) đã đăng ký cho mỗi MediatR
/// request trước khi vào handler. Trước đây pipeline chỉ có AuthorizationBehaviour nên các
/// ...CommandValidator/...QueryValidator của MediatR không bao giờ chạy — validation phải nhét thủ công
/// trong handler. Behaviour này khép lại lỗ hổng đó: request không có validator thì bỏ qua (no-op).
/// </summary>
public sealed class ValidationBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehaviour(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (_validators.Any())
        {
            var context = new ValidationContext<TRequest>(request);

            var results = await Task.WhenAll(
                _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

            var failures = results
                .SelectMany(r => r.Errors)
                .Where(f => f is not null)
                .ToList();

            if (failures.Count != 0)
            {
                throw new ValidationException(failures);
            }
        }

        return await next(cancellationToken);
    }
}
