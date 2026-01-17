namespace TestApp.Infrastructure.UnitOfWork

open System.Threading
open System.Threading.Tasks
open TestApp.Domain.Entities
open TestApp.Infrastructure.Repositories

/// <summary>
/// Unit of Work pattern for managing database transactions
/// </summary>
type public IUnitOfWork =
    interface
        /// Repository for tests
        abstract member Tests : IRepository<TestEntity>
        
        /// Repository for questions
        abstract member Asks : IRepository<AskEntity>
        
        /// Repository for answers
        abstract member Answers : IRepository<AnswerEntity>
        
        /// Save changes to database
        abstract member SaveChangesAsync : ?cancellationToken: CancellationToken -> Task<int>
        
        /// Dispose resources
        inherit System.IAsyncDisposable
    end
