namespace Streetpay.API.Models.DTOs
{
    public class UserLoginRequest
    {
        public string Phone { get; set; } = string.Empty;
        public string Pin { get; set; } = string.Empty;
        public required string DeviceId { get; init; } = string.Empty;
    }
}