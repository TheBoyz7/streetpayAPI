namespace Streetpay.API.Models.DTOs
{
    public class EncryptedResponseDto
    {
       public string Data { get; set; } = string.Empty;
       public string EncryptedKey { get; set; } = string.Empty;
       public string Iv { get; set; } = string.Empty;
     
  
    }
}
