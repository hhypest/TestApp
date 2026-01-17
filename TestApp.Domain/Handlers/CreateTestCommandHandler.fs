namespace TestApp.Domain.Handlers

open System
open System.Threading
open System.Threading.Tasks
open TestApp.Core.Messages
open TestApp.Domain.Commands
open TestApp.Domain.Entities

/// <summary>
/// Handler for CreateTestCommand
/// Demonstrates how to implement command handlers in CQRS pattern
/// </summary>
type public CreateTestCommandHandler() =
    interface ICommandHandler<CreateTestCommand, TestCommandResult> with
        member _.Handle (command: CreateTestCommand) (token: CancellationToken) : Task<TestCommandResult> =
            task {
                try
                    // Validate input
                    if String.IsNullOrWhiteSpace(command.TestTitle) then
                        return { Success = false
                                 Message = "Test title cannot be empty"
                                 EntityId = None }
                    else
                        // Create new test entity
                        let newTest = TestEntity()
                        newTest.TestId <- Guid.NewGuid()
                        newTest.TestTitle <- command.TestTitle
                        newTest.TestTime <- command.TestTime
                        newTest.AsksList <- Seq.empty
                        
                        // TODO: Save to repository
                        // await repository.AddAsync(newTest, token)
                        
                        return { Success = true
                                 Message = "Test created successfully"
                                 EntityId = Some newTest.TestId }
                with ex ->
                    return { Success = false
                             Message = $"Error creating test: {ex.Message}"
                             EntityId = None }
            }
