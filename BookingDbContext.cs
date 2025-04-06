using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Models;

namespace Telegram.Bot
{
    public class BookingDbContext : DbContext
    {
        public DbSet<Company> Companies { get; set; }
        public DbSet<Service> Services { get; set; }
        public DbSet<WorkingHours> WorkingHours { get; set; }
        public DbSet<Booking> Bookings { get; set; }
        public DbSet<Client> Clients { get; set; }
        public DbSet<Employee> Employees { get; set; }
        
        // New DbSet for Tokens
        public DbSet<Token> Tokens { get; set; }

        public BookingDbContext(DbContextOptions<BookingDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Token>()
                .HasIndex(t => t.TokenValue)
                .IsUnique();
            
            modelBuilder.Entity<Token>()
                .HasOne(t => t.Company)
                .WithOne(c => c.Token)
                .HasForeignKey<Company>(o => o.TokenId);
            
            modelBuilder.Entity<Company>()
                .HasMany(c => c.Employees)
                .WithOne(s => s.Company)
                .HasForeignKey(s => s.CompanyId);
        }
    }
}
