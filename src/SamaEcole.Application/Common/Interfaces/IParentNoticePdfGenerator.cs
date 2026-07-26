using SamaEcole.Application.VieScolaire.Queries.GetParentNotice;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>Rend la convocation de parent en PDF (A4) — même contrat que les autres générateurs de documents officiels.</summary>
public interface IParentNoticePdfGenerator
{
    byte[] Generate(ParentNoticeDto notice, byte[]? logo, byte[] qrCodeImage);
}
