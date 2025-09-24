namespace Streetpay.API.Services
{
    public class EncryptionOptions
{
    public string AesKey { get; set; } = default!;
    public string JwtSecret { get; set; } = string.Empty;
}
}
