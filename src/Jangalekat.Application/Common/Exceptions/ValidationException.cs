using FluentValidation.Results;

namespace Jangalekat.Application.Common.Exceptions;

/// <summary>Levée par ValidationBehavior, traduite en HTTP 422 par Jangalekat.Web (docs/Volume_4_API_Design.md §0.4).</summary>
public class ValidationException : Exception
{
    public IDictionary<string, string[]> Errors { get; }

    public ValidationException() : base("Une ou plusieurs erreurs de validation se sont produites.")
    {
        Errors = new Dictionary<string, string[]>();
    }

    public ValidationException(IEnumerable<ValidationFailure> failures) : this()
    {
        Errors = failures
            .GroupBy(f => f.PropertyName, f => f.ErrorMessage)
            .ToDictionary(g => g.Key, g => g.ToArray());
    }
}
