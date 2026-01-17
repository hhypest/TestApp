namespace TestApp.Core.Messages

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection

/// <summary>
/// Basic implementation of IMediator that resolves handlers from DI container
/// This is a simplified version - consider using MediatR library for production
/// </summary>
type public Mediator(serviceProvider: IServiceProvider) =
    interface IMediator with
        member _.Send<'TCommand, 'TResponse when 'TCommand :> ICommand> 
            (command: 'TCommand) 
            (token: CancellationToken option) : Task<'TResponse> =
            task {
                let cancellationToken = defaultArg token CancellationToken.None
                
                // Resolve handler from DI container
                let handlerType = typedefof<ICommandHandler<_, _>>.MakeGenericType(typeof<'TCommand>, typeof<'TResponse>)
                let handler = serviceProvider.GetService(handlerType)
                
                match handler with
                | null -> 
                    raise (InvalidOperationException($"No handler registered for command {typeof<'TCommand>.Name}"))
                | h ->
                    let handlerInterface = h :?> ICommandHandler<'TCommand, 'TResponse>
                    return! handlerInterface.Handle command cancellationToken
            }
        
        member _.Query<'TQuery, 'TResponse when 'TQuery :> IQuery<'TResponse>> 
            (query: 'TQuery) 
            (token: CancellationToken option) : Task<'TResponse> =
            task {
                let cancellationToken = defaultArg token CancellationToken.None
                
                // Resolve handler from DI container
                let handlerType = typedefof<IQueryHandler<_, _>>.MakeGenericType(typeof<'TQuery>, typeof<'TResponse>)
                let handler = serviceProvider.GetService(handlerType)
                
                match handler with
                | null -> 
                    raise (InvalidOperationException($"No handler registered for query {typeof<'TQuery>.Name}"))
                | h ->
                    let handlerInterface = h :?> IQueryHandler<'TQuery, 'TResponse>
                    return! handlerInterface.Handle query cancellationToken
            }
        
        member _.Publish<'TNotification when 'TNotification :> INotification> 
            (notification: 'TNotification) 
            (token: CancellationToken option) : Task =
            task {
                let cancellationToken = defaultArg token CancellationToken.None
                
                // Resolve all handlers from DI container
                let handlerType = typedefof<INotificationHandler<_>>.MakeGenericType(typeof<'TNotification>)
                let handlers = serviceProvider.GetServices(handlerType)
                
                // Execute all handlers in parallel
                let tasks = 
                    handlers 
                    |> Seq.cast<INotificationHandler<'TNotification>>
                    |> Seq.map (fun h -> h.Handle notification cancellationToken)
                    |> Seq.toArray
                
                do! Task.WhenAll(tasks)
            }
