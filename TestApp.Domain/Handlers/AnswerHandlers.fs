namespace TestApp.Domain.Handlers

open System.Threading
open System.Threading.Tasks
open TestApp.Core.Types
open TestApp.Core.Monads
open TestApp.Core.Messages
open TestApp.Core.Extensions
open TestApp.Domain.Abstractions

type public GetAnswerHandler(repository: IAnswerRepository) =
    class
        interface IQueryHandler<GetAnswerQuery, Result<AnswerData, Error>> with
            member _.Handle(query: GetAnswerQuery)(token: CancellationToken) : Task<Result<AnswerData, Error>> = 
                task {
                    let! answer = (query.AnswerId, token) ||> repository.GetById
                    return
                        match answer with
                            | Success entity -> entity |> DataMapper.map |> Success
                            | Failure error -> error |> Failure
                }
    end

type public GetAllAnswersHandler(repository: IAnswerRepository) =
    class
        interface IQueryHandler<GetAnswersQuery, Result<seq<AnswerData>, Error>> with
            member _.Handle(query: GetAnswersQuery)(token: CancellationToken) : Task<Result<seq<AnswerData>, Error>> = 
                task {
                    let! answers = (query.AskId, token) ||> repository.GetAllAnswers
                    return
                        match answers with
                            | Success entities -> entities |> Seq.map DataMapper.map |> Success
                            | Failure error -> error |> Failure
                }
    end

type public CreateAnswerHandler(repository: IAnswerRepository) =
    class
        interface ICommandHandler<PostAnswerCommand, Result<int, Error>> with
            member _.Handle(command: PostAnswerCommand)(token: CancellationToken)  : Task<Result<int, Error>> = 
                task {
                    let answerEntity = command.Query |> DataMapper.map
                    return! (answerEntity, token) ||> repository.CreateAnswer
                }
    end

type public UpdateAnswerHandler(repository: IAnswerRepository) =
    class
        interface ICommandHandler<PutAnswerCommand, Result<int, Error>> with
            member _.Handle(command: PutAnswerCommand)(token: CancellationToken): Task<Result<int, Error>> = 
                task {
                    let answerEntity = command.Query |> DataMapper.mapBack
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