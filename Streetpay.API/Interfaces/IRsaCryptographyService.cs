namespace Streetpay.API.Interfaces
{
    public interface IRsaCryptographyService
    {
        byte[] Encrypt(string aesKey);
        string Decrypt(byte[] encryptedKey);
        string GetPublicKey();
    }
}
