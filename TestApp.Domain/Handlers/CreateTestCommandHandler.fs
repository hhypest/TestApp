namespace TestApp.Domain.Handlers

open System
open System.Threading
open System.Threading.Tasks
open TestApp.Core.Messages
open TestApp.Domain.Commands
open TestApp.Domain.Entities
open TestApp.Infrastructure.UnitOfWork

/// <summary>
/// Handler for CreateTestCommand
/// Uses UnitOfWork for database operations
/// </summary>
type public CreateTestCommandHandler(unitOfWork: IUnitOfWork) =
    interface ICommandHandler<CreateTestCommand, TestCommandResult> with
        member _.Handle (command: CreateTestCommand) (token: CancellationToken) : Task<TestCommandResult> =
            task {
                try
                    // Validate input
                    if String.IsNullOrWhiteSpace(command.TestTitle) then
                        return { Success = false
                                 Message = "Test title cannot be empty"
                                 EntityId = None }
                    else if command.TestTitle.Length > 500 then
                        return { Success = false
                                 Message = "Test title cannot exceed 500 characters"
                                 EntityId = None }
                    else
                        // Create new test entity
                        let newTest = TestEntity()
                        newTest.TestId <- Guid.NewGuid()
                        newTest.TestTitle <- command.TestTitle
                        newTest.TestTime <- command.TestTime
                        newTest.AsksList <- Seq.empty
                        
                        // Save to repository
                        let! savedTest = unitOfWork.Tests.AddAsync(newTest, token)
                        let! _ = unitOfWork.SaveChangesAsync(token)
                        
                        return { Success = true
                                 Message = "Test created successfully"
                                 EntityId = Some savedTest.TestId }
                with ex ->
                    return { Success = false
                             Message = $"Error creating test: {ex.Message}"
                             EntityId = None }
            }
