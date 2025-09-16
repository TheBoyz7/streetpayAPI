using Microsoft.EntityFrameworkCore;
using Streetpay.API.Models;
using Streetpay.API.Models.DTOs;
using System.ComponentModel.DataAnnotations;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);

// Register services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<StreetPayDbContext>(options =>
    options.UseSqlite("Data Source=streetpay.db;Pooling=false"));

var app = builder.Build();

// Middleware
app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => Results.Ok(new { Message = "StreetPay Offline API is live" }));

// Register a new user (unchanged)
app.MapPost("/auth/register", async (UserRegisterRequest req, StreetPayDbContext db) =>
{
    var validationResults = new List<ValidationResult>();
    if (!Validator.TryValidateObject(req, new ValidationContext(req), validationResults, true))
    {
        return Results.BadRequest(new { Message = "Invalid input", Errors = validationResults.Select(v => v.ErrorMessage) });
    }

    var existing = await db.Users.FirstOrDefaultAsync(u => u.Phone == req.Phone);
    if (existing != null)
        return Results.Conflict(new { Message = "User already exists" });

    var newUser = new User
    {
        Name = req.Name,
        Phone = req.Phone,
        Pin = req.Pin // TODO: Hash PIN in production
    };

    db.Users.Add(newUser);
    try
    {
        await db.SaveChangesAsync();
        return Results.Created($"/auth/profile/{newUser.Id}", new
        {
            newUser.Id,
            newUser.Name,
            newUser.Phone
        });
    }
    catch (SqliteException ex) when (ex.SqliteErrorCode == 5)
    {
        Console.WriteLine($"Database locked during user registration: {ex.Message}");
        return Results.StatusCode(503);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error registering user: {ex.Message}");
        return Results.StatusCode(500);
    }
});

// Login user (unchanged)
app.MapPost("/auth/login", async (UserLoginRequest login, StreetPayDbContext db) =>
{
    var validationResults = new List<ValidationResult>();
    if (!Validator.TryValidateObject(login, new ValidationContext(login), validationResults, true))
    {
        return Results.BadRequest(new { Message = "Invalid input", Errors = validationResults.Select(v => v.ErrorMessage) });
    }

    var user = await db.Users.FirstOrDefaultAsync(u => u.Phone == login.Phone && u.Pin == login.Pin);
    if (user == null)
        return Results.Unauthorized();

    return Results.Ok(new
    {
        id = user.Id,
        name = user.Name,
        phone = user.Phone,
        token = "mock-jwt-token-123" // TODO: Implement proper JWT in production
    });
});

// Profile (unchanged)
app.MapGet("/auth/profile/{id}", async (int id, StreetPayDbContext db) =>
{
    var user = await db.Users.FindAsync(id);
    if (user == null)
        return Results.NotFound(new { Message = "User not found" });

    return Results.Ok(new
    {
        user.Id,
        user.Name,
        user.Phone
    });
});

// Wallet (unchanged)
app.MapGet("/wallet/{id}", async (int id, StreetPayDbContext db) =>
{
    var user = await db.Users.FindAsync(id);
    if (user == null)
        return Results.NotFound(new { Message = "User not found" });

    var wallet = new WalletResponse
    {
        Main = user.MainBalance,
        Savings = user.SavingsBalance
    };

    return Results.Ok(wallet);
});

// Update wallet balance (unchanged)
app.MapPut("/wallet/{id}", async (int id, WalletUpdateRequest wallet, StreetPayDbContext db) =>
{
    var user = await db.Users.FindAsync(id);
    if (user == null)
        return Results.NotFound(new { Message = "User not found" });

    var validationResults = new List<ValidationResult>();
    if (!Validator.TryValidateObject(wallet, new ValidationContext(wallet), validationResults, true))
    {
        return Results.BadRequest(new { Message = "Invalid wallet data", Errors = validationResults.Select(v => v.ErrorMessage) });
    }

    user.MainBalance = wallet.Main;
    user.SavingsBalance = wallet.Savings;
    try
    {
        await db.SaveChangesAsync();
        return Results.Ok(new { Message = "Wallet updated", Main = user.MainBalance, Savings = user.SavingsBalance });
    }
    catch (SqliteException ex) when (ex.SqliteErrorCode == 5)
    {
        Console.WriteLine($"Database locked during wallet update: {ex.Message}");
        return Results.StatusCode(503);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error updating wallet: {ex.Message}");
        return Results.StatusCode(500);
    }
});

