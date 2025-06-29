namespace TestApp.Domain.Handlers

open System.Threading
open System.Threading.Tasks
open TestApp.Core.Types
open TestApp.Core.Monads
open TestApp.Core.Messages
open TestApp.Domain.Abstractions
open TestApp.Domain.Extensions

type public GetAnswerHandler(repository: IAnswerRepository) =
    class
        interface IQueryHandler<GetAnswerQuery, Result<AnswerData, Error>> with
            member _.Handle(query: GetAnswerQuery)(token: CancellationToken) : Task<Result<AnswerData, Error>> = 
                task {
                    return! (query.AnswerId, token) ||> repository.GetById
                }
    end

type public GetAllAnswersHandler(repository: IAnswerRepository) =
    class
        interface IQueryHandler<GetAnswersQuery, Result<seq<AnswerData>, Error>> with
            member _.Handle(query: GetAnswersQuery)(token: CancellationToken) : Task<Result<seq<AnswerData>, Error>> = 
                task {
                    return! (query.AskId, token) ||> repository.GetAllAnswers
                }
    end

type public CreateAnswerHandler(repository: IAnswerRepository) =
    class
        interface ICommandHandler<PostAnswerCommand, Result<int, Error>> with
            member _.Handle(command: PostAnswerCommand)(token: CancellationToken)  : Task<Result<int, Error>> = 
                task {
                    let answerEntity = command.Query.AnswerToEntity()
                    return! (answerEntity, token) ||> repository.CreateAnswer
                }
    end

type public UpdateAnswerHandler(repository: IAnswerRepository) =
    class
        interface ICommandHandler<PutAnswerCommand, Result<int, Error>> with
            member _.Handle(command: PutAnswerCommand)(token: CancellationToken): Task<Result<int, Error>> = 
                task {
                    let answerEntity = command.Query.AnswerToEntity()
                    return! (answerEntity, token) ||> repository.UpdateAnswer
                }
    end

type public DeleteAnswerHandler(repository: IAnswerRepository) =
    class
        interface ICommandHandler<DeleteAnswerCommand, Result<int, Error>> with
            member _.Handle(command: DeleteAnswerCommand)(token: CancellationToken) : Task<Result<int, Error>> = 
                task {
                    return! (command.AnswerId, token) ||> repository.DeleteAnswer
                }
    end

type public DeleteAllAnswersHandler(repository: IAnswerRepository) =
    class
        interface ICommandHandler<DeleteAllAnswersCommand, Result<int, Error>> with
            member _.Handle(command: DeleteAllAnswersCommand)(token: CancellationToken) : Task<Result<int, Error>> = 
                task {
                    return! (command.AskId, token) ||> repository.DeleteAllAnswers
                }
    end