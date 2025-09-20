namespace Streetpay.API.Interfaces
{
    public interface IAesCryptographyService
    {
        public string Encrypt(string plainText,byte[] kry, byte[] iv);
        public string Decrypt(string cipherText,byte[] key, byte[] iv);
    }
}
