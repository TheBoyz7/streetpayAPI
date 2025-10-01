using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Streetpay.API.Helpers;
using Streetpay.API.Interfaces;
using Streetpay.API.Models;
using Streetpay.API.Models.DTOs;
using Streetpay.API.Services;
using Streetpay.API.Services.Cryptography;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using BCrypt.Net;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Streetpay.API;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Register services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "StreetPay API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] { }
        }
    });
});
builder.Services.AddDbContext<StreetPayDbContext>(options =>
    options.UseSqlite("Data Source=streetpay.db;Pooling=false"));
builder.Services.Configure<EncryptionOptions>(builder.Configuration.GetSection("Encryption"));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddScoped<IAesCryptographyService, AesCryptographyService>();
builder.Services.AddScoped<IRsaCryptographyService, RsaCryptographyService>();
builder.Services.AddScoped<Encryption>();
builder.Services.AddScoped<KeyManagementService>();

// Add JWT authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
{
    var key = Encoding.ASCII.GetBytes(builder.Configuration["Jwt:Secret"] ?? throw new InvalidOperationException("JWT secret not configured."));
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});

// Add authorization services
builder.Services.AddAuthorization();

var app = builder.Build();

// Middleware
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new { Message = "StreetPay Offline API is live" }));

// Register a new user 
app.MapPost("/auth/register", async (UserRegisterRequest req, StreetPayDbContext db, Encryption encryption, KeyManagementService keyService) =>
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
        Pin = BCrypt.Net.BCrypt.HashPassword(req.Pin),
    };

    db.Users.Add(newUser);
    try
    {
        await db.SaveChangesAsync();

        // Generate initial transaction key
        var transactionKey = keyService.GenerateTransactionKey(newUser.Id);
        var payload = new 
        {
            newUser.Id,
            newUser.Name,
            newUser.Phone,
            TransactionKey = transactionKey
        };
        var encrypted = encryption.EncryptResponse(payload);
        return Results.Created($"/auth/profile/{newUser.Id}", encrypted);
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

// Login user 
app.MapPost("/auth/login", async (UserLoginRequest login, StreetPayDbContext db, Encryption encryption, KeyManagementService keyService) =>
{
    var validationResults = new List<ValidationResult>();
    if (!Validator.TryValidateObject(login, new ValidationContext(login), validationResults, true))
    {
        return Results.BadRequest(new { Message = "Invalid input", Errors = validationResults.Select(v => v.ErrorMessage) });
    }

    var user = await db.Users.FirstOrDefaultAsync(u => u.Phone == login.Phone);
    if (user == null || !BCrypt.Net.BCrypt.Verify(login.Pin, user.Pin))
        return Results.Unauthorized();

    var key = Encoding.ASCII.GetBytes(keyService.GetJwtSecret());
    var tokenDescriptor = new SecurityTokenDescriptor
    {
        Subject = new ClaimsIdentity(new[] { new Claim("id", user.Id.ToString()) }),
        Expires = DateTime.UtcNow.AddHours(1),
        SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
    };
    var tokenHandler = new JwtSecurityTokenHandler();
    var token = tokenHandler.CreateToken(tokenDescriptor);

    // Generate or retrieve transaction key
    var transactionKey = keyService.GenerateTransactionKey(user.Id);
    var payload = new
    {
        id = user.Id,
        name = user.Name,
        phone = user.Phone,
        token = tokenHandler.WriteToken(token),
        transactionKey
    };
    return Results.Ok(encryption.EncryptResponse(payload));
});

// Profile 
app.MapGet("/auth/profile/{id}", async (int id, StreetPayDbContext db, Encryption encryption) =>
{
    var user = await db.Users.FindAsync(id);
    if (user == null)
        return Results.NotFound(new { Message = "User not found" });

    var payload = new
    {
        user.Id,
        user.Name,
        user.Phone
    };
    var encrypted = encryption.EncryptResponse(payload);
    return Results.Ok(encrypted);
}).RequireAuthorization();

// Wallet
app.MapGet("/wallet/{id}", async (int id, StreetPayDbContext db, Encryption encryption) =>
{
    var user = await db.Users.FindAsync(id);
    if (user == null)
        return Results.NotFound(new { Message = "User not found" });

    var wallet = new WalletResponse
    {
        Main = user.MainBalance,
        Savings = user.SavingsBalance
    };
    var encrypted = encryption.EncryptResponse(wallet);
    return Results.Ok(encrypted);
}).RequireAuthorization();

// Update wallet balance 
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
}).RequireAuthorization();

