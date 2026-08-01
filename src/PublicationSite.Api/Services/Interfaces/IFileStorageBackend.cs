namespace PublicationSite.Api.Services.Interfaces;

/// <summary>
/// One place uploaded bytes can live: a directory, a row in the database, a bucket.
///
/// Deliberately narrower than <see cref="IFileStorageService"/>. Everything that is true of every
/// upload wherever it ends up, which extensions are allowed and how large a file may be, is
/// decided once above this and never repeated here. A backend takes bytes and gives them back.
///
/// Which one new uploads go to is the administrator's choice and can change at any time, so a
/// backend also has to be able to say nothing about the ones already written elsewhere. That is
/// why <see cref="Name"/> exists: it is recorded against every stored file, and a file is always
/// read back from the backend that wrote it rather than from whichever is configured today.
/// </summary>
public interface IFileStorageBackend
{
    /// <summary>
    /// How this backend is named in settings and in a stored key. Short, lower case, and fixed
    /// forever: changing it would orphan every file already written under the old name.
    /// </summary>
    string Name { get; }

    /// <summary>Writes the stream and returns the path this backend will want back to read it.</summary>
    Task<string> WriteAsync(
        Stream content, string subFolder, string storedFileName, CancellationToken cancellationToken = default);

    Task<Stream> ReadAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Removes the file. Silent where it is already gone: deleting twice is not an error.</summary>
    Task DeleteAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tries the round trip, so an administrator finds out that a bucket name is wrong or a key
    /// has expired while they are on the settings screen rather than when a student uploads.
    /// </summary>
    Task CheckAsync(CancellationToken cancellationToken = default);
}
