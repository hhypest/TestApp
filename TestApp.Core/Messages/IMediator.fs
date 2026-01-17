namespace TestApp.Core.Messages

open System.Threading
open System.Threading.Tasks

/// <summary>
/// Mediator interface for sending commands, queries and notifications
/// </summary>
type public IMediator =
    interface
        /// <summary>
        /// Send a command and get a response
        /// </summary>
        abstract member Send<'TCommand, 'TResponse when 'TCommand :> ICommand> : 
            command: 'TCommand -> 
            ?token: CancellationToken -> 
            Task<'TResponse>
        
        /// <summary>
        /// Send a query and get a response
        /// </summary>
        abstract member Query<'TQuery, 'TResponse when 'TQuery :> IQuery<'TResponse>> : 
            query: 'TQuery -> 
            ?token: CancellationToken -> 
            Task<'TResponse>
        
        /// <summary>
        /// Publish a notification to all handlers
        /// </summary>
        abstract member Publish<'TNotification when 'TNotification :> INotification> : 
            notification: 'TNotification -> 
            ?token: CancellationToken -> 
            Task
    end
