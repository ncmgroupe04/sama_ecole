namespace SamaEcole.Application.Common.Interfaces;

public interface IQrCodeService
{
    byte[] GenerateQrCode(string text);
}
