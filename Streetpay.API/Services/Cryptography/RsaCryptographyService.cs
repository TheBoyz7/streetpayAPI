using Streetpay.API.Interfaces;
using System.Security.Cryptography;
using System.Text;

namespace Streetpay.API.Services.Cryptography
{
    public class RsaCryptographyService : IRsaCryptographyService
    {
        private readonly RSA _privateRsa;
        private readonly RSA _publicRsa;

        public RsaCryptographyService(IConfiguration config, IHostEnvironment env)
        {
            var privateKeyPath = Path.Combine(env.ContentRootPath, config["Encryption:privateKey"]);
            var publicKeyPath = Path.Combine(env.ContentRootPath, config["Encryption:publicKey"]);
            var privateKeyPassword = config["Encryption:privateKeyPassword"];

            if (!File.Exists(privateKeyPath) || !File.Exists(publicKeyPath))
                throw new FileNotFoundException("RSA key files not found. Ensure the PEM files are bundled with the app.");

            _privateRsa = RSA.Create();
            var privateKeyContent = File.ReadAllText(privateKeyPath);

            if (!string.IsNullOrWhiteSpace(privateKeyPassword))
            {
                _privateRsa.ImportFromEncryptedPem(privateKeyContent.ToCharArray(), privateKeyPassword.ToCharArray());
            }
            else
            {
                _privateRsa.ImportFromPem(privateKeyContent.ToCharArray());
            }

            _publicRsa = RSA.Create();
            var publicKeyContent = File.ReadAllText(publicKeyPath);
            _publicRsa.ImportFromPem(publicKeyContent.ToCharArray());
        }

        public byte[] Encrypt(string aesKey)
        {
            var bytes = Encoding.UTF8.GetBytes(aesKey);
            return _publicRsa.Encrypt(bytes, RSAEncryptionPadding.Pkcs1);
        }

        public string Decrypt(byte[] encryptedKey)
        {
            var decrypted = _privateRsa.Decrypt(encryptedKey, RSAEncryptionPadding.Pkcs1);
            return Encoding.UTF8.GetString(decrypted);
        }

        public string GetPublicKey()
        {
            return Convert.ToBase64String(_publicRsa.ExportSubjectPublicKeyInfo());
        }

        public void Dispose()
        {
            _privateRsa?.Dispose();
            _publicRsa?.Dispose();
        }
    }
}
