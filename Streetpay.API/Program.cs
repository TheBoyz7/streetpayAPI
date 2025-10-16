using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Streetpay.API;
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
app.MapPost("/auth/register", async (UserRegisterRequest req, StreetPayDbContext db, KeyManagementService keyService, IOptions<EncryptionOptions> encryptionOptions) =>
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

        // --- FIX: Added token generation to log user in immediately ---
        var key = Encoding.ASCII.GetBytes(keyService.GetJwtSecret());
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[] { new Claim("id", newUser.Id.ToString()) }),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        // --- End of Fix ---

        var transactionKey = keyService.GenerateTransactionKey(newUser.Id);
        var payload = new 
        {
            id = newUser.Id,
            name = newUser.Name,
            phone = newUser.Phone,
            token = tokenHandler.WriteToken(token), 
            transactionKey = transactionKey,
            encryptionKey = encryptionOptions.Value.AesKey 
        };
        
        // Return the payload directly without encrypting it
        return Results.Created($"/auth/profile/{newUser.Id}", payload);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error registering user: {ex.Message}");
        return Results.StatusCode(500);
    }
});

// Login user 
app.MapPost("/auth/login", async (UserLoginRequest login, StreetPayDbContext db, KeyManagementService keyService, IOptions<EncryptionOptions> encryptionOptions) =>
{
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

    var transactionKey = keyService.GenerateTransactionKey(user.Id);
    var payload = new
    {
        id = user.Id,
        name = user.Name,
        phone = user.Phone,
        token = tokenHandler.WriteToken(token),
        transactionKey,
        encryptionKey = encryptionOptions.Value.AesKey
    };
    
    // Return the payload directly without encrypting it
    return Results.Ok(payload);
});

//Profile
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

    var wallet = new 
    {
        Main = user.MainBalance,
        Savings = user.SavingsBalance,
        Offline = user.OfflineBalance // <-- Add this line
    };
    var encrypted = encryption.EncryptResponse(wallet);
    return Results.Ok(encrypted);
}).RequireAuthorization();

// Update wallet balance 
// ADD THIS NEW ENDPOINT
app.MapPost("/wallet/to-offline", async (OfflineTopUpDto dto, StreetPayDbContext db, Encryption encryption, HttpContext context) =>
{
    var userIdClaim = context.User.FindFirst("id")?.Value;
    if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var authUserId) || authUserId != dto.UserId)
    {
        return Results.Unauthorized();
    }
    
    using var dbTransaction = await db.Database.BeginTransactionAsync();
    try
    {
        var user = await db.Users.FindAsync(dto.UserId);
        if (user == null) return Results.NotFound(new { Message = "User not found" });
        if (user.MainBalance < dto.Amount) return Results.BadRequest(new { Message = "Insufficient main balance" });

        user.MainBalance -= dto.Amount;
        user.OfflineBalance += dto.Amount;

        await db.SaveChangesAsync();
        await dbTransaction.CommitAsync();

        var payload = new 
        {
            success = true,
            message = $"₦{dto.Amount} moved to Offline Wallet.",
            main = user.MainBalance,
            offline = user.OfflineBalance
        };
        var encrypted = encryption.EncryptResponse(payload);
        return Results.Ok(encrypted);
    }
    catch (Exception ex)
    {
        await dbTransaction.RollbackAsync();
        Console.WriteLine($"Error during offline top-up: {ex.Message}");
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

    if (await db.Transactions.AnyAsync(t => t.TransactionId == txn.TransactionId || t.Nonce == txn.Nonce))
        return Results.Conflict(new { Message = "Transaction or nonce already exists" });

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

// Sync device-generated keys
app.MapPost("/keys/sync", async (KeySyncDto dto, StreetPayDbContext db, Encryption encryption, KeyManagementService keyService, HttpContext context) =>
{
    var validationResults = new List<ValidationResult>();
    if (!Validator.TryValidateObject(dto, new ValidationContext(dto), validationResults, true))
    {
        Console.WriteLine($"Validation failed for key sync: {string.Join(", ", validationResults.Select(v => v.ErrorMessage))}");
        return Results.BadRequest(new { Message = "Invalid key data", Errors = validationResults.Select(v => v.ErrorMessage) });
    }

    var userIdClaim = context.User.FindFirst("id")?.Value;
    if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var authUserId) || authUserId != dto.userId)
    {
        Console.WriteLine($"Unauthorized key sync attempt: AuthUserId={userIdClaim}, ProvidedUserId={dto.userId}");
        return Results.Unauthorized();
    }

    var user = await db.Users.FindAsync(dto.userId);
    if (user == null)
    {
        Console.WriteLine($"User not found: UserId={dto.userId}");
        return Results.NotFound(new { Message = "User not found" });
    }

    try
    {
        // Store the key using KeyManagementService
        keyService.StoreTransactionKey(dto.userId, dto.key, DateTimeOffset.FromUnixTimeMilliseconds(dto.created).UtcDateTime);
        var payload = new { success = true, message = "Key synced successfully" };
        var encrypted = encryption.EncryptResponse(payload);
        return Results.Ok(encrypted);
    }
    catch (InvalidOperationException ex)
    {
        Console.WriteLine($"Key sync failed: {ex.Message}");
        return Results.Conflict(new { Message = ex.Message });
    }
    catch (SqliteException ex) when (ex.SqliteErrorCode == 5)
    {
        Console.WriteLine($"Database locked during key sync: {ex.Message}");
        return Results.StatusCode(503);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error syncing key: {ex.Message}");
        return Results.StatusCode(500);
    }
}).RequireAuthorization();

