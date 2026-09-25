using MediatR;

namespace SamaEcole.Application.ClassJournal.Queries.GetClassJournal;

/// <summary>
/// GET /api/v1/class-journal?page=&amp;pageSize=&amp;classroomId=&amp;subjectId=&amp;periodStart=&amp;periodEnd=
/// (ticket JGK-P04). LECTURE OUVERTE à tout rôle authentifié listé par le contrôleur — un journal
/// de classe est un document pédagogique partagé (continuité en cas de remplacement, contrôle
/// administratif), pas un carnet privé par enseignant : aucune restriction de périmètre au-delà du
/// tenant courant, y compris pour l'Enseignant.
/// </summary>
public record GetClassJournalQuery : IRequest<PaginatedClassJournalEntries>
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;

    public Guid? ClassroomId { get; init; }
    public Guid? SubjectId { get; init; }

    /// <summary>Bornes inclusives sur SessionDate — filtre « période » de l'écran.</summary>
    public DateOnly? PeriodStart { get; init; }
    public DateOnly? PeriodEnd { get; init; }
}

public record ClassJournalEntryListItem(
    Guid Id,
    Guid ClassroomId,
    string ClassroomName,
    Guid SubjectId,
    string SubjectName,
    Guid TeacherId,
    string TeacherName,
    DateOnly SessionDate,
    string Topic,
    string Content,
    string? Homework,
    DateOnly? HomeworkDueDate,
    uint RowVersion,

    /// <summary>
    /// Calculé serveur (ClassJournalEditWindow.CanCorrect) : l'écran s'en sert pour masquer
    /// Modifier/Supprimer, la vraie garde restant côté Update/Delete (403 sinon). Évite à l'écran
    /// de deviner l'heure serveur ou, pour l'Enseignant, sa propre fiche enseignant.
    /// </summary>
    bool CanEdit,

    /// <summary>Unités du programme pointées pour cette séance (Évolution N°7) ; vide si aucune.</summary>
    IReadOnlyList<Guid>? SyllabusUnitIds = null);

public record PaginatedClassJournalEntries(
    IReadOnlyList<ClassJournalEntryListItem> Items,
    int TotalCount,
    int Page,
    int PageSize);
