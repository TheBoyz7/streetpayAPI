using Microsoft.Extensions.Options;
using Streetpay.API.Interfaces;
using Streetpay.API.Models.DTOs;
using Streetpay.API.Services;
using System.Security.Cryptography;
using System.Text.Json;

namespace Streetpay.API.Helpers
{
    public class Encryption
    {
        private readonly IAesCryptographyService _aes;
        private readonly IRsaCryptographyService _rsa;
        private readonly ILogger<Encryption> _logger;
        private readonly byte[] _aesKey;

        public Encryption(
            IAesCryptographyService aes,
            IRsaCryptographyService rsa,
            IOptions<EncryptionOptions> options,
            ILogger<Encryption> logger)
        {
            _aes = aes;
            _rsa = rsa;
            _logger = logger;
            _aesKey = Convert.FromBase64String(options.Value.AesKey);
        }

        public EncryptedResponseDto EncryptResponse<T>(T payload)
        {
            try
            {
                var iv = RandomNumberGenerator.GetBytes(16);
                var json = JsonSerializer.Serialize(payload);

                var encryptedData = _aes.Encrypt(json, _aesKey, iv);
                var encryptedKey = _rsa.Encrypt(Convert.ToBase64String(_aesKey));

                return new EncryptedResponseDto
                {
                    Data = encryptedData,
                    EncryptedKey = Convert.ToBase64String(encryptedKey),
                    Iv = Convert.ToBase64String(iv)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Encryption failed.");
                return new EncryptedResponseDto
                {
                    Data = string.Empty,
                    EncryptedKey = string.Empty,
                    Iv = string.Empty
                };
            }
        }
    }
}
