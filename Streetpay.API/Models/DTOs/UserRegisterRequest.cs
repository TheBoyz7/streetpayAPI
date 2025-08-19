namespace Streetpay.API.Models.DTOs
{
    public class UserRegisterRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Pin { get; set; } = string.Empty;
    }
}