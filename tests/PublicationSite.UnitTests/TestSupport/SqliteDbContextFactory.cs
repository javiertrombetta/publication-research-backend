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

    private DbContextOptions<ApplicationDbContext> Options { get; }
    private readonly List<ApplicationDbContext> _extra = [];

    public ApplicationDbContext Context { get; }

    public SqliteDbContextFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        Options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        Context = new ApplicationDbContext(Options);
        Context.Database.EnsureCreated();
    }

    /// <summary>
    /// The database as it now stands, rather than as this test last saw it.
    ///
    /// The service runs on its own context, so what it wrote is in the database and not in the one
    /// the test is holding. Read without this, an assertion gets the copy the test seeded and
    /// reports a change that did happen as one that did not; written without it, EF compares
    /// against a stale original, decides nothing changed and issues no UPDATE at all.
    /// </summary>
    public ApplicationDbContext Reread()
    {
        Context.ChangeTracker.Clear();

        // And the service's, because a real one gets a new context on every request and this one
        // lives for the whole test. Left holding what it read three calls ago, it compares against
        // a stale row and its next save is refused as a conflict: true of this fixture, and of
        // nothing that happens in front of a user.
        _service?.ChangeTracker.Clear();

        return Context;
    }

    /// <summary>
    /// The context the service under test runs on, which is deliberately not the one the test
    /// seeds with.
    ///
    /// Sharing one meant the service found everything the test had just created already in memory,
    /// and EF fills a navigation property in from what it is holding rather than from the query
    /// that was written. A service missing an Include passed every test and threw in front of a
    /// real request, which arrives on a context that knows nothing. Demonstrated: removing an
    /// Include from a service left its test green.
    ///
    /// One context is one request. Two of them over one connection is the database being shared
    /// and nothing else, which is the truth.
    ///
    /// It does not catch everything, and the limit is worth knowing. This context lives for the
    /// whole test method, so where a test makes two service calls the entities the first loaded are
    /// still held for the second, and EF will fix that navigation up as before. Reread() between
    /// them is what closes it, and is what a test making two calls should say anyway, since two
    /// calls are two requests. Clearing automatically on save was tried and is wrong: several
    /// services save more than once inside a single operation, so the line would fall in the middle
    /// of a request rather than at the end of one.
    /// </summary>
    public ApplicationDbContext ServiceContext => _service ??= new ApplicationDbContext(Options);

    private ApplicationDbContext? _service;


    /// <summary>
    /// Another context over the same database, which is what a second request is. Needed to test
    /// anything about two people acting at once: one context is one request, and two writes
    /// through the same one are the same person changing their mind.
    /// </summary>
    public ApplicationDbContext NewContext()
    {
        var context = new ApplicationDbContext(Options);

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
