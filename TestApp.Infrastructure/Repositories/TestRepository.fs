namespace TestApp.Infrastructure.Repositories

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.EntityFrameworkCore
open TestApp.Domain.Entities
open TestApp.Infrastructure.Data

/// <summary>
/// Test-specific repository with domain queries
/// </summary>
type public TestRepository(context: ApplicationDbContext) =
    inherit Repository<TestEntity>(context)
    
    /// Get test with all questions and answers
    member this.GetTestWithDetailsAsync(testId: Guid, ?cancellationToken: CancellationToken) : Task<TestEntity option> =
        task {
            let token = defaultArg cancellationToken CancellationToken.None
            let! test = 
                context.Tests
                    .Include(fun t -> (t.AsksList :> System.Collections.Generic.IEnumerable<_>))
                    .ThenInclude(fun a -> (a.AnswersList :> System.Collections.Generic.IEnumerable<_>))
                    .FirstOrDefaultAsync(fun t -> t.TestId = testId, token)
            return 
                if isNull (box test) then None
                else Some test
        }
    
    /// Search tests by title
    member this.SearchByTitleAsync(searchTerm: string, ?cancellationToken: CancellationToken) : Task<seq<TestEntity>> =
        task {
            let token = defaultArg cancellationToken CancellationToken.None
            let! tests = 
                context.Tests
                    .Where(fun t -> t.TestTitle.Contains(searchTerm))
                    .ToListAsync(token)
            return tests |> Seq.ofList
        }
    
    /// Get all tests with pagination
    member this.GetTestsPagedAsync(pageNumber: int, pageSize: int, ?cancellationToken: CancellationToken) : Task<seq<TestEntity> * int> =
        task {
            let token = defaultArg cancellationToken CancellationToken.None
            let! totalCount = context.Tests.CountAsync(token)
            let! tests = 
                context.Tests
                    .OrderByDescending(fun t -> t.TestId)
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync(token)
            return (tests |> Seq.ofList, totalCount)
        }
