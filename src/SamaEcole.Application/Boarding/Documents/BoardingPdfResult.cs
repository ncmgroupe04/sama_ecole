namespace SamaEcole.Application.Boarding.Documents;

/// <param name="Content">Octets du PDF.</param>
/// <param name="FileStem">Radical du nom de fichier (matricule, ou pavillon et date) ; assaini par le contrôleur.</param>
public record BoardingPdfResult(byte[] Content, string FileStem);
