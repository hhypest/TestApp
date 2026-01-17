namespace TestApp.Domain.Handlers

open System.Threading
open System.Threading.Tasks
open TestApp.Core.Messages
open TestApp.Domain.Queries
open TestApp.Domain.Entities
open TestApp.Infrastructure.UnitOfWork

/// <summary>
/// Handler for GetAllTestsQuery
/// Retrieves all tests from the database
/// </summary>
type public GetAllTestsQueryHandler(unitOfWork: IUnitOfWork) =
    interface IQueryHandler<GetAllTestsQuery, seq<TestEntity>> with
        member _.Handle (query: GetAllTestsQuery) (token: CancellationToken) : Task<seq<TestEntity>> =
            task {
                try
                    // Retrieve all tests from repository
                    let! tests = unitOfWork.Tests.GetAllAsync(token)
                    return tests
                    
                with ex ->
                    // Log error and return empty sequence
                    System.Console.WriteLine($"Error retrieving tests: {ex.Message}")
                    return Seq.empty<TestEntity>
            }
