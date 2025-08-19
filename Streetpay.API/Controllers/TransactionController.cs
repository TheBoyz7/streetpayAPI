using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Streetpay.API.Models;
using Streetpay.API.Models.DTOs;

namespace Streetpay.API.Controllers
{
    [ApiController]
    [Route("transactions")]
    public class TransactionController : ControllerBase
    {
        private readonly StreetPayDbContext _db;

        public TransactionController(StreetPayDbContext db)
        {
            _db = db;
        }

        [HttpPost]
        public async Task<IActionResult> CreateTransaction([FromBody] Transaction txn)
        {
            _db.Transactions.Add(txn);
            await _db.SaveChangesAsync();
            return Created($"/transactions/{txn.Id}", txn);
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var all = await _db.Transactions.ToListAsync();
            return Ok(all);
        }

        [HttpGet("pending")]
        public async Task<IActionResult> GetPending()
        {
            var pending = await _db.Transactions
                .Where(t => !t.IsSynced)
                .ToListAsync();

            return Ok(pending);
        }

        [HttpPost("receive-offline")]
        public async Task<IActionResult> ReceiveOffline([FromBody] OfflineTransactionDto dto)
        {
            var sender = await _db.Users.FirstOrDefaultAsync(u => u.Phone == dto.SenderPhone);
            var receiver = await _db.Users.FirstOrDefaultAsync(u => u.Phone == dto.ReceiverPhone);

            if (sender == null || receiver == null)
                return BadRequest("Invalid sender or receiver");

            var txn = new Transaction
            {
                SenderPhone = dto.SenderPhone,
                ReceiverPhone = dto.ReceiverPhone,
                Amount = dto.Amount,
                Status = "pending",
                IsSynced = false
            };

            _db.Transactions.Add(txn);
            await _db.SaveChangesAsync();

            return Created($"/transactions/{txn.Id}", txn);
        }

        [HttpPost("sync")]
        public async Task<IActionResult> SyncTransactions([FromBody] List<OfflineTransactionDto> txns)
        {
            foreach (var dto in txns)
            {
                var sender = await _db.Users.FirstOrDefaultAsync(u => u.Phone == dto.SenderPhone);
                var receiver = await _db.Users.FirstOrDefaultAsync(u => u.Phone == dto.ReceiverPhone);

                if (sender == null || receiver == null)
                    continue;

                if (sender.MainBalance < dto.Amount)
                    continue;

                sender.MainBalance -= dto.Amount;
                receiver.MainBalance += dto.Amount;

                var txn = new Transaction
                {
                    SenderPhone = dto.SenderPhone,
                    ReceiverPhone = dto.ReceiverPhone,
                    Amount = dto.Amount,
                    Status = "synced",
                    IsSynced = true,
                    Timestamp = dto.Timestamp
                };

                _db.Transactions.Add(txn);
            }

            await _db.SaveChangesAsync();
            return Ok("Sync complete");
        }
    }
}
