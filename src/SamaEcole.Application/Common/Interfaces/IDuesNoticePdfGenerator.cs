using SamaEcole.Application.Finance.Queries.GetDuesNotice;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>Rend la sommation pour impayés en PDF (A4) — même contrat que les autres générateurs de documents officiels.</summary>
public interface IDuesNoticePdfGenerator
{
    byte[] Generate(DuesNoticeDto notice, byte[]? logo, byte[] qrCodeImage);
}
