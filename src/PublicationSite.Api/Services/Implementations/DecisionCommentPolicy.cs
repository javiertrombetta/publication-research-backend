using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

/// <inheritdoc cref="IDecisionCommentPolicy"/>
public class DecisionCommentPolicy(ISystemSettingsProvider settings) : IDecisionCommentPolicy
{
    public async Task<bool> IsRequiredAsync(string decisionKey, CancellationToken cancellationToken = default)
    {
        var decision = DecisionPoints.Find(decisionKey);

        // A key nothing knows about is a programming error rather than a configuration one, and
        // the safe answer to "must this be explained" is yes.
        if (decision is null) return true;

        return await settings.GetBoolAsync(
            DecisionPoints.SettingKeyFor(decision.Key), decision.RequiredByDefault, cancellationToken);
    }

    public async Task EnsureAsync(string decisionKey, string? comments, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(comments)) return;
        if (!await IsRequiredAsync(decisionKey, cancellationToken)) return;

        var decision = DecisionPoints.Find(decisionKey);

        // Named, because a person meeting this refusal is looking at a screen with several
        // buttons on it and has to know which one asked.
        throw new BusinessRuleException(decision is null
            ? "This decision needs a comment."
            : $"This institution asks for a comment on this decision: {decision.Name}.");
    }
}
