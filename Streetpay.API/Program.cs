using Microsoft.EntityFrameworkCore;
using Streetpay.API.Models;
using Streetpay.API.Models.DTOs;
using System.ComponentModel.DataAnnotations;

var builder = WebApplication.CreateBuilder(args);

// Register services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<StreetPayDbContext>(options =>
    options.UseSqlite("Data Source=streetpay.db"));

var app = builder.Build();

// Middleware
app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => Results.Ok(new { Message = "StreetPay Offline API is live" }));

// Register a new user
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
    await db.SaveChangesAsync();

    return Results.Created($"/auth/profile/{newUser.Id}", new
    {
        newUser.Id,
        newUser.Name,
        newUser.Phone
    });
});

// Login user
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

// Profile
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

// Wallet
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

// Update wallet balance
app.MapPut("/wallet/{id}", async (int id, WalletResponse wallet, StreetPayDbContext db) =>
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
    await db.SaveChangesAsync();

    return Results.Ok(new { Message = "Wallet updated", Main = user.MainBalance, Savings = user.SavingsBalance });
});

// Create new transaction
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
    await db.SaveChangesAsync();

    return Results.Created($"/transactions/{txn.Id}", txn);
});

// Get all transactions
app.MapGet("/transactions", async (StreetPayDbContext db) =>
{
    var all = await db.Transactions.ToListAsync();
    return Results.Ok(all);
});

// Pending transactions
app.MapGet("/transactions/pending", async (StreetPayDbContext db) =>
{
    var pending = await db.Transactions
        .Where(t => !t.IsSynced)
        .ToListAsync();

    return Results.Ok(pending);
});

// Get user by phone number
app.MapGet("/users/by-phone/{phone}", async (string phone, StreetPayDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(phone))
        return Results.BadRequest(new { Message = "Phone number is required" });

    var user = await db.Users.FirstOrDefaultAsync(u => u.Phone == phone);
    if (user == null)
        return Results.NotFound(new { Message = "User not found" });

    return Results.Ok(new { id = user.Id, name = user.Name });
});

// Send money online
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

    sender.MainBalance -= dto.Amount;
    receiver.MainBalance += dto.Amount;

    var txn = new Transaction
    {
        TransactionId = dto.TransactionId,
        SenderPhone = sender.Phone,
        ReceiverPhone = receiver.Phone,
        Amount = dto.Amount,
        Status = "sent",
        IsSynced = true,
        Timestamp = DateTime.UtcNow
    };

    db.Transactions.Add(txn);
    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        message = "Transaction successful",
        transactionId = txn.TransactionId,
        amount = txn.Amount,
        to = receiver.Name
    });
});

// Send transaction via offline
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

    if (await db.Transactions.AnyAsync(t => t.TransactionId == dto.TransactionId))
        return Results.Conflict(new { Message = "Transaction already exists" });

    var txn = new Transaction
    {
        TransactionId = dto.TransactionId,
        SenderPhone = dto.SenderPhone,
        ReceiverPhone = dto.ReceiverPhone,
        Amount = dto.Amount,
        Status = "pending",
        IsSynced = false,
        Timestamp = dto.Timestamp
    };

    db.Transactions.Add(txn);
    await db.SaveChangesAsync();

    return Results.Created($"/transactions/{txn.Id}", txn);
});

// Sync pending transactions
app.MapPost("/transactions/sync", async (List<OfflineTransactionDto> txns, StreetPayDbContext db) =>
{
    var validationResults = new List<ValidationResult>();
    foreach (var dto in txns)
    {
        if (!Validator.TryValidateObject(dto, new ValidationContext(dto), validationResults, true))
        {
            return Results.BadRequest(new { Message = "Invalid transaction data", Errors = validationResults.Select(v => v.ErrorMessage) });
        }
    }

    foreach (var dto in txns)
    {
        var sender = await db.Users.FirstOrDefaultAsync(u => u.Phone == dto.SenderPhone);
        var receiver = await db.Users.FirstOrDefaultAsync(u => u.Phone == dto.ReceiverPhone);

        if (sender == null || receiver == null)
            continue;

        if (dto.Amount <= 0 || sender.MainBalance < dto.Amount)
            continue;

        if (await db.Transactions.AnyAsync(t => t.TransactionId == dto.TransactionId))
            continue;

        sender.MainBalance -= dto.Amount;
        receiver.MainBalance += dto.Amount;

        var txn = new Transaction
        {
            TransactionId = dto.TransactionId,
            SenderPhone = dto.SenderPhone,
            ReceiverPhone = dto.ReceiverPhone,
            Amount = dto.Amount,
            Status = "synced",
            IsSynced = true,
            Timestamp = dto.Timestamp
        };

        db.Transactions.Add(txn);
    }

    await db.SaveChangesAsync();
    return Results.Ok(new { Message = "Sync complete" });
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
            currency = "NGN",
            timestamp = txn.Timestamp.ToString("o"),
            status = txn.Status,
            used = txn.IsSynced
        });
    }

    return Results.Ok(response);
});

app.Run();