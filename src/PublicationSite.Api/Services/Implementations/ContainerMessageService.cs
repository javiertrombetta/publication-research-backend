using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Data;
using PublicationSite.Api.DTOs.Common;
using PublicationSite.Api.DTOs.Messages;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

public class ContainerMessageService(
    ApplicationDbContext db,
    IContainerAccessService accessService,
    IFileStorageService fileStorageService,
    INotificationService notificationService,
    IAuditService auditService,
    ISystemSettingsProvider settings) : IContainerMessageService
{
    public async Task<ContainerMessagingDto> GetMessagingAsync(
        Guid publicationContainerId, Guid userId, CancellationToken cancellationToken = default)
    {
        await accessService.EnsureAccessAsync(publicationContainerId, userId);

        return new ContainerMessagingDto(
            await MessagingEnabledAsync(cancellationToken),
            await CounterpartsAsync(publicationContainerId, userId, cancellationToken),
            await AllowedExtensionsTextAsync(cancellationToken));
    }

    public async Task<PagedResult<ContainerMessageDto>> GetMessagesAsync(
        Guid publicationContainerId,
        Guid userId,
        Guid? withUserId,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        await accessService.EnsureAccessAsync(publicationContainerId, userId);

        // Mine, not this publication's. Access to the publication is what lets somebody read the
        // proposals, the ethics file and the paper; it is not what lets them read a conversation
        // they are not in.
        var query = db.ContainerMessages
            .Where(m => m.PublicationContainerId == publicationContainerId
                        && (m.SenderUserId == userId || m.RecipientUserId == userId));

        if (withUserId is { } other)
        {
            query = query.Where(m => m.SenderUserId == other || m.RecipientUserId == other);
        }

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(m => m.SentAt)
            .Skip((page.SafePage - 1) * page.SafePageSize)
            .Take(page.SafePageSize)
            .Select(m => new ContainerMessageDto(
                m.Id,
                m.SenderUserId,
                m.SenderUser.FirstName + " " + m.SenderUser.LastName,
                m.RecipientUserId,
                m.RecipientUser.FirstName + " " + m.RecipientUser.LastName,
                m.Body,
                m.SentAt,
                m.SenderUserId == userId,
                m.ReadAt != null,
                m.Attachments
                    .OrderBy(a => a.FileName)
                    .Select(a => new MessageAttachmentDto(a.Id, a.FileName, a.SizeInBytes))
                    .ToList()))
            .ToListAsync(cancellationToken);

        return new PagedResult<ContainerMessageDto>(items, page.SafePage, page.SafePageSize, total);
    }

    public async Task<ContainerMessageDto> SendAsync(
        Guid publicationContainerId,
        Guid senderUserId,
        SendContainerMessageRequest request,
        IReadOnlyList<(Stream Content, string FileName)> attachments,
        CancellationToken cancellationToken = default)
    {
        await accessService.EnsureAccessAsync(publicationContainerId, senderUserId);

        if (!await MessagingEnabledAsync(cancellationToken))
        {
            throw new BusinessRuleException(
                "Writing to each other through a publication has been switched off for this institution.");
        }

        var body = (request.Body ?? string.Empty).Trim();
        if (body.Length == 0)
        {
            throw new BusinessRuleException("Write something before sending it.");
        }

        if (body.Length > SettingKeys.MessageMaximumLength)
        {
            throw new BusinessRuleException(
                $"A message can be up to {SettingKeys.MessageMaximumLength} characters. Yours is {body.Length}.");
        }

        if (attachments.Count > SettingKeys.MessageMaximumAttachments)
        {
            throw new BusinessRuleException(
                $"A message can carry up to {SettingKeys.MessageMaximumAttachments} files. Send the rest in another one.");
        }

        // Who may be written to is one definition, asked here as well as on the screen. A list the
        // screen draws is a suggestion; this is the rule.
        var counterparts = await CounterpartsAsync(publicationContainerId, senderUserId, cancellationToken);
        if (counterparts.All(c => c.UserId != request.RecipientUserId))
        {
            throw new ForbiddenException("That is not somebody you can write to about this publication.");
        }

        var message = new ContainerMessage
        {
            PublicationContainerId = publicationContainerId,
            SenderUserId = senderUserId,
            RecipientUserId = request.RecipientUserId,
            Body = body
        };

        var permitted = await AllowedExtensionsAsync(cancellationToken);
        foreach (var (content, fileName) in attachments)
        {
            var stored = await fileStorageService.SaveAsync(
                content, fileName, $"messages/{publicationContainerId}", permitted, cancellationToken);

            message.Attachments.Add(new ContainerMessageAttachment
            {
                FileName = stored.FileName,
                FilePath = stored.RelativePath,
                // Read after the copy, since that is when a stream that could not say its length
                // has one. Zero from a stream that still will not say is honest: it is unknown.
                SizeInBytes = content.CanSeek ? content.Length : 0
            });
        }

        db.ContainerMessages.Add(message);
        await db.SaveChangesAsync(cancellationToken);

        var sender = await db.Users
            .Where(u => u.Id == senderUserId)
            .Select(u => u.FirstName + " " + u.LastName)
            .SingleAsync(cancellationToken);

        // The notification says who wrote and that there is something to read, never what it said.
        // Notifications are emailed when that is switched on, and an email leaves the site: a
        // message somebody wrote inside it should not turn up in full in a mailbox.
        await notificationService.NotifyAsync(
            request.RecipientUserId,
            NotificationType.MessageReceived,
            $"{sender} wrote to you about a publication",
            "Open the publication to read it and reply.",
            "ContainerMessages",
            publicationContainerId,
            cancellationToken);

        if (await settings.GetBoolAsync(SettingKeys.MessagingRecordedInActivityHistory,
                SettingKeys.DefaultMessagingRecordedInActivityHistory, cancellationToken))
        {
            var recipient = counterparts.First(c => c.UserId == request.RecipientUserId);

            // The fact, not the contents. The activity history is read by everybody with access to
            // the publication, and what two people said to each other is not theirs.
            await auditService.LogActivityAsync(
                publicationContainerId,
                senderUserId,
                "Message sent",
                $"Wrote to {recipient.Name} ({recipient.Role}) through the site."
                + (message.Attachments.Count > 0
                    ? $" {message.Attachments.Count} file{(message.Attachments.Count == 1 ? "" : "s")} attached."
                    : string.Empty));
        }

        var recipientName = counterparts.First(c => c.UserId == request.RecipientUserId).Name;

        return new ContainerMessageDto(
            message.Id, senderUserId, sender, request.RecipientUserId, recipientName,
            message.Body, message.SentAt, true, false,
            message.Attachments.Select(a => new MessageAttachmentDto(a.Id, a.FileName, a.SizeInBytes)).ToList());
    }

    public async Task<int> MarkReadAsync(
        Guid publicationContainerId, Guid userId, Guid withUserId, CancellationToken cancellationToken = default)
    {
        await accessService.EnsureAccessAsync(publicationContainerId, userId);

        var now = DateTime.UtcNow;

        return await db.ContainerMessages
            .Where(m => m.PublicationContainerId == publicationContainerId
                        && m.RecipientUserId == userId
                        && m.SenderUserId == withUserId
                        && m.ReadAt == null)
            .ExecuteUpdateAsync(m => m.SetProperty(x => x.ReadAt, now), cancellationToken);
    }

    public async Task<(Stream Content, string FileName)> OpenAttachmentAsync(
        Guid publicationContainerId,
        Guid userId,
        Guid attachmentId,
        CancellationToken cancellationToken = default)
    {
        await accessService.EnsureAccessAsync(publicationContainerId, userId);

        // Both halves of the condition matter. The container narrows it to this publication, and
        // the sender-or-recipient test is what stops anybody else with access to the publication
        // opening a file out of a conversation they are not in.
        var attachment = await db.ContainerMessageAttachments
            .Where(a => a.Id == attachmentId
                        && a.ContainerMessage.PublicationContainerId == publicationContainerId
                        && (a.ContainerMessage.SenderUserId == userId || a.ContainerMessage.RecipientUserId == userId))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(ContainerMessageAttachment), attachmentId);

        var content = await fileStorageService.OpenReadAsync(attachment.FilePath, cancellationToken);
        return (content, attachment.FileName);
    }

    public async Task<int> GetUnreadCountAsync(
        Guid publicationContainerId, Guid userId, CancellationToken cancellationToken = default) =>
        await db.ContainerMessages.CountAsync(
            m => m.PublicationContainerId == publicationContainerId
                 && m.RecipientUserId == userId
                 && m.ReadAt == null,
            cancellationToken);

    /// <summary>
    /// Who this person may write to about this publication.
    ///
    /// Two rules, because there are two kinds of person here.
    ///
    /// The student writes to the people responsible for their publication: the supervisor they were
    /// assigned, the coordinator running it, and the head of their department. Not to the whole
    /// institution, and not to a committee member who is judging their paper.
    ///
    /// Everybody else on the publication writes to the student. That is what they are here for. The
    /// Staff role is left out: it is the placeholder an institutional address holds before an
    /// administrator says what the person actually is, so there is nobody there yet to write.
    ///
    /// And to both, anybody who has already written to them here. Without that, a message from
    /// somebody outside the first rule would arrive with no way to answer it, which is worse than
    /// not being able to write to them in the first place.
    /// </summary>
    private async Task<IReadOnlyList<MessageCounterpartDto>> CounterpartsAsync(
        Guid publicationContainerId, Guid userId, CancellationToken cancellationToken)
    {
        var container = await db.PublicationContainers
            .AsNoTracking()
            .Where(c => c.Id == publicationContainerId)
            .Select(c => new
            {
                c.StudentId,
                StudentName = c.Student.FirstName + " " + c.Student.LastName,
                c.CoordinatorId,
                CoordinatorName = c.Coordinator.FirstName + " " + c.Coordinator.LastName,
                c.AssignedSupervisorId,
                SupervisorName = c.AssignedSupervisor != null
                    ? c.AssignedSupervisor.FirstName + " " + c.AssignedSupervisor.LastName
                    : null,
                DepartmentId = c.Student.StudentProfile != null ? c.Student.StudentProfile.DepartmentId : (Guid?)null
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(PublicationContainer), publicationContainerId);

        var found = new Dictionary<Guid, MessageCounterpartDto>();

        void Add(Guid? id, string? name, string role)
        {
            if (id is { } value && value != userId && !found.ContainsKey(value) && !string.IsNullOrWhiteSpace(name))
            {
                found[value] = new MessageCounterpartDto(value, name, role, 0);
            }
        }

        if (container.StudentId == userId)
        {
            Add(container.AssignedSupervisorId, container.SupervisorName, "Supervisor");
            Add(container.CoordinatorId, container.CoordinatorName, "Coordinator");

            if (container.DepartmentId is { } departmentId)
            {
                var heads = await db.HeadOfDepartmentProfiles
                    .Where(h => h.DepartmentId == departmentId)
                    .Select(h => new { h.UserId, Name = h.User.FirstName + " " + h.User.LastName })
                    .ToListAsync(cancellationToken);

                foreach (var head in heads)
                {
                    Add(head.UserId, head.Name, "Head of Department");
                }
            }
        }
        else if (await HoldsAnOperationalRoleAsync(userId, cancellationToken))
        {
            Add(container.StudentId, container.StudentName, "Student");
        }

        // Anybody already in a conversation here, whichever rule brought them in.
        //
        // Two queries rather than one. Picking "the other person" with a conditional inside the
        // projection reads well and does not translate, so the ids are collected first and the
        // people looked up by them. Their role is read rather than assumed, so a reviewer who
        // wrote to a student is named as one.
        var otherIds = await db.ContainerMessages
            .Where(m => m.PublicationContainerId == publicationContainerId
                        && (m.SenderUserId == userId || m.RecipientUserId == userId))
            .Select(m => m.SenderUserId == userId ? m.RecipientUserId : m.SenderUserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (otherIds.Count > 0)
        {
            var alreadyWriting = await db.Users
                .Where(u => otherIds.Contains(u.Id))
                .Select(u => new
                {
                    u.Id,
                    Name = u.FirstName + " " + u.LastName,
                    Role = db.UserRoles
                        .Where(ur => ur.UserId == u.Id)
                        .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
                        .OrderBy(name => name == RoleNames.Staff ? 1 : 0)
                        .FirstOrDefault()
                })
                .ToListAsync(cancellationToken);

            foreach (var person in alreadyWriting)
            {
                Add(person.Id, person.Name, Readable(person.Role));
            }
        }

        // Counted in one query rather than per person: this list is short, but a query per row is
        // how a short list becomes a slow screen.
        var unread = await db.ContainerMessages
            .Where(m => m.PublicationContainerId == publicationContainerId
                        && m.RecipientUserId == userId
                        && m.ReadAt == null)
            .GroupBy(m => m.SenderUserId)
            .Select(g => new { SenderId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        foreach (var row in unread)
        {
            if (found.TryGetValue(row.SenderId, out var counterpart))
            {
                found[row.SenderId] = counterpart with { UnreadFromThem = row.Count };
            }
        }

        return found.Values
            .OrderByDescending(c => c.UnreadFromThem)
            .ThenBy(c => c.Name)
            .ToList();
    }

    /// <summary>
    /// Whether this person has a job here at all. The Staff role does not count: it is what an
    /// institutional address holds before an administrator says what the person is.
    /// </summary>
    private Task<bool> HoldsAnOperationalRoleAsync(Guid userId, CancellationToken cancellationToken) =>
        db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
            .AnyAsync(name => name != null && RoleNames.Operational.Contains(name), cancellationToken);

    /// <summary>Role names as somebody would say them out loud.</summary>
    private static string Readable(string? role) => role switch
    {
        RoleNames.HeadOfDepartment => "Head of Department",
        RoleNames.ExternalCommitteeMember => "External committee member",
        null or "" => "Member of staff",
        _ => role
    };

    private Task<bool> MessagingEnabledAsync(CancellationToken cancellationToken) =>
        settings.GetBoolAsync(SettingKeys.MessagingEnabled, SettingKeys.DefaultMessagingEnabled, cancellationToken);

    private async Task<string> AllowedExtensionsTextAsync(CancellationToken cancellationToken) =>
        await settings.GetStringAsync(SettingKeys.MessagingAllowedExtensions, cancellationToken)
        ?? SettingKeys.DefaultMessagingAllowedExtensions;

    private async Task<IReadOnlyCollection<string>> AllowedExtensionsAsync(CancellationToken cancellationToken) =>
        (await AllowedExtensionsTextAsync(cancellationToken))
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
        .ToArray();
}
