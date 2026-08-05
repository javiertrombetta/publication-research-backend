using PublicationSite.Api.Enums;

namespace PublicationSite.Api.Common;

/// <summary>
/// When a publication's research paper has been accepted, and what to say to whoever tries to
/// change the material behind it afterwards.
///
/// An administrator's corrections exist to unstick work that is still moving: a document uploaded
/// to the wrong slot, a version that will not open, proposals nobody can act on. Once the paper is
/// accepted none of that applies. What is on the publication then is the record of what was
/// judged, and rearranging it does not correct anything, it makes the record disagree with the
/// decision that was taken on it.
///
/// Named in one place because four different acts have to refuse on the same grounds, and four
/// copies of the same condition is how they drift apart.
/// </summary>
public static class SettledPaper
{
    /// <summary>
    /// Accepted, and published, which is accepted and then made public. A paper still under review
    /// or sent back for revisions is not settled: that is exactly the work an administrator is
    /// sometimes asked to unstick.
    /// </summary>
    public static bool Is(PublicationStatus? status) =>
        status is PublicationStatus.Accepted or PublicationStatus.Published;

    public const string Message =
        "This publication's research paper has been accepted. What it holds is the record of what "
        + "was judged, so it cannot be changed from here.";
}
