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
        public DbSet<ClientCompanyInvite> ClientCompanyInvites { get; set; }
        
        public DbSet<Token> Tokens { get; set; }
        public DbSet<Feedback> Feedbacks { get; set; }

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
                
            modelBuilder.Entity<ClientCompanyInvite>()
                .HasOne(cci => cci.Client)
                .WithMany(c => c.CompanyInvites)
                .HasForeignKey(cci => cci.ClientId);
                
            modelBuilder.Entity<ClientCompanyInvite>()
                .HasOne(cci => cci.Company)
                .WithMany(c => c.ClientInvites)
                .HasForeignKey(cci => cci.CompanyId);
            
            modelBuilder.Entity<Feedback>()
                .HasOne(f => f.Company)
                .WithMany(c => c.Feedbacks)
                .HasForeignKey(f => f.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Feedback>()
                .Property(f => f.Message)
                .IsRequired()
                .HasMaxLength(1000);
        }
    }
}
