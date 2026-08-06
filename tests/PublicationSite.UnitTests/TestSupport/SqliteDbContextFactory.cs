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
    private readonly List<ApplicationDbContext> _extra = [];

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

    /// <summary>
    /// Another context over the same database, which is what a second request is. Needed to test
    /// anything about two people acting at once: one context is one request, and two writes
    /// through the same one are the same person changing their mind.
    /// </summary>
    public ApplicationDbContext NewContext()
    {
        var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options);

        _extra.Add(context);
        return context;
    }

    public void Dispose()
    {
        foreach (var context in _extra) context.Dispose();
        Context.Dispose();
        _connection.Dispose();
    }
}
