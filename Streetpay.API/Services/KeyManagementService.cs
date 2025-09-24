using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using Streetpay.API.Models;

namespace Streetpay.API.Services
{
    public class KeyManagementService
    {
        private readonly EncryptionOptions _encryptionOptions;
        private readonly JwtOptions _jwtOptions;
        private readonly Dictionary<int, string> _userPublicKeys;

        public KeyManagementService(
            IOptions<EncryptionOptions> encryptionOptions,
            IOptions<JwtOptions> jwtOptions)
        {
            _encryptionOptions = encryptionOptions.Value ?? throw new ArgumentNullException(nameof(encryptionOptions));
            _jwtOptions = jwtOptions.Value ?? throw new ArgumentNullException(nameof(jwtOptions));
            _userPublicKeys = new Dictionary<int, string>();
        }

        /// <summary>
        /// Get JWT secret key from configuration.
        /// </summary>
        public string GetJwtSecret()
        {
            if (string.IsNullOrWhiteSpace(_jwtOptions.Secret))
                throw new InvalidOperationException("JWT secret not configured. Please check appsettings.json under 'Jwt:Secret'.");

            return _jwtOptions.Secret;
        }

        /// <summary>
        /// Generate a new AES key (Base64 encoded).
        /// </summary>
        public string GenerateAesKey()
        {
            using var aes = Aes.Create();
            aes.GenerateKey();
            return Convert.ToBase64String(aes.Key);
        }

        /// <summary>
        /// Generate an RSA key pair (Base64 encoded).
        /// </summary>
        public (string PublicKey, string PrivateKey) GenerateRsaKeyPair()
        {
            using var rsa = RSA.Create(2048);
            var publicKey = Convert.ToBase64String(rsa.ExportRSAPublicKey());
            var privateKey = Convert.ToBase64String(rsa.ExportRSAPrivateKey());
            return (publicKey, privateKey);
        }

        /// <summary>
        /// Store a user’s public key for later encryption.
        /// </summary>
        public void StorePublicKeyForUser(int userId, string publicKey)
        {
            _userPublicKeys[userId] = publicKey;
        }

        /// <summary>
        /// Retrieve a stored public key for a given user.
        /// </summary>
        public string GetPublicKeyForUser(int userId)
        {
            if (_userPublicKeys.TryGetValue(userId, out var publicKey))
            {
                return publicKey;
            }
            throw new KeyNotFoundException($"No public key found for user {userId}");
        }

        /// <summary>
        /// Encrypt text using AES with a provided key.
        /// </summary>
        public byte[] EncryptWithAes(string data, string aesKey)
        {
            using var aes = Aes.Create();
            aes.Key = Convert.FromBase64String(aesKey);
            aes.GenerateIV();

            using var ms = new MemoryStream();
            ms.Write(aes.IV, 0, aes.IV.Length);

            using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
            {
                var dataBytes = Encoding.UTF8.GetBytes(data);
                cs.Write(dataBytes, 0, dataBytes.Length);
                cs.FlushFinalBlock();
            }

            return ms.ToArray();
        }

        /// <summary>
        /// Decrypt AES-encrypted data.
        /// </summary>
        public string DecryptWithAes(byte[] encryptedData, string aesKey)
        {
            using var aes = Aes.Create();
            aes.Key = Convert.FromBase64String(aesKey);

            var iv = new byte[16];
            Array.Copy(encryptedData, 0, iv, 0, iv.Length);
            aes.IV = iv;

            using var ms = new MemoryStream();
            using (var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Write))
            {
                cs.Write(encryptedData, iv.Length, encryptedData.Length - iv.Length);
                cs.FlushFinalBlock();
            }

            return Encoding.UTF8.GetString(ms.ToArray());
        }
    }
}
