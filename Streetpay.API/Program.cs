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

//profile for now
app.MapGet("/auth/profile/{id}", async (int id, StreetPayDbContext db) =>
{
    var user = await db.Users.FindAsync(id);
    if (user == null) return Results.NotFound();

    return Results.Ok(user);
});



//wallet for now
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

app.Run();
