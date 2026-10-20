using Microsoft.EntityFrameworkCore;
using OrderIngestion.Domain.Entities;

namespace OrderIngestion.Infrastructure.Persistence;

public class OrderDbContext : DbContext
{
    public OrderDbContext(
        DbContextOptions<OrderDbContext> options)
        : base(options)
    {
    }

    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(order => order.Id);

            entity.Property(order => order.ExternalId)
                .IsRequired();

            entity.HasIndex(order => order.ExternalId)
                .IsUnique();

            entity.Property(order => order.Title)
                .IsRequired();

            entity.Property(order => order.Body)
                .IsRequired();

            entity.Property(order => order.IngestedAt)
                .IsRequired()
                .HasConversion(
                    value => value,
                    value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        });
    }
}
