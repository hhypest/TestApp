namespace TestApp.Infrastructure.UnitOfWork

open System
open System.Threading
open System.Threading.Tasks
open TestApp.Domain.Entities
open TestApp.Infrastructure.Data
open TestApp.Infrastructure.Repositories

/// <summary>
/// Unit of Work implementation coordinating multiple repositories
/// </summary>
type public UnitOfWork(context: ApplicationDbContext) =
    let mutable testRepo: IRepository<TestEntity> option = None
    let mutable askRepo: IRepository<AskEntity> option = None
    let mutable answerRepo: IRepository<AnswerEntity> option = None
    
    interface IUnitOfWork with
        member this.Tests =
            match testRepo with
            | Some repo -> repo
            | None -> 
                let repo = TestRepository(context) :> IRepository<TestEntity>
                testRepo <- Some repo
                repo
        
        member this.Asks =
            match askRepo with
            | Some repo -> repo
            | None -> 
                let repo = Repository<AskEntity>(context) :> IRepository<AskEntity>
                askRepo <- Some repo
                repo
        
        member this.Answers =
            match answerRepo with
            | Some repo -> repo
            | None -> 
                let repo = Repository<AnswerEntity>(context) :> IRepository<AnswerEntity>
                answerRepo <- Some repo
                repo
        
        member _.SaveChangesAsync(?cancellationToken: CancellationToken) : Task<int> =
            let token = defaultArg cancellationToken CancellationToken.None
            context.SaveChangesAsync(token)
        
        member _.DisposeAsync() : ValueTask =
            context.DisposeAsync()
