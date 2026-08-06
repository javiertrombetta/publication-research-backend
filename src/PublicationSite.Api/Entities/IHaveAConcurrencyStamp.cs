namespace PublicationSite.Api.Entities;

/// <summary>
/// A row two people can decide on at the same moment.
///
/// Every workflow decision in this system reads a record, satisfies itself that the step is still
/// open, and writes. Between the read and the write there is room for a second request to do
/// exactly the same, and both then write: a double click, a retried request, or two people acting
/// on the same publication in the same second. The guards cannot see each other.
///
/// The stamp closes that. It is part of the WHERE clause of every UPDATE, and
/// <see cref="Data.ApplicationDbContext"/> gives it a new value on each save, so a request whose
/// record has moved since it read it matches no rows and is told so. Whoever arrives second is
/// refused rather than quietly overwriting the first, which is the only outcome worth having: the
/// alternative is a decision that appears to have been taken and was not.
///
/// Only on the rows a decision actually writes. A stamp on reference data would cost every
/// administrator an argument with the database for nothing.
/// </summary>
public interface IHaveAConcurrencyStamp
{
    /// <summary>
    /// Changes on every save. Compared, never read by anything that means anything: what it is
    /// does not matter, only that it differs from what the other request saw.
    /// </summary>
    Guid ConcurrencyStamp { get; set; }
}
