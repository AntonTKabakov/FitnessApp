
using FitnessApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Data;

public partial class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    // Database tables
    public virtual DbSet<User> Users => Set<User>();
    public virtual DbSet<Checkin> Checkins => Set<Checkin>();
    public virtual DbSet<Customerpass> Customerpasses => Set<Customerpass>();
    public virtual DbSet<Gympass> Gympasses => Set<Gympass>();
    public virtual DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public virtual DbSet<Trainer> Trainers => Set<Trainer>();

    // Optional alias for existing code using Customers
    public virtual DbSet<User> Customers => Users;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // =============================
        // USERS / CUSTOMERS
        // =============================

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("customers");

            entity.HasKey(e => e.Id)
                .HasName("customers_pkey");

            entity.Property(e => e.Id)
                .HasColumnName("customerid")
                .ValueGeneratedOnAdd();

            entity.Property(e => e.Fullname)
                .HasColumnName("fullname")
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(e => e.Email)
                .HasColumnName("email")
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(e => e.Password)
                .HasColumnName("password")
                .IsRequired();

            entity.Property(e => e.Dateofbirth)
                .HasColumnName("dateofbirth");

            entity.HasIndex(e => e.Email)
                .IsUnique();

            entity.HasMany(e => e.RefreshTokens)
                .WithOne(e => e.User)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_refreshtokens_customers");
        });

        // =============================
        // CUSTOMER PASSES
        // =============================

        modelBuilder.Entity<Customerpass>(entity =>
        {
            entity.ToTable("customerpasses");

            entity.HasKey(e => e.Customerpassid)
                .HasName("customerpasses_pkey");

            entity.Property(e => e.Customerpassid)
                .HasColumnName("customerpassid");

            entity.Property(e => e.Customerid)
                .HasColumnName("customerid");

            entity.Property(e => e.Passtypeid)
                .HasColumnName("passtypeid");

            entity.Property(e => e.Purchasedate)
                .HasColumnName("purchasedate")
                .HasDefaultValueSql("CURRENT_DATE");

            entity.Property(e => e.Expirydate)
                .HasColumnName("expirydate");

            entity.Property(e => e.Remainingvisits)
                .HasColumnName("remainingvisits");

            entity.HasOne(e => e.Customer)
                .WithMany(e => e.Customerpasses)
                .HasForeignKey(e => e.Customerid)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_customerpasses_customers");

            entity.HasOne(e => e.Passtype)
                .WithMany(e => e.Customerpasses)
                .HasForeignKey(e => e.Passtypeid)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_customerpasses_gympasses");
        });

        // =============================
        // GYM PASSES
        // =============================

        modelBuilder.Entity<Gympass>(entity =>
        {
            entity.ToTable("gympasses");

            entity.HasKey(e => e.Passtypeid)
                .HasName("gympasses_pkey");

            entity.Property(e => e.Passtypeid)
                .HasColumnName("passtypeid");

            entity.Property(e => e.Passname)
                .HasColumnName("passname")
                .HasMaxLength(50);

            entity.Property(e => e.Price)
                .HasColumnName("price")
                .HasPrecision(6, 2);

            entity.Property(e => e.Maxvisits)
                .HasColumnName("maxvisits");

            entity.Property(e => e.Validitydays)
                .HasColumnName("validitydays");

            entity.HasIndex(e => e.Passname, "gympasses_passname_key")
                .IsUnique();
        });

        // =============================
        // CHECKINS
        // =============================

        modelBuilder.Entity<Checkin>(entity =>
        {
            entity.ToTable("checkins");

            entity.HasKey(e => e.Checkinid)
                .HasName("checkins_pkey");

            entity.Property(e => e.Checkinid)
                .HasColumnName("checkinid");

            entity.Property(e => e.Customerpassid)
                .HasColumnName("customerpassid");

            entity.Property(e => e.Trainerid)
                .HasColumnName("trainerid");

            entity.Property(e => e.Checkindatetime)
                .HasColumnName("checkindatetime")
                .HasColumnType("timestamp without time zone")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(e => e.Customerpass)
                .WithMany(e => e.Checkins)
                .HasForeignKey(e => e.Customerpassid)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_checkins_customerpasses");

            entity.HasOne(e => e.Trainer)
                .WithMany(e => e.Checkins)
                .HasForeignKey(e => e.Trainerid)
                .HasConstraintName("fk_checkins_trainers");
        });

        // =============================
        // TRAINERS
        // =============================

        modelBuilder.Entity<Trainer>(entity =>
        {
            entity.ToTable("trainers");

            entity.HasKey(e => e.Trainerid)
                .HasName("trainers_pkey");

            entity.Property(e => e.Trainerid)
                .HasColumnName("trainerid");

            entity.Property(e => e.Fullname)
                .HasColumnName("fullname")
                .HasMaxLength(100);

            entity.Property(e => e.Phone)
                .HasColumnName("phone")
                .HasMaxLength(20);

            entity.Property(e => e.Specialty)
                .HasColumnName("specialty")
                .HasMaxLength(50);

            entity.HasIndex(e => e.Phone, "trainers_phone_key")
                .IsUnique();
        });

        // =============================
        // REFRESH TOKENS
        // =============================

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("refreshtokens");

            entity.HasKey(e => e.Id)
                .HasName("refreshtokens_pkey");

            entity.Property(e => e.Id)
                .HasColumnName("id")
                .ValueGeneratedOnAdd();

            entity.Property(e => e.UserId)
                .HasColumnName("userid");

            entity.Property(e => e.Token)
                .HasColumnName("token")
                .HasMaxLength(256)
                .IsRequired();

            entity.Property(e => e.ExpiresAt)
                .HasColumnName("expiresat")
                .HasColumnType("timestamp with time zone");

            entity.Property(e => e.CreatedAt)
                .HasColumnName("createdat")
                .HasColumnType("timestamp with time zone")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.Token)
                .IsUnique();

            // Relationship configured in User mapping
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
