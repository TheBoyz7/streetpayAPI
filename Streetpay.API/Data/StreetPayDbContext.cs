using Microsoft.EntityFrameworkCore;
using Streetpay.API.Models;

public class StreetPayDbContext : DbContext
{
    public StreetPayDbContext(DbContextOptions<StreetPayDbContext> options) : base(options) { }

    
    public DbSet<User> Users { get; set; }
    public DbSet<Transaction> Transactions { get; set; }
}