// Create new transaction 
app.MapPost("/transactions", async (Transaction txn, StreetPayDbContext db, Encryption encryption) =>
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
        var encrypted = encryption.EncryptResponse(txn);
        return Results.Created($"/transactions/{txn.Id}", encrypted);
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
}).RequireAuthorization();

// Get all transactions 
app.MapGet("/transactions", async (StreetPayDbContext db, Encryption encryption) =>
{
    var all = await db.Transactions.ToListAsync();
    var encrypted = encryption.EncryptResponse(all);
    return Results.Ok(encrypted);
}).RequireAuthorization();

// Pending transactions 
app.MapGet("/transactions/pending", async (StreetPayDbContext db, Encryption encryption) =>
{
    var pending = await db.Transactions
        .Where(t => !t.IsSynced)
        .ToListAsync();
    var encrypted = encryption.EncryptResponse(pending);
    return Results.Ok(encrypted);
}).RequireAuthorization();

// Get user by phone number 
app.MapGet("/users/by-phone/{phone}", async (
    string phone,
    StreetPayDbContext db,
    Encryption encryption,
    KeyManagementService keyService) =>
{
    if (string.IsNullOrWhiteSpace(phone))
        return Results.BadRequest(new { Message = "Phone number is required" });

    var user = await db.Users.FirstOrDefaultAsync(u => u.Phone == phone);
    if (user == null)
        return Results.NotFound(new { Message = "User not found" });

    string? publicKey = null;
    try
    {
        publicKey = keyService.GetPublicKeyForUser(user.Id);
    }
    catch (KeyNotFoundException)
    {
        publicKey = null;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[KeyService Error] {ex.Message}");
        publicKey = null;
    }

    var payload = new
    {
        id = user.Id,
        name = user.Name,
        publicKey
    };

    var encrypted = encryption.EncryptResponse(payload);
    return Results.Ok(encrypted);
}).RequireAuthorization();

// Get transaction key
app.MapGet("/keys/transaction", async (HttpContext context, StreetPayDbContext db, Encryption encryption, KeyManagementService keyService) =>
{
    var userIdClaim = context.User.FindFirst("id")?.Value;
    if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
        return Results.Unauthorized();

    var user = await db.Users.FindAsync(userId);
    if (user == null)
        return Results.NotFound(new { Message = "User not found" });

    try
    {
        var transactionKey = keyService.GenerateTransactionKey(userId);
        var payload = new { key = transactionKey };
        var encrypted = encryption.EncryptResponse(payload);
        return Results.Ok(encrypted);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error generating transaction key: {ex.Message}");
        return Results.StatusCode(500);
    }
}).RequireAuthorization();

// Get transaction key by senderId
app.MapGet("/keys/transaction/{senderId}", async (int senderId, StreetPayDbContext db, Encryption encryption, KeyManagementService keyService) =>
{
    var user = await db.Users.FindAsync(senderId);
    if (user == null)
        return Results.NotFound(new { Message = "User not found" });

    try
    {
        var transactionKey = keyService.GetTransactionKey(senderId);
        var payload = new { key = transactionKey };
        var encrypted = encryption.EncryptResponse(payload);
        return Results.Ok(encrypted);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error fetching transaction key for sender {senderId}: {ex.Message}");
        return Results.StatusCode(500);
    }
}).RequireAuthorization();

