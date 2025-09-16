namespace Streetpay.API.Models;
using System.ComponentModel.DataAnnotations;

public record WalletUpdateRequest
{
    [Required]
    public decimal Main { get; init; }

    [Required]
    public decimal Savings { get; init; }

    [Required]
    public string TransactionId { get; init; }
}