using CashFlow.SharedKernel.Application;
using FluentValidation;
using MediatR;

namespace CashFlow.Entries.Application.Behaviors;

/// <summary>
/// MediatR pipeline behavior that runs FluentValidation validators before the handler.
/// If validation fails, returns a Result.Failure without invoking the handler.
/// Constrains TResponse to Result so validation errors can be returned uniformly.
/// </summary>
public class ValidationPipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationPipelineBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!_validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);

        var failures = _validators
            .Select(v => v.Validate(context))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count == 0)
            return await next();

        var errorMessage = string.Join("; ", failures.Select(f => f.ErrorMessage));

        // TResponse is always a Result<T> in this solution.
        // Use reflection to construct Result<T>.Failure for the correct generic type.
        var responseType = typeof(TResponse);
        if (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var innerType = responseType.GetGenericArguments()[0];
            var failureMethod = typeof(Result)
                .GetMethods()
                .First(m => m.Name == nameof(Result.Failure) && m.IsGenericMethodDefinition)
                .MakeGenericMethod(innerType);
            return (TResponse)failureMethod.Invoke(null, new object[] { errorMessage })!;
        }

        // Fallback for non-generic Result (shouldn't happen in this solution)
        if (responseType == typeof(Result))
            return (TResponse)(object)Result.Failure(errorMessage);

        // Cannot inject validation error for unknown TResponse — pass through to handler
        return await next();
    }
}