// Send money online
app.MapPost("/transactions/send", async (OnlineTransactionDto dto, StreetPayDbContext db, Encryption encryption) =>
{
    try
    {
        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(dto, new ValidationContext(dto), validationResults, true))
        {
            Console.WriteLine($"Validation failed: {string.Join(", ", validationResults.Select(v => v.ErrorMessage))}");
            return Results.BadRequest(new { Message = "Invalid transaction data", Errors = validationResults.Select(v => v.ErrorMessage) });
        }

        Console.WriteLine($"Received OnlineTransactionDto: {System.Text.Json.JsonSerializer.Serialize(dto)}");

        var sender = await db.Users.FindAsync(dto.SenderId);
        if (sender == null)
        {
            Console.WriteLine($"Sender not found: SenderId={dto.SenderId}");
            return Results.BadRequest(new { Message = "Sender not found" });
        }

        var receiver = await db.Users.FindAsync(dto.ReceiverId);
        if (receiver == null)
        {
            Console.WriteLine($"Receiver not found: ReceiverId={dto.ReceiverId}");
            return Results.BadRequest(new { Message = "Receiver not found" });
        }

        if (dto.Amount <= 0)
        {
            Console.WriteLine($"Invalid amount: Amount={dto.Amount}");
            return Results.BadRequest(new { Message = "Invalid amount" });
        }

        if (sender.MainBalance < dto.Amount)
        {
            Console.WriteLine($"Insufficient balance: SenderId={dto.SenderId}, MainBalance={sender.MainBalance}, Amount={dto.Amount}");
            return Results.BadRequest(new { Message = "Insufficient balance" });
        }

        if (await db.Transactions.AnyAsync(t => t.TransactionId == dto.TransactionId))
        {
            Console.WriteLine($"Transaction already exists: TransactionId={dto.TransactionId}");
            return Results.Conflict(new { Message = "Transaction already exists" });
        }

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

            var payload = new
            {
                message = "Transaction successful",
                transactionId = txn.TransactionId,
                amount = txn.Amount,
                to = receiver.Name,
                senderNewBalance = txn.SenderNewBalance,
                receiverNewBalance = txn.ReceiverNewBalance
            };
            Console.WriteLine($"Sending response payload: {System.Text.Json.JsonSerializer.Serialize(payload)}");
            var encrypted = encryption.EncryptResponse(payload);
            return Results.Ok(encrypted);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            Console.WriteLine($"Transaction error: {ex.Message}, StackTrace: {ex.StackTrace}");
            return Results.StatusCode(500);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Unexpected error in /transactions/send: {ex.Message}, StackTrace: {ex.StackTrace}");
        return Results.StatusCode(500);
    }
}).RequireAuthorization();

// Receive offline
app.MapPost("/transactions/receive-offline", async (OfflineTransactionDto dto, StreetPayDbContext db, Encryption encryption, KeyManagementService keyService) =>
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
    try
    {
        var transactionKey = keyService.GetTransactionKey(sender.Id);
        var signingPayload = new Dictionary<string, object>
        {
            { "amount", dto.Amount },
            { "currency", dto.Currency },
            { "isSync", false },
            { "nonce", dto.Nonce },
            { "receiverPhone", dto.ReceiverPhone },
            { "senderNewBalance", dto.SenderNewBalance ?? sender.MainBalance },
            { "senderPhone", dto.SenderPhone },
            { "senderId", sender.Id },
            { "status", "pending" },
            { "timestamp", dto.Timestamp.ToString("o") },
            { "transactionId", dto.TransactionId },
            { "type", "offline" },
            { "used", false }
        };

        // Sort keys alphabetically (same as frontend)
        var sortedPayload = signingPayload.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value);
        
        var payloadString = System.Text.Json.JsonSerializer.Serialize(sortedPayload, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = false
        });
        
        Console.WriteLine($"Backend signing payload for {dto.TransactionId}: {payloadString}");
        Console.WriteLine($"Backend transaction key: {transactionKey}");
        
        var expectedSignature = Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData(
                Encoding.UTF8.GetBytes(payloadString + transactionKey)
            )
        );
        
        Console.WriteLine($"Backend expected signature: {expectedSignature}");
        Console.WriteLine($"Backend received signature: {dto.Signature}");

        if (dto.Signature != expectedSignature)
        {
            Console.WriteLine($"Invalid signature for transaction {dto.TransactionId}");
            return Results.BadRequest(new { Message = "Invalid transaction signature" });
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error verifying signature: {ex.Message}");
        return Results.BadRequest(new { Message = "Signature verification failed" });
    }

    var txn = new Transaction
    {
        TransactionId = dto.TransactionId,
        SenderId = sender.Id,
        ReceiverId = receiver.Id,
        SenderPhone = dto.SenderPhone,
        ReceiverPhone = dto.ReceiverPhone,
        Amount = dto.Amount,
        SenderNewBalance = dto.SenderNewBalance ?? sender.MainBalance,
        ReceiverNewBalance = dto.ReceiverNewBalance ?? receiver.MainBalance + dto.Amount,
        Currency = dto.Currency,
        Status = "pending",
        IsSynced = false,
        Timestamp = dto.Timestamp,
        Type = "offline",
        Used = true,
        Signature = dto.Signature
    };

    db.Transactions.Add(txn);
    try
    {
        await db.SaveChangesAsync();
        var payload = new
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
        };
        var encrypted = encryption.EncryptResponse(payload);
        return Results.Created($"/transactions/{txn.Id}", encrypted);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error during offline transaction: {ex.Message}");
        return Results.StatusCode(500);
    }
}).RequireAuthorization();

