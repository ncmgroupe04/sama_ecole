using SamaEcole.Application.Common.Interfaces;
using MediatR;

namespace SamaEcole.Application.SchoolYears.Queries.ExportSchoolYear;

/// <summary>
/// GET /school-years/{id}/export — export ZIP (élèves, paiements, classes) d'une année scolaire, pour
/// archivage hors plateforme une fois l'année validée/clôturée (JGK-C01). Format CSV : ultra-simple à
/// générer, léger, et directement importable dans Excel par le Directeur — aucune dépendance NuGet
/// ajoutée, System.IO.Compression (BCL) suffit pour le ZIP.
///
/// IAuditableRequest (JGK-H01) : « exports » fait partie des écritures sensibles explicitement listées
/// par le journal d'audit centralisé.
/// </summary>
public record ExportSchoolYearQuery(Guid SchoolYearId) : IRequest<SchoolYearExportResult>, IAuditableRequest;

public record SchoolYearExportResult(byte[] Content, string FileName);
