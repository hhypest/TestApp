namespace TestApp.Infrastructure.Repositories

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.EntityFrameworkCore
open TestApp.Domain.Entities
open TestApp.Infrastructure.Data

/// <summary>
/// Generic repository implementation using Entity Framework Core
/// </summary>
type public Repository<'TEntity when 'TEntity :> Entity and 'TEntity : not struct> 
    (context: ApplicationDbContext) =
    
    interface IRepository<'TEntity> with
        member _.GetByIdAsync(id: Guid, ?cancellationToken: CancellationToken) : Task<'TEntity option> = 
            task {
                let token = defaultArg cancellationToken CancellationToken.None
                let! entity = context.Set<'TEntity>().FindAsync([| box id |], cancellationToken = token).AsTask()
                return 
                    if isNull (box entity) then None
                    else Some entity
            }
        
        member _.GetAllAsync(?cancellationToken: CancellationToken) : Task<seq<'TEntity>> = 
            task {
                let token = defaultArg cancellationToken CancellationToken.None
                let! entities = context.Set<'TEntity>().ToListAsync(token)
                return entities |> Seq.ofList
            }
        
        member _.AddAsync(entity: 'TEntity, ?cancellationToken: CancellationToken) : Task<'TEntity> = 
            task {
                let token = defaultArg cancellationToken CancellationToken.None
                let entityEntry = context.Set<'TEntity>().Add(entity)
                let! _ = context.SaveChangesAsync(token)
                return entityEntry.Entity
            }
        
        member _.Update(entity: 'TEntity) : unit =
            context.Set<'TEntity>().Update(entity) |> ignore
        
        member _.Remove(entity: 'TEntity) : unit =
            context.Set<'TEntity>().Remove(entity) |> ignore
