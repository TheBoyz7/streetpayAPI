namespace Streetpay.API.Models;
public record WalletUpdateRequest
{
    public decimal Main { get; init; }
    public decimal Savings { get; init; }
}