using Microsoft.EntityFrameworkCore;
using Streetpay.API.Models;
using Streetpay.API.Models.DTOs;

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

app.MapGet("/", () => Results.Ok("🔥 StreetPay Offline API is live! 🔥"));

// Register a new user
app.MapPost("/auth/register", async (UserRegisterRequest req, StreetPayDbContext db) =>
{
    var existing = await db.Users.FirstOrDefaultAsync(u => u.Phone == req.Phone);
    if (existing != null)
        return Results.Conflict("User already exists");

    var newUser = new User
    {
        Name = req.Name,
        Phone = req.Phone,
        Pin = req.Pin // 🔐 TODO: hash in production
    };

    db.Users.Add(newUser);
    await db.SaveChangesAsync();

    return Results.Created($"/auth/profile/{newUser.Id}", newUser);
});

// Login user
app.MapPost("/auth/login", async (UserLoginRequest login, StreetPayDbContext db) =>
{
    var user = await db.Users.FirstOrDefaultAsync(u => u.Phone == login.Phone && u.Pin == login.Pin);
    if (user == null)
        return Results.Unauthorized();

    return Results.Ok(new
    {
        id = user.Id,
        name = user.Name,
        phone = user.Phone,
        token = "mock-jwt-token-123"
    });
});

// Profile
app.MapGet("/auth/profile/{id}", async (int id, StreetPayDbContext db) =>
{
    var user = await db.Users.FindAsync(id);
    if (user == null) return Results.NotFound();

    return Results.Ok(user);
});

// Wallet
app.MapGet("/wallet/{id}", async (int id, StreetPayDbContext db) =>
{
    var user = await db.Users.FindAsync(id);
    if (user == null) return Results.NotFound("User not found");

    var wallet = new WalletResponse
    {
        Main = user.MainBalance,
        Savings = user.SavingsBalance
    };

    return Results.Ok(wallet);
});

// Create new transaction
app.MapPost("/transactions", async (Transaction txn, StreetPayDbContext db) =>
{
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
    var user = await db.Users.FirstOrDefaultAsync(u => u.Phone == phone);
    if (user == null) return Results.NotFound("User not found");

    return Results.Ok(new { id = user.Id, name = user.Name });
});

// Send money online
app.MapPost("/transactions/send", async (OnlineTransactionDto dto, StreetPayDbContext db) =>
{
    var sender = await db.Users.FindAsync(dto.SenderId);
    var receiver = await db.Users.FindAsync(dto.ReceiverId);

    if (sender == null || receiver == null)
        return Results.BadRequest("Sender or receiver not found");

    if (dto.Amount <= 0)
        return Results.BadRequest("Invalid amount");

    if (sender.MainBalance < dto.Amount)
        return Results.BadRequest("Insufficient balance");

    // Deduct and credit
    sender.MainBalance -= dto.Amount;
    receiver.MainBalance += dto.Amount;

    var txn = new Transaction
    {
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
        transactionId = txn.Id,
        amount = dto.Amount,
        to = receiver.Name
    });
});

// Send transaction via offline
app.MapPost("/transactions/receive-offline", async (OfflineTransactionDto dto, StreetPayDbContext db) =>
{
    var sender = await db.Users.FirstOrDefaultAsync(u => u.Phone == dto.SenderPhone);
    var receiver = await db.Users.FirstOrDefaultAsync(u => u.Phone == dto.ReceiverPhone);

    if (sender == null || receiver == null)
        return Results.BadRequest("Invalid sender or receiver");

    if (dto.Amount <= 0)
        return Results.BadRequest("Invalid amount");

    var txn = new Transaction
    {
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
    foreach (var dto in txns)
    {
        var sender = await db.Users.FirstOrDefaultAsync(u => u.Phone == dto.SenderPhone);
        var receiver = await db.Users.FirstOrDefaultAsync(u => u.Phone == dto.ReceiverPhone);

        if (sender == null || receiver == null)
            continue;

        if (dto.Amount <= 0 || sender.MainBalance < dto.Amount)
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

        db.Transactions.Add(txn);
    }

    await db.SaveChangesAsync();
    return Results.Ok("Sync complete");
});

app.Run();