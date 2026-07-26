using SamaEcole.Application.Discipline.Queries.GetDisciplinaryPv;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>Rend le PV de sanction disciplinaire en PDF (A4) — même contrat que les autres générateurs de documents officiels.</summary>
public interface IDisciplinaryPvPdfGenerator
{
    byte[] Generate(DisciplinaryPvDto pv, byte[]? logo, byte[] qrCodeImage);
}