// transactions/sync endpoint 
app.MapPost("/transactions/sync", async (List<OfflineTransactionDto> txns, StreetPayDbContext db, Encryption encryption, KeyManagementService keyService) =>
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
            try
            {
                var transactionKey = keyService.GetTransactionKey(sender.Id);
                
                // Create signing payload with EXACT same structure as frontend
                var signingPayload = new Dictionary<string, object>
                {
                    { "amount", dto.Amount },
                    { "currency", dto.Currency },
                    { "isSync", false },
                    { "nonce", dto.Nonce },
                    { "receiverPhone", dto.ReceiverPhone },
                    { "senderNewBalance", dto.SenderNewBalance ?? sender.MainBalance },
                    { "senderPhone", dto.SenderPhone },
                    { "senderId", sender.Id },
                    { "status", "pending" },
                    { "timestamp", dto.Timestamp.ToString("o") },
                    { "transactionId", dto.TransactionId },
                    { "type", "offline" },
                    { "used", false }
                };

                // Sort keys alphabetically (same as frontend)
                var sortedPayload = signingPayload.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value);
                
                var payloadString = System.Text.Json.JsonSerializer.Serialize(sortedPayload, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = false
                });
                
                Console.WriteLine($"Backend sync signing payload for {dto.TransactionId}: {payloadString}");
                Console.WriteLine($"Backend sync transaction key: {transactionKey}");
                
                var expectedSignature = Convert.ToBase64String(
                    System.Security.Cryptography.SHA256.HashData(
                        Encoding.UTF8.GetBytes(payloadString + transactionKey)
                    )
                );
                
                Console.WriteLine($"Backend sync expected signature: {expectedSignature}");
                Console.WriteLine($"Backend sync received signature: {dto.Signature}");

                if (dto.Signature != expectedSignature)
                {
                    Console.WriteLine($"Invalid signature for transaction {dto.TransactionId}");
                    results.Add(new { TransactionId = dto.TransactionId, Status = "Failed", Message = "Invalid transaction signature" });
                    continue;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error verifying signature: {ex.Message}");
                results.Add(new { TransactionId = dto.TransactionId, Status = "Failed", Message = "Signature verification failed" });
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
                    Currency = dto.Currency,
                    Status = "synced",
                    IsSynced = true,
                    Timestamp = dto.Timestamp,
                    Type = "offline",
                    Used = true,
                    Signature = dto.Signature
                };
                db.Transactions.Add(txn);
            }

            sender.MainBalance -= dto.Amount;
            receiver.MainBalance += dto.Amount;

            results.Add(new { TransactionId = dto.TransactionId, Status = "Success", Message = "Transaction synced", SenderNewBalance = sender.MainBalance, ReceiverNewBalance = receiver.MainBalance });
        }

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        var encrypted = encryption.EncryptResponse(results);
        return Results.Ok(new { Message = "Sync complete", Results = encrypted });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        Console.WriteLine($"Error during sync: {ex.Message}");
        return Results.StatusCode(500);
    }
}).RequireAuthorization();


// Transaction history
app.MapGet("/transactions/history/{userId}", async (int userId, StreetPayDbContext db, Encryption encryption) =>
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
            senderNewBalance = txn.SenderNewBalance,
            receiverNewBalance = txn.ReceiverNewBalance,
            currency = txn.Currency,
            timestamp = txn.Timestamp.ToString("o"),
            status = txn.Status,
            used = txn.IsSynced,
            type = txn.Type
        });
    }
    var encrypted = encryption.EncryptResponse(response);
    return Results.Ok(encrypted);
}).RequireAuthorization();

app.Run(); 