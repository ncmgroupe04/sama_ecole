using SamaEcole.Application.Finance.Queries.GetHourRecordSheet;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>Rend la fiche de suivi des heures (Vacataire) en PDF (A4) — même contrat que le bulletin de paie.</summary>
public interface IHourRecordSheetPdfGenerator
{
    byte[] Generate(HourRecordSheetDto sheet, byte[]? logo);
}
