using FitnessApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Data;

public partial class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Checkin> Checkins { get; set; }
    public virtual DbSet<User> Customers { get; set; }
    public virtual DbSet<Customerpass> Customerpasses { get; set; }
    public virtual DbSet<Gympass> Gympasses { get; set; }
    public virtual DbSet<RefreshToken> RefreshToken { get; set; }
    public virtual DbSet<Trainer> Trainers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Checkin>(entity =>
        {
            entity.HasKey(e => e.Checkinid).HasName("checkins_pkey");

            entity.ToTable("checkins");

            entity.Property(e => e.Checkinid).HasColumnName("checkinid");

            entity.Property(e => e.Checkindatetime)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("checkindatetime");

            entity.Property(e => e.Customerpassid).HasColumnName("customerpassid");
            entity.Property(e => e.Trainerid).HasColumnName("trainerid");

            entity.HasOne(d => d.Customerpass)
                .WithMany(p => p.Checkins)
                .HasForeignKey(d => d.Customerpassid)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_checkins_customerpasses");

            entity.HasOne(d => d.Trainer)
                .WithMany(p => p.Checkins)
                .HasForeignKey(d => d.Trainerid)
                .HasConstraintName("fk_checkins_trainers");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("customers_pkey");

            entity.ToTable("customers");

            entity.HasIndex(e => e.Phone, "customers_phone_key")
                .IsUnique();

            entity.Property(e => e.Id).HasColumnName("customerid");
            entity.Property(e => e.Dateofbirth).HasColumnName("dateofbirth");

            entity.Property(e => e.Email)
                .HasMaxLength(100)
                .HasColumnName("email");

            entity.Property(e => e.Fullname)
                .HasMaxLength(100)
                .HasColumnName("fullname");

            entity.Property(e => e.Phone)
                .HasMaxLength(20)
                .HasColumnName("phone");
        });

        modelBuilder.Entity<Customerpass>(entity =>
        {
            entity.HasKey(e => e.Customerpassid)
                .HasName("customerpasses_pkey");

            entity.ToTable("customerpasses");

            entity.Property(e => e.Customerpassid).HasColumnName("customerpassid");
            entity.Property(e => e.Customerid).HasColumnName("customerid");
            entity.Property(e => e.Expirydate).HasColumnName("expirydate");
            entity.Property(e => e.Passtypeid).HasColumnName("passtypeid");

            entity.Property(e => e.Purchasedate)
                .HasDefaultValueSql("CURRENT_DATE")
                .HasColumnName("purchasedate");

            entity.Property(e => e.Remainingvisits).HasColumnName("remainingvisits");

            entity.HasOne(d => d.Customer)
                .WithMany(p => p.Customerpasses)
                .HasForeignKey(d => d.Customerid)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_customerpasses_customers");

            entity.HasOne(d => d.Passtype)
                .WithMany(p => p.Customerpasses)
                .HasForeignKey(d => d.Passtypeid)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_customerpasses_gympasses");
        });

        modelBuilder.Entity<Gympass>(entity =>
        {
            entity.HasKey(e => e.Passtypeid).HasName("gympasses_pkey");

            entity.ToTable("gympasses");

            entity.HasIndex(e => e.Passname, "gympasses_passname_key")
                .IsUnique();

            entity.Property(e => e.Passtypeid).HasColumnName("passtypeid");
            entity.Property(e => e.Maxvisits).HasColumnName("maxvisits");

            entity.Property(e => e.Passname)
                .HasMaxLength(50)
                .HasColumnName("passname");

            entity.Property(e => e.Price)
                .HasPrecision(6, 2)
                .HasColumnName("price");

            entity.Property(e => e.Validitydays).HasColumnName("validitydays");
        });

        modelBuilder.Entity<Trainer>(entity =>
        {
            entity.HasKey(e => e.Trainerid).HasName("trainers_pkey");

            entity.ToTable("trainers");

            entity.HasIndex(e => e.Phone, "trainers_phone_key")
                .IsUnique();

            entity.Property(e => e.Trainerid).HasColumnName("trainerid");

            entity.Property(e => e.Fullname)
                .HasMaxLength(100)
                .HasColumnName("fullname");

            entity.Property(e => e.Phone)
                .HasMaxLength(20)
                .HasColumnName("phone");

            entity.Property(e => e.Specialty)
                .HasMaxLength(50)
                .HasColumnName("specialty");
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("refreshtokens_pkey");

            entity.ToTable("refreshtokens");

            entity.Property(e => e.Id)
                .HasColumnName("id")
                .ValueGeneratedOnAdd();

            entity.Property(e => e.UserId)
                .HasColumnName("userid");

            entity.Property(e => e.Token)
                .HasMaxLength(256)
                .IsRequired()
                .HasColumnName("token");

            entity.HasIndex(e => e.Token)
                .IsUnique();

            entity.Property(e => e.ExpiresAt)
                .HasColumnName("expiresat")
                .HasColumnType("timestamp with time zone");

            entity.Property(e => e.CreatedAt)
                .HasColumnName("createdat")
                .HasColumnType("timestamp with time zone")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_refreshtokens_customers");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}