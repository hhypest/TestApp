namespace TestApp.Domain.Abstractions

open System.Threading
open System.Threading.Tasks

type public IUnitOfWork =
    interface
        abstract member SaveChangesAsync: token: CancellationToken -> Task
    end