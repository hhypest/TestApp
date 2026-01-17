namespace TestApp.Infrastructure.Repositories

open System
open System.Linq.Expressions
open System.Threading
open System.Threading.Tasks
open TestApp.Domain.Entities

/// <summary>
/// Generic repository interface following Repository pattern
/// </summary>
type public IRepository<'TEntity when 'TEntity :> Entity> =
    interface
        abstract member GetByIdAsync : id: Guid * ?cancellationToken: CancellationToken -> Task<'TEntity option>
        abstract member GetAllAsync : ?cancellationToken: CancellationToken -> Task<seq<'TEntity>>
        abstract member AddAsync : entity: 'TEntity * ?cancellationToken: CancellationToken -> Task<'TEntity>
        abstract member Update : entity: 'TEntity -> unit
        abstract member Remove : entity: 'TEntity -> unit
    end
