using Microsoft.EntityFrameworkCore;
using Streetpay.API.Models;

namespace Streetpay.API
{
    public class StreetPayDbContext : DbContext
    {
        public StreetPayDbContext(DbContextOptions<StreetPayDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<Transaction> Transactions { get; set; }
        public DbSet<TransactionKey> TransactionKeys { get; set; }
        public DbSet<OfflineEscrow> OfflineEscrows { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Transaction>()
                .HasOne(t => t.Sender)
                .WithMany()
                .HasForeignKey(t => t.SenderId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Transaction>()
                .HasOne(t => t.Receiver)
                .WithMany()
                .HasForeignKey(t => t.ReceiverId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TransactionKey>()
                .HasKey(tk => tk.Id);

            modelBuilder.Entity<TransactionKey>()
                .HasOne(tk => tk.User)
                .WithMany()
                .HasForeignKey(tk => tk.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public class TransactionKey
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Key { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public User User { get; set; }
    }
}