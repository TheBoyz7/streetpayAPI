using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Streetpay.API.Models;

namespace Streetpay.API.Services
{
    public class KeyManagementService
    {
        private readonly EncryptionOptions _encryptionOptions;
        private readonly JwtOptions _jwtOptions;
        private readonly StreetPayDbContext _dbContext;
        private readonly Dictionary<int, string> _userPublicKeys;
        private readonly Dictionary<int, string> _transactionKeys;

        public KeyManagementService(
            IOptions<EncryptionOptions> encryptionOptions,
            IOptions<JwtOptions> jwtOptions,
            StreetPayDbContext dbContext)
        {
            _encryptionOptions = encryptionOptions.Value ?? throw new ArgumentNullException(nameof(encryptionOptions));
            _jwtOptions = jwtOptions.Value ?? throw new ArgumentNullException(nameof(jwtOptions));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _userPublicKeys = new Dictionary<int, string>();
            _transactionKeys = new Dictionary<int, string>();
        }

        /// <summary>
        /// Generate a new transaction key for a user.
        /// </summary>
        public string GenerateTransactionKey(int userId)
        {
            // Generate a secure random key
            byte[] keyBytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(keyBytes);
            }
            string transactionKey = Convert.ToBase64String(keyBytes);

            // Store in database
            var keyEntry = new TransactionKey
            {
                UserId = userId,
                Key = transactionKey,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(24) // Key expires in 24 hours
            };

            _dbContext.TransactionKeys.Add(keyEntry);
            _dbContext.SaveChanges();

            // Cache in memory
            _transactionKeys[userId] = transactionKey;

            return transactionKey;
        }

        /// <summary>
        /// Retrieve the transaction key for a user.
        /// </summary>
        public string GetTransactionKey(int userId)
        {
            // Check in-memory cache first
            if (_transactionKeys.TryGetValue(userId, out var key))
            {
                return key;
            }

            // Fallback to database
            var keyEntry = _dbContext.TransactionKeys
                .Where(k => k.UserId == userId && k.ExpiresAt > DateTime.UtcNow)
                .OrderByDescending(k => k.CreatedAt)
                .FirstOrDefault();

            if (keyEntry == null)
            {
                throw new KeyNotFoundException($"No valid transaction key found for user {userId}");
            }

            _transactionKeys[userId] = keyEntry.Key;
            return keyEntry.Key;
        }

        /// <summary>
        /// Store a device-generated transaction key for a user.
        /// </summary>
        public void StoreTransactionKey(int userId, string key, DateTime created)
        {
            // Check for existing key to prevent duplicates
            var existingKey = _dbContext.TransactionKeys
                .Where(k => k.UserId == userId && k.Key == key)
                .FirstOrDefault();

            if (existingKey != null)
            {
                throw new InvalidOperationException("Key already exists for this user.");
            }

            // Store in database
            var keyEntry = new TransactionKey
            {
                UserId = userId,
                Key = key,
                CreatedAt = created,
                ExpiresAt = created.AddDays(7) // Match frontend 7-day expiration
            };

            _dbContext.TransactionKeys.Add(keyEntry);
            _dbContext.SaveChanges();

            // Cache in memory
            _transactionKeys[userId] = key;
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