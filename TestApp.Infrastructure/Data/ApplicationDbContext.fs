namespace TestApp.Infrastructure.Data

open System
open System.Collections.Generic
open Microsoft.EntityFrameworkCore
open TestApp.Domain.Entities

/// <summary>
/// Database context for TestApp using Entity Framework Core
/// Configured for MariaDB via Pomelo provider
/// </summary>
type public ApplicationDbContext(options: DbContextOptions<ApplicationDbContext>) =
    inherit DbContext(options)
    
    /// Tests DbSet
    member val Tests: DbSet<TestEntity> = Unchecked.defaultof<_> with get, set
    
    /// Questions DbSet
    member val Asks: DbSet<AskEntity> = Unchecked.defaultof<_> with get, set
    
    /// Answers DbSet
    member val Answers: DbSet<AnswerEntity> = Unchecked.defaultof<_> with get, set
    
    /// Configure the model
    override this.OnModelCreating(modelBuilder: ModelBuilder) : unit =
        base.OnModelCreating(modelBuilder)
        
        // Test entity configuration
        modelBuilder.Entity<TestEntity>()
            .HasKey(fun t -> t.TestId :> obj) |> ignore
        
        modelBuilder.Entity<TestEntity>()
            .Property(fun t -> t.TestId)
            .HasDefaultValueSql("UUID()")
            .IsRequired() |> ignore
        
        modelBuilder.Entity<TestEntity>()
            .Property(fun t -> t.TestTitle)
            .HasMaxLength(500)
            .IsRequired() |> ignore
        
        modelBuilder.Entity<TestEntity>()
            .Property(fun t -> t.TestTime)
            .IsRequired() |> ignore
        
        modelBuilder.Entity<TestEntity>()
            .HasMany(fun t -> (t.AsksList :> seq<AskEntity>))
            .WithOne()
            .HasForeignKey(fun a -> a.TestId)
            .OnDelete(DeleteBehavior.Cascade) |> ignore
        
        // Ask entity configuration
        modelBuilder.Entity<AskEntity>()
            .HasKey(fun a -> a.AskId :> obj) |> ignore
        
        modelBuilder.Entity<AskEntity>()
            .Property(fun a -> a.AskId)
            .HasDefaultValueSql("UUID()")
            .IsRequired() |> ignore
        
        modelBuilder.Entity<AskEntity>()
            .Property(fun a -> a.AskTitle)
            .HasMaxLength(1000)
            .IsRequired() |> ignore
        
        modelBuilder.Entity<AskEntity>()
            .Property(fun a -> a.IsSingle)
            .IsRequired() |> ignore
        
        modelBuilder.Entity<AskEntity>()
            .HasMany(fun a -> (a.AnswersList :> seq<AnswerEntity>))
            .WithOne()
            .HasForeignKey(fun ans -> ans.AskId)
            .OnDelete(DeleteBehavior.Cascade) |> ignore
        
        // Answer entity configuration
        modelBuilder.Entity<AnswerEntity>()
            .HasKey(fun ans -> ans.AnswerId :> obj) |> ignore
        
        modelBuilder.Entity<AnswerEntity>()
            .Property(fun ans -> ans.AnswerId)
            .HasDefaultValueSql("UUID()")
            .IsRequired() |> ignore
        
        modelBuilder.Entity<AnswerEntity>()
            .Property(fun ans -> ans.AnswerTitle)
            .HasMaxLength(1000)
            .IsRequired() |> ignore
        
        modelBuilder.Entity<AnswerEntity>()
            .Property(fun ans -> ans.IsCorrect)
            .IsRequired() |> ignore
        
        ()
