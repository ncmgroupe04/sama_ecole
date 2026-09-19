using FluentValidation;

namespace SamaEcole.Application.ClassJournal.Queries.GetClassJournal;

public class GetClassJournalQueryValidator : AbstractValidator<GetClassJournalQuery>
{
    /// <summary>
    /// Plafond de pageSize, motif GetStudentsQueryValidator : sans lui, `?pageSize=1000000`
    /// transforme une liste en déni de service — la borne n'est pas du confort, c'est la seule
    /// chose qui empêche le client de dicter la taille de la réponse.
    /// </summary>
    public const int MaxPageSize = 100;

    public GetClassJournalQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).GreaterThan(0).LessThanOrEqualTo(MaxPageSize);
    }
}
