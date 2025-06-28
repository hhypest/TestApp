namespace TestApp.Core.Messages

open System.Threading
open System.Threading.Tasks
open TestApp.Core.Messages

type public INotificationHandler<'TNotification when 'TNotification :> INotification> =
    interface
        abstract member Handle : notification: 'TNotification -> token: CancellationToken -> Task
    end

type public ICommandHandler<'TCommand, 'TResponse when 'TCommand :> ICommand> =
    interface
        abstract member Handle : command: 'TCommand -> token: CancellationToken -> Task<'TResponse>
    end

type public IQueryHandler<'TQuery, 'TResponse when 'TQuery :> IQuery<'TResponse>> =
    interface
        abstract member Handle : query: 'TQuery -> token: CancellationToken -> Task<'TResponse>
    end