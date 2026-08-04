using PublicationSite.Api.Common;

namespace PublicationSite.Api.Services.Interfaces;

/// <summary>
/// Whether a given decision has to carry a comment, as this institution has decided.
///
/// The rule used to be written into each request's validator. It is a policy rather than a rule of
/// the software, and it belongs somewhere an administrator can reach: one deployment wants a
/// reason recorded against everything, another only where somebody has to act on it.
///
/// Enforced in the services, beside the decision itself, rather than in a validator. A validator
/// sees a request and not the decision it turns into: "accept" and "send back" arrive in the same
/// shape, and the two have different answers.
/// </summary>
public interface IDecisionCommentPolicy
{
    /// <summary>
    /// Refuses the decision when this institution requires a comment for it and none was written.
    /// Silent otherwise, whatever was or was not typed.
    /// </summary>
    /// <param name="decisionKey">One of <see cref="DecisionPoints"/>.</param>
    Task EnsureAsync(string decisionKey, string? comments, CancellationToken cancellationToken = default);

    /// <summary>Whether that decision requires one, for a screen that has to say so before asking.</summary>
    Task<bool> IsRequiredAsync(string decisionKey, CancellationToken cancellationToken = default);
}