// Create new transaction (unchanged)
app.MapPost("/transactions", async (Transaction txn, StreetPayDbContext db) =>
{
    var validationResults = new List<ValidationResult>();
    if (!Validator.TryValidateObject(txn, new ValidationContext(txn), validationResults, true))
    {
        return Results.BadRequest(new { Message = "Invalid transaction data", Errors = validationResults.Select(v => v.ErrorMessage) });
    }

    if (await db.Transactions.AnyAsync(t => t.TransactionId == txn.TransactionId))
        return Results.Conflict(new { Message = "Transaction already exists" });

    db.Transactions.Add(txn);
    try
    {
        await db.SaveChangesAsync();
        return Results.Created($"/transactions/{txn.Id}", txn);
    }
    catch (SqliteException ex) when (ex.SqliteErrorCode == 5)
    {
        Console.WriteLine($"Database locked during transaction creation: {ex.Message}");
        return Results.StatusCode(503);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error creating transaction: {ex.Message}");
        return Results.StatusCode(500);
    }
});

// Get all transactions (unchanged)
app.MapGet("/transactions", async (StreetPayDbContext db) =>
{
    var all = await db.Transactions.ToListAsync();
    return Results.Ok(all);
});

// Pending transactions (unchanged)
app.MapGet("/transactions/pending", async (StreetPayDbContext db) =>
{
    var pending = await db.Transactions
        .Where(t => !t.IsSynced)
        .ToListAsync();

    return Results.Ok(pending);
});

// Get user by phone number (unchanged)
app.MapGet("/users/by-phone/{phone}", async (string phone, StreetPayDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(phone))
        return Results.BadRequest(new { Message = "Phone number is required" });

    var user = await db.Users.FirstOrDefaultAsync(u => u.Phone == phone);
    if (user == null)
        return Results.NotFound(new { Message = "User not found" });

    return Results.Ok(new { id = user.Id, name = user.Name });
});

// Send money online (updated with transaction scope)
app.MapPost("/transactions/send", async (OnlineTransactionDto dto, StreetPayDbContext db) =>
{
    var validationResults = new List<ValidationResult>();
    if (!Validator.TryValidateObject(dto, new ValidationContext(dto), validationResults, true))
    {
        return Results.BadRequest(new { Message = "Invalid transaction data", Errors = validationResults.Select(v => v.ErrorMessage) });
    }

    var sender = await db.Users.FindAsync(dto.SenderId);
    var receiver = await db.Users.FindAsync(dto.ReceiverId);

    if (sender == null || receiver == null)
        return Results.BadRequest(new { Message = "Sender or receiver not found" });

    if (dto.Amount <= 0)
        return Results.BadRequest(new { Message = "Invalid amount" });

    if (sender.MainBalance < dto.Amount)
        return Results.BadRequest(new { Message = "Insufficient balance" });

    if (await db.Transactions.AnyAsync(t => t.TransactionId == dto.TransactionId))
        return Results.Conflict(new { Message = "Transaction already exists" });

    using var transaction = await db.Database.BeginTransactionAsync();
    try
    {
        sender.MainBalance -= dto.Amount;
        receiver.MainBalance += dto.Amount;

        var txn = new Transaction
        {
            TransactionId = dto.TransactionId,
            SenderId = sender.Id,
            ReceiverId = receiver.Id,
            SenderPhone = sender.Phone,
            ReceiverPhone = receiver.Phone,
            Amount = dto.Amount,
            SenderNewBalance = sender.MainBalance,
            ReceiverNewBalance = receiver.MainBalance,
            Currency = "NGN",
            Status = "sent",
            IsSynced = true,
            Timestamp = DateTime.UtcNow,
            Type = "online",
            Used = true,
            Signature = string.Empty
        };

        db.Transactions.Add(txn);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        return Results.Ok(new
        {
            message = "Transaction successful",
            transactionId = txn.TransactionId,
            amount = txn.Amount,
            to = receiver.Name,
            senderNewBalance = txn.SenderNewBalance,
            receiverNewBalance = txn.ReceiverNewBalance
        });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        Console.WriteLine($"Error during online transaction: {ex.Message}");
        return Results.StatusCode(500);
    }
});

