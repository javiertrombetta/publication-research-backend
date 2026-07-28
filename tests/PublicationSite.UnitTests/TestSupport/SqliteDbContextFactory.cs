using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Data;

namespace PublicationSite.UnitTests.TestSupport;

/// <summary>
/// Backs each test with a fresh SQLite in-memory database built directly from the EF Core
/// model (EnsureCreated), giving relational/FK-enforcing behaviour that the InMemory
/// provider doesn't, without needing a real MySQL server for unit tests.
/// </summary>
public sealed class SqliteDbContextFactory : IDisposable
{
    private readonly SqliteConnection _connection;

    public ApplicationDbContext Context { get; }

    public SqliteDbContextFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        Context = new ApplicationDbContext(options);
        Context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
