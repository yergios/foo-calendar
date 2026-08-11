using Api.Data.Models;

using Microsoft.EntityFrameworkCore;

namespace Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Person> People => Set<Person>();
    public DbSet<Absence> Absences => Set<Absence>();
    public DbSet<BankHoliday> BankHolidays => Set<BankHoliday>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Person>()
            .HasIndex(p => p.Username)
            .IsUnique();

        modelBuilder.Entity<Person>()
            .HasIndex(p => p.Color)
            .IsUnique();

        modelBuilder.Entity<Absence>()
            .HasIndex(a => new { a.PersonId, a.StartDate, a.EndDate });

        modelBuilder.Entity<Person>()
            .Property(p => p.CreatedAt)
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        modelBuilder.Entity<Absence>()
            .Property(a => a.CreatedAt)
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        modelBuilder.Entity<BankHoliday>()
            .HasIndex(b => new { b.Region, b.Date })
            .IsUnique(); // Upsert target for the Nager bank-holiday sync.

        modelBuilder.Entity<BankHoliday>()
            .Property(b => b.CreatedAt)
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        base.OnModelCreating(modelBuilder);
    }
}
