using FluentValidation;
using MediatR;
using ValidationException = Jangalekat.Application.Common.Exceptions.ValidationException;

namespace Jangalekat.Application.Common.Behaviors;

/// <summary>
/// Exécute automatiquement tous les IValidator&lt;TRequest&gt; enregistrés avant chaque Command/Query.
/// Aucun handler ne doit revalider manuellement ce que ce pipeline couvre déjà (AGENTS.md règle #7).
/// </summary>
public class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!validators.Any())
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);

        var failures = (await Task.WhenAll(
                validators.Select(v => v.ValidateAsync(context, cancellationToken))))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count != 0)
        {
            throw new ValidationException(failures);
        }

        return await next();
    }
}