// Receive offline (updated to pend and validate during sync)
app.MapPost("/transactions/receive-offline", async (OfflineTransactionDto dto, StreetPayDbContext db) =>
{
    var validationResults = new List<ValidationResult>();
    if (!Validator.TryValidateObject(dto, new ValidationContext(dto), validationResults, true))
    {
        return Results.BadRequest(new { Message = "Invalid transaction data", Errors = validationResults.Select(v => v.ErrorMessage) });
    }

    var sender = await db.Users.FirstOrDefaultAsync(u => u.Phone == dto.SenderPhone);
    var receiver = await db.Users.FirstOrDefaultAsync(u => u.Phone == dto.ReceiverPhone);

    if (sender == null || receiver == null)
        return Results.BadRequest(new { Message = "Invalid sender or receiver" });

    if (dto.Amount <= 0)
        return Results.BadRequest(new { Message = "Invalid amount" });

    var txn = new Transaction
    {
        TransactionId = dto.TransactionId,
        SenderId = sender.Id,
        ReceiverId = receiver.Id,
        SenderPhone = dto.SenderPhone,
        ReceiverPhone = dto.ReceiverPhone,
        Amount = dto.Amount,
        SenderNewBalance = sender.MainBalance, // Current balance, to be updated on sync
        ReceiverNewBalance = receiver.MainBalance + dto.Amount,
        Currency = "NGN",
        Status = "pending",
        IsSynced = false,
        Timestamp = dto.Timestamp,
        Type = "offline",
        Used = true,
        Signature = string.Empty
    };

    db.Transactions.Add(txn);
    try
    {
        await db.SaveChangesAsync();
        return Results.Created($"/transactions/{txn.Id}", new
        {
            txn.Id,
            txn.TransactionId,
            txn.SenderPhone,
            txn.ReceiverPhone,
            txn.Amount,
            txn.SenderNewBalance,
            txn.ReceiverNewBalance,
            txn.Currency,
            txn.Status,
            txn.IsSynced,
            txn.Timestamp,
            txn.Type,
            txn.Used,
            txn.Signature
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error during offline transaction: {ex.Message}");
        return Results.StatusCode(500);
    }
});

// Sync pending transactions (updated with transaction scope and validation)
app.MapPost("/transactions/sync", async (List<OfflineTransactionDto> txns, StreetPayDbContext db) =>
{
    var validationResults = new List<ValidationResult>();
    var results = new List<object>();

    using var transaction = await db.Database.BeginTransactionAsync();
    try
    {
        foreach (var dto in txns)
        {
            if (!Validator.TryValidateObject(dto, new ValidationContext(dto), validationResults, true))
            {
                results.Add(new { TransactionId = dto.TransactionId, Status = "Failed", Message = "Invalid transaction data" });
                continue;
            }

            var sender = await db.Users.FirstOrDefaultAsync(u => u.Phone == dto.SenderPhone);
            var receiver = await db.Users.FirstOrDefaultAsync(u => u.Phone == dto.ReceiverPhone);

            if (sender == null || receiver == null)
            {
                results.Add(new { TransactionId = dto.TransactionId, Status = "Failed", Message = "Sender or receiver not found" });
                continue;
            }

            if (dto.Amount <= 0)
            {
                results.Add(new { TransactionId = dto.TransactionId, Status = "Failed", Message = "Invalid amount" });
                continue;
            }

            if (sender.MainBalance < dto.Amount)
            {
                results.Add(new { TransactionId = dto.TransactionId, Status = "Failed", Message = "Insufficient sender balance" });
                continue;
            }

            var existingTxn = await db.Transactions.FirstOrDefaultAsync(t => t.TransactionId == dto.TransactionId);
            if (existingTxn != null)
            {
                if (existingTxn.IsSynced)
                {
                    results.Add(new { TransactionId = dto.TransactionId, Status = "Skipped", Message = "Transaction already synced" });
                    continue;
                }
                existingTxn.Status = "synced";
                existingTxn.IsSynced = true;
                existingTxn.SenderId = sender.Id;
                existingTxn.ReceiverId = receiver.Id;
                existingTxn.Type = "offline";
                existingTxn.Used = true;
                existingTxn.SenderNewBalance = sender.MainBalance - dto.Amount;
                existingTxn.ReceiverNewBalance = receiver.MainBalance + dto.Amount;
            }
            else
            {
                var txn = new Transaction
                {
                    TransactionId = dto.TransactionId,
                    SenderId = sender.Id,
                    ReceiverId = receiver.Id,
                    SenderPhone = dto.SenderPhone,
                    ReceiverPhone = dto.ReceiverPhone,
                    Amount = dto.Amount,
                    SenderNewBalance = sender.MainBalance - dto.Amount,
                    ReceiverNewBalance = receiver.MainBalance + dto.Amount,
                    Currency = "NGN",
                    Status = "synced",
                    IsSynced = true,
                    Timestamp = dto.Timestamp,
                    Type = "offline",
                    Used = true,
                    Signature = string.Empty
                };
                db.Transactions.Add(txn);
            }

            sender.MainBalance -= dto.Amount;
            receiver.MainBalance += dto.Amount;

            results.Add(new { TransactionId = dto.TransactionId, Status = "Success", Message = "Transaction synced", SenderNewBalance = sender.MainBalance, ReceiverNewBalance = receiver.MainBalance });
        }

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Results.Ok(new { Message = "Sync complete", Results = results });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        Console.WriteLine($"Error during sync: {ex.Message}");
        return Results.StatusCode(500);
    }
});

// Transaction history
app.MapGet("/transactions/history/{userId}", async (int userId, StreetPayDbContext db) =>
{
    var user = await db.Users.FindAsync(userId);
    if (user == null)
        return Results.NotFound(new { Message = "User not found" });

    var transactions = await db.Transactions
        .Where(t => t.SenderPhone == user.Phone || t.ReceiverPhone == user.Phone)
        .ToListAsync();

    var response = new List<object>();
    foreach (var txn in transactions)
    {
        var sender = await db.Users.FirstOrDefaultAsync(u => u.Phone == txn.SenderPhone);
        var receiver = await db.Users.FirstOrDefaultAsync(u => u.Phone == txn.ReceiverPhone);

        response.Add(new
        {
            transactionId = txn.TransactionId,
            senderId = sender?.Id ?? 0,
            senderPhone = txn.SenderPhone,
            senderName = sender?.Name ?? txn.SenderPhone,
            receiverId = receiver?.Id ?? 0,
            receiverPhone = txn.ReceiverPhone,
            receiverName = receiver?.Name ?? txn.ReceiverPhone,
            amount = txn.Amount,
            senderNewBalance = txn.SenderNewBalance, // Include new balance
            receiverNewBalance = txn.ReceiverNewBalance, // Include new balance
            currency = txn.Currency,
            timestamp = txn.Timestamp.ToString("o"),
            status = txn.Status,
            used = txn.IsSynced,
            type = txn.Type
        });
    }

    return Results.Ok(response);
});

app.Run();