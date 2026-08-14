using TestApp.Application.Queries;

namespace TestApp.Infrastructure.Persistence;

public sealed partial class ReadModelQueries : IReadModelQueries
{
    private readonly AppDbContext _db;

    public ReadModelQueries(AppDbContext db) => _db = db;
}