// Send money online
app.MapPost("/transactions/send", async (OnlineTransactionDto dto, StreetPayDbContext db, Encryption encryption, KeyManagementService keyService) =>
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

        if (await db.Transactions.AnyAsync(t => t.TransactionId == dto.TransactionId || t.Nonce == dto.Nonce))
        {
            Console.WriteLine($"Transaction or nonce already exists: TransactionId={dto.TransactionId}, Nonce={dto.Nonce}");
            return Results.Conflict(new { Message = "Transaction or nonce already exists" });
        }

        // START: ADDED VERIFICATION BLOCK
        try
        {
            var secretKey = keyService.GetTransactionKey(sender.Id);
            var transactionDetails = new
            {
                transactionId = dto.TransactionId,
                timestamp = dto.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                senderPhone = sender.Phone,
                receiverPhone = receiver.Phone
            };

            var message = System.Text.Json.JsonSerializer.Serialize(transactionDetails);
            using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(secretKey));
            var computedHash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(message));
            var serverSignature = Convert.ToBase64String(computedHash);

            if (serverSignature != dto.Signature)
            {
                Console.WriteLine($"Signature mismatch for TransactionId={dto.TransactionId}");
                return Results.Unauthorized(); // REJECT
            }
        }
        catch (KeyNotFoundException)
        {
            Console.WriteLine($"Transaction key not found for SenderId={dto.SenderId}");
            return Results.Unauthorized(); // REJECT
        }
        // END: ADDED VERIFICATION BLOCK

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
                Timestamp = dto.Timestamp, // use client-signed timestamp, not DateTime.UtcNow
                Type = "online",
                Used = true,
                Signature = dto.Signature ?? string.Empty,
                Nonce = dto.Nonce
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
app.MapPost("/transactions/receive-offline", async (OfflineTransactionDto dto, StreetPayDbContext db, Encryption encryption) =>
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

    if (await db.Transactions.AnyAsync(t => t.TransactionId == dto.TransactionId || t.Nonce == dto.Nonce))
        return Results.Conflict(new { Message = "Transaction or nonce already exists" });

    if (DateTime.UtcNow - dto.Timestamp > TimeSpan.FromDays(7))
        return Results.BadRequest(new { Message = "Transaction expired" });

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
        Signature = dto.Signature ?? string.Empty,
        Nonce = dto.Nonce
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
            txn.Signature,
            txn.Nonce
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

// transactions sync endpoint 
app.MapPost("/transactions/sync", async (List<OfflineTransactionDto> txns, StreetPayDbContext db, Encryption encryption, KeyManagementService keyService) =>
{
    var results = new List<object>();
    using var transaction = await db.Database.BeginTransactionAsync();
    try
    {
        foreach (var dto in txns)
        {
            var sender = await db.Users.FirstOrDefaultAsync(u => u.Phone == dto.SenderPhone);
            var receiver = await db.Users.FirstOrDefaultAsync(u => u.Phone == dto.ReceiverPhone);
            if (sender == null || receiver == null) { results.Add(new { TransactionId = dto.TransactionId, Status = "Failed", Message = "Sender or receiver not found" }); continue; }
            if (await db.Transactions.AnyAsync(t => t.TransactionId == dto.TransactionId && t.Nonce == dto.Nonce)) { results.Add(new { TransactionId = dto.TransactionId, Status = "Skipped", Message = "Transaction already synced." }); continue; }
            if (sender.OfflineBalance < dto.Amount) { results.Add(new { TransactionId = dto.TransactionId, Status = "Failed", Message = "Insufficient offline balance." }); continue; }
            try
            {
                var secretKey = keyService.GetTransactionKey(sender.Id);
                var canonicalString = $"{dto.TransactionId}{dto.Timestamp:yyyy-MM-ddTHH:mm:ss.fffZ}{dto.SenderPhone}{dto.ReceiverPhone}";
                using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
                var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonicalString));
                var serverSignature = Convert.ToBase64String(computedHash);
                if (serverSignature != dto.Signature) { results.Add(new { TransactionId = dto.TransactionId, Status = "Failed", Message = "Invalid signature" }); continue; }
            }
            catch (KeyNotFoundException) { results.Add(new { TransactionId = dto.TransactionId, Status = "Failed", Message = "Sender's transaction key not found" }); continue; }
            sender.OfflineBalance -= dto.Amount;
            receiver.OfflineBalance += dto.Amount;
            var newTxn = new Transaction { TransactionId = dto.TransactionId, SenderId = sender.Id, ReceiverId = receiver.Id, SenderPhone = dto.SenderPhone, ReceiverPhone = dto.ReceiverPhone, Amount = dto.Amount, SenderNewBalance = sender.MainBalance, ReceiverNewBalance = receiver.MainBalance, Currency = "NGN", Status = "synced", IsSynced = true, Timestamp = dto.Timestamp, Type = "offline", Used = true, Signature = dto.Signature ?? string.Empty, Nonce = dto.Nonce };
            db.Transactions.Add(newTxn);
            results.Add(new { TransactionId = dto.TransactionId, Status = "Success", Message = "Transaction synced" });
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Results.Ok(encryption.EncryptResponse(new { success = true, results }));
    }
    catch (Exception) { await transaction.RollbackAsync(); return Results.StatusCode(500); }
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
            type = txn.Type,
            nonce = txn.Nonce
        });
    }
    var encrypted = encryption.EncryptResponse(response);
    return Results.Ok(encrypted);
}).RequireAuthorization();

app.Run();

public class OfflineEscrow
{
    [Key]
    public int Id { get; set; }
    [Required]
    public required string TransactionId { get; set; }
    [Required]
    public int SenderId { get; set; }
    [Required]
    public decimal Amount { get; set; }
    [Required]
    public string Status { get; set; } = "Active"; // Active, Settled, Expired
    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [Required]
    public DateTime ExpiresAt { get; set; }
}

public record OfflineCommitDto
{
    [Required]
    public int SenderId { get; init; }
    [Required]
    public required string TransactionId { get; init; }
    [Required, Range(0.01, double.MaxValue)]
    public decimal Amount { get; init; }
}

public record KeySyncDto
{
    [Required]
    public int userId { get; init; }
    [Required]
    public string key { get; init; } = string.Empty;
    [Required]
    public long created { get; init; }
}

public record OfflineTopUpDto
{
    [Required]
    public int UserId { get; init; }
    [Required, Range(0.01, double.MaxValue)]
    public decimal Amount { get; init; }
}
