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
    ISystemSettingService settingService,
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
    /// Two rules, because there are two kinds of person here, and both are the administrator's to
    /// set. A student writes to whichever of the people on their publication the institution has
    /// named: by default the supervisor they were assigned, the coordinator running it, and the head
    /// of their department. Whoever is working on the publication writes to the student, again from
    /// a named list, by default everybody with a job here.
    ///
    /// Either direction can be switched off on its own, and a list with nothing on it means nobody.
    /// A list nobody has ever configured means the default, which is not the same thing: an
    /// institution that has never opened the settings screen should get the behaviour the system
    /// was shipped with.
    ///
    /// The Staff role is on neither list and cannot be put on one. It is the placeholder an
    /// institutional address holds before an administrator says what the person actually is, so
    /// there is no job there yet and nobody to write.
    ///
    /// On top of both, anybody who has already written to you here, so long as your direction is
    /// switched on. Without that, a message from somebody the lists do not name would arrive with
    /// no way to answer it, which is worse than not being able to write to them at all. Narrowing a
    /// list stops new conversations; it does not gag somebody mid-exchange.
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
                found[value] = new MessageCounterpartDto(value, name, role, 0, null);
            }
        }

        var rules = await settingService.GetMessagingSettingsAsync(cancellationToken);
        var isTheStudent = container.StudentId == userId;

        // What an administrator has decided about this publication in particular, which overrides
        // everything the institution has decided in general. Loaded once and asked about each
        // person below.
        var overrides = await OverridesForAsync(publicationContainerId, cancellationToken);

        // Whether this person's direction is open at all. It gates the reply rule below as well as
        // the lists: switching a direction off has to mean off, not "off unless somebody wrote to
        // you first".
        var mayWriteAtAll = isTheStudent
            ? rules.StudentsMayWrite
            : rules.StaffMayWrite && await HoldsAnOperationalRoleAsync(userId, cancellationToken);

        // A rule about this publication has the last word, either way: it can silence somebody the
        // institution allows, and let through somebody the institution generally shuts out.
        if (await overrides.DecideAsync(userId, mayWriteAtAll) is false)
        {
            return [];
        }

        if (isTheStudent)
        {
            if (rules.StudentMayWriteToRoles.Contains(RoleNames.Supervisor))
            {
                Add(container.AssignedSupervisorId, container.SupervisorName, "Supervisor");
            }

            if (rules.StudentMayWriteToRoles.Contains(RoleNames.Coordinator))
            {
                Add(container.CoordinatorId, container.CoordinatorName, "Coordinator");
            }

            if (rules.StudentMayWriteToRoles.Contains(RoleNames.HeadOfDepartment)
                && container.DepartmentId is { } departmentId)
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

            // The committee, where an institution has said a student may write to it. Read from the
            // seats actually filled on this publication rather than from the role at large: a
            // reviewer somewhere else in the institution is not somebody this student has business
            // with.
            var committeeRoles = SettingKeys.CommitteeMessagingRoles
                .Where(rules.StudentMayWriteToRoles.Contains)
                .ToList();

            if (committeeRoles.Count > 0)
            {
                var members = await db.CommitteeMembers
                    .Where(m => m.Committee.Publication.PublicationContainerId == publicationContainerId)
                    .Select(m => new
                    {
                        m.UserId,
                        Name = m.User.FirstName + " " + m.User.LastName,
                        Role = m.RoleType == CommitteeMemberRoleType.Reviewer
                            ? RoleNames.Reviewer
                            : RoleNames.ExternalCommitteeMember
                    })
                    .ToListAsync(cancellationToken);

                foreach (var member in members.Where(m => committeeRoles.Contains(m.Role)))
                {
                    Add(member.UserId, member.Name, Readable(member.Role));
                }
            }
        }
        else if (await HoldsAnyOfAsync(userId, rules.StaffMayWriteToStudentRoles, cancellationToken))
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

        // And when each conversation was last touched, in either direction, so a screen with
        // nothing waiting can still open on the one somebody was last having rather than on a
        // chooser. Grouped by the other person, which is the sender or the recipient depending on
        // which way the message went.
        var lastFromThem = await db.ContainerMessages
            .Where(m => m.PublicationContainerId == publicationContainerId && m.RecipientUserId == userId)
            .GroupBy(m => m.SenderUserId)
            .Select(g => new { OtherId = g.Key, At = g.Max(m => m.SentAt) })
            .ToListAsync(cancellationToken);

        var lastToThem = await db.ContainerMessages
            .Where(m => m.PublicationContainerId == publicationContainerId && m.SenderUserId == userId)
            .GroupBy(m => m.RecipientUserId)
            .Select(g => new { OtherId = g.Key, At = g.Max(m => m.SentAt) })
            .ToListAsync(cancellationToken);

        foreach (var row in lastFromThem.Concat(lastToThem))
        {
            if (found.TryGetValue(row.OtherId, out var counterpart)
                && (counterpart.LastMessageAt is null || counterpart.LastMessageAt < row.At))
            {
                found[row.OtherId] = counterpart with { LastMessageAt = row.At };
            }
        }

        // And last, anybody an administrator has silenced on this publication comes off the list.
        // A rule is symmetrical: it stops them writing, which is the check above, and it stops
        // anybody writing to them, which is this one. Applied after the reply rule on purpose, so
        // silencing somebody also closes a conversation they were already in.
        foreach (var id in found.Keys.ToList())
        {
            if (await overrides.DecideAsync(id, true) is false)
            {
                found.Remove(id);
            }
        }

        // Whoever is waiting first, then whoever was spoken to most recently, then everybody else
        // by name. Somebody who has never been written to sorts last, which is where a name nobody
        // has needed yet belongs.
        return found.Values
            .OrderByDescending(c => c.UnreadFromThem)
            .ThenByDescending(c => c.LastMessageAt ?? DateTime.MinValue)
            .ThenBy(c => c.Name)
            .ToList();
    }

    // ---------- What an administrator has decided about one publication ----------

    public async Task<ContainerMessagingRulesDto> GetRulesAsync(
        Guid publicationContainerId, CancellationToken cancellationToken = default)
    {
        var participants = await ParticipantsAsync(publicationContainerId, cancellationToken);
        var byId = participants.ToDictionary(p => p.UserId, p => p.Name);

        var rules = await db.ContainerMessagingRules
            .AsNoTracking()
            .Where(r => r.PublicationContainerId == publicationContainerId)
            .OrderBy(r => r.TargetUserId == null && r.TargetRole == null ? 0 : r.TargetRole != null ? 1 : 2)
            .ThenBy(r => r.SetAt)
            .Select(r => new
            {
                r.Id, r.TargetRole, r.TargetUserId, r.Allowed, r.Reason, r.SetAt,
                SetByName = r.SetByUser.FirstName + " " + r.SetByUser.LastName
            })
            .ToListAsync(cancellationToken);

        // Names for anybody a rule is about who is no longer on the publication: a supervisor can
        // be replaced, and the rule about them outlives that. Looked up in one query rather than
        // per rule.
        var strangers = rules
            .Where(r => r.TargetUserId is { } id && !byId.ContainsKey(id))
            .Select(r => r.TargetUserId!.Value)
            .Distinct()
            .ToList();

        if (strangers.Count > 0)
        {
            var names = await db.Users
                .Where(u => strangers.Contains(u.Id))
                .Select(u => new { u.Id, Name = u.FirstName + " " + u.LastName })
                .ToListAsync(cancellationToken);

            foreach (var person in names)
            {
                byId[person.Id] = person.Name;
            }
        }

        var settings = await settingService.GetMessagingSettingsAsync(cancellationToken);

        return new ContainerMessagingRulesDto(
            settings.Enabled,
            rules.Select(r => new ContainerMessagingRuleDto(
                r.Id, r.TargetRole, r.TargetUserId,
                r.TargetUserId is { } id ? byId.GetValueOrDefault(id, "Somebody no longer here")
                : r.TargetRole is { } role ? $"Everybody who is {Readable(role)} here"
                : "Everybody on this publication",
                r.Allowed, r.Reason, r.SetByName, r.SetAt)).ToList(),
            participants,
            RoleNames.Operational.Concat([RoleNames.Student]).ToList());
    }

    public async Task<ContainerMessagingRuleDto> SetRuleAsync(
        Guid publicationContainerId,
        Guid actingAdminId,
        SetContainerMessagingRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await db.PublicationContainers.AnyAsync(c => c.Id == publicationContainerId, cancellationToken))
        {
            throw new NotFoundException(nameof(PublicationContainer), publicationContainerId);
        }

        var role = string.IsNullOrWhiteSpace(request.TargetRole) ? null : request.TargetRole.Trim();

        // One target or none. A rule naming both a role and a person is two different rules asked
        // for at once, and guessing which was meant is worse than saying so.
        if (role is not null && request.TargetUserId is not null)
        {
            throw new BusinessRuleException(
                "A rule is about a role, about one person, or about the whole publication. Choose one.");
        }

        if (role is not null && !RoleNames.All.Contains(role))
        {
            throw new BusinessRuleException($"'{role}' is not a role here.");
        }

        var reason = (request.Reason ?? string.Empty).Trim();
        if (reason.Length == 0)
        {
            throw new BusinessRuleException(
                "Say why. Another administrator will find this later and needs to know what it is for.");
        }

        if (request.TargetUserId is { } targetUserId
            && !await db.Users.AnyAsync(u => u.Id == targetUserId, cancellationToken))
        {
            throw new NotFoundException(nameof(ApplicationUser), targetUserId);
        }

        // One rule per target: an existing one is changed rather than joined by a second that
        // contradicts it.
        var rule = await db.ContainerMessagingRules.FirstOrDefaultAsync(
            r => r.PublicationContainerId == publicationContainerId
                 && r.TargetRole == role
                 && r.TargetUserId == request.TargetUserId,
            cancellationToken);

        if (rule is null)
        {
            rule = new ContainerMessagingRule
            {
                PublicationContainerId = publicationContainerId,
                TargetRole = role,
                TargetUserId = request.TargetUserId
            };
            db.ContainerMessagingRules.Add(rule);
        }

        rule.Allowed = request.Allowed;
        rule.Reason = reason;
        rule.SetByUserId = actingAdminId;
        rule.SetAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        var describedAs = request.TargetUserId is { } person
            ? await db.Users.Where(u => u.Id == person)
                .Select(u => u.FirstName + " " + u.LastName).SingleAsync(cancellationToken)
            : role is not null ? $"everybody who is {Readable(role)} here"
            : "everybody on this publication";

        // On the publication's own history, because it changes who can say anything about it and
        // everybody with access to it is entitled to know that it was done.
        await auditService.LogActivityAsync(
            publicationContainerId,
            actingAdminId,
            request.Allowed ? "Messaging allowed" : "Messaging stopped",
            $"{(request.Allowed ? "Allowed" : "Stopped")} messages on this publication for {describedAs}. {reason}");

        var refreshed = await GetRulesAsync(publicationContainerId, cancellationToken);
        return refreshed.Rules.Single(r => r.Id == rule.Id);
    }

    public async Task RemoveRuleAsync(
        Guid publicationContainerId, Guid actingAdminId, Guid ruleId, CancellationToken cancellationToken = default)
    {
        var rule = await db.ContainerMessagingRules
            .FirstOrDefaultAsync(
                r => r.Id == ruleId && r.PublicationContainerId == publicationContainerId, cancellationToken)
            ?? throw new NotFoundException(nameof(ContainerMessagingRule), ruleId);

        var describedAs = rule.TargetUserId is { } person
            ? await db.Users.Where(u => u.Id == person)
                .Select(u => u.FirstName + " " + u.LastName).SingleAsync(cancellationToken)
            : rule.TargetRole is { } role ? $"everybody who is {Readable(role)} here"
            : "everybody on this publication";

        db.ContainerMessagingRules.Remove(rule);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogActivityAsync(
            publicationContainerId,
            actingAdminId,
            "Messaging rule removed",
            $"Removed the rule about {describedAs}. This publication follows the institution's settings again.");
    }

    /// <summary>
    /// Everybody with a part in this publication, so an administrator picks a name rather than
    /// typing one: the student, the coordinator, the supervisor assigned, the heads of the
    /// student's department, and the committee appointed to judge the paper.
    /// </summary>
    private async Task<IReadOnlyList<ContainerParticipantDto>> ParticipantsAsync(
        Guid publicationContainerId, CancellationToken cancellationToken)
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

        var people = new Dictionary<Guid, ContainerParticipantDto>
        {
            [container.StudentId] = new(container.StudentId, container.StudentName, "Student"),
            [container.CoordinatorId] = new(container.CoordinatorId, container.CoordinatorName, "Coordinator")
        };

        if (container.AssignedSupervisorId is { } supervisorId && container.SupervisorName is { } supervisorName)
        {
            people[supervisorId] = new(supervisorId, supervisorName, "Supervisor");
        }

        if (container.DepartmentId is { } departmentId)
        {
            var heads = await db.HeadOfDepartmentProfiles
                .Where(h => h.DepartmentId == departmentId)
                .Select(h => new { h.UserId, Name = h.User.FirstName + " " + h.User.LastName })
                .ToListAsync(cancellationToken);

            foreach (var head in heads)
            {
                people.TryAdd(head.UserId, new(head.UserId, head.Name, "Head of Department"));
            }
        }

        var members = await db.CommitteeMembers
            .Where(m => m.Committee.Publication.PublicationContainerId == publicationContainerId)
            .Select(m => new
            {
                m.UserId,
                Name = m.User.FirstName + " " + m.User.LastName,
                Role = m.RoleType == CommitteeMemberRoleType.Reviewer
                    ? RoleNames.Reviewer
                    : RoleNames.ExternalCommitteeMember
            })
            .ToListAsync(cancellationToken);

        foreach (var member in members)
        {
            people.TryAdd(member.UserId, new(member.UserId, member.Name, Readable(member.Role)));
        }

        return people.Values.OrderBy(p => p.Name).ToList();
    }

    /// <summary>
    /// The rules an administrator has set on one publication, ready to be asked about people.
    ///
    /// Loaded once per screen rather than per person: a publication has a handful of these at most,
    /// and asking the database once per name on a list is how a short list becomes a slow screen.
    /// </summary>
    private async Task<MessagingOverrides> OverridesForAsync(
        Guid publicationContainerId, CancellationToken cancellationToken)
    {
        var rules = await db.ContainerMessagingRules
            .AsNoTracking()
            .Where(r => r.PublicationContainerId == publicationContainerId)
            .Select(r => new { r.TargetRole, r.TargetUserId, r.Allowed })
            .ToListAsync(cancellationToken);

        var wholeContainer = rules
            .FirstOrDefault(r => r.TargetRole == null && r.TargetUserId == null)?.Allowed;

        var byUser = rules
            .Where(r => r.TargetUserId is not null)
            .ToDictionary(r => r.TargetUserId!.Value, r => r.Allowed);

        var byRole = rules
            .Where(r => r.TargetRole is not null)
            .ToDictionary(r => r.TargetRole!, r => r.Allowed, StringComparer.Ordinal);

        return new MessagingOverrides(wholeContainer, byUser, byRole, RolesOfAsync);

        Task<List<string>> RolesOfAsync(Guid userId) =>
            db.UserRoles
                .Where(ur => ur.UserId == userId)
                .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name!)
                .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// What one publication's own rules say about a person, over whatever the institution says.
    ///
    /// Most specific wins: a rule naming the person, then one naming a role they hold, then one
    /// covering the whole publication, then the answer that was passed in. Among roles a refusal
    /// wins, because somebody holding two roles where one has been silenced has been silenced.
    /// </summary>
    private sealed class MessagingOverrides(
        bool? wholeContainer,
        Dictionary<Guid, bool> byUser,
        Dictionary<string, bool> byRole,
        Func<Guid, Task<List<string>>> rolesOf)
    {
        public async Task<bool> DecideAsync(Guid userId, bool otherwise)
        {
            if (byUser.TryGetValue(userId, out var forThisPerson))
            {
                return forThisPerson;
            }

            if (byRole.Count > 0)
            {
                var theirs = (await rolesOf(userId))
                    .Where(byRole.ContainsKey)
                    .Select(role => byRole[role])
                    .ToList();

                if (theirs.Count > 0)
                {
                    return theirs.All(allowed => allowed);
                }
            }

            return wholeContainer ?? otherwise;
        }
    }

    /// <summary>
    /// Whether this person has a job here at all. The Staff role does not count: it is what an
    /// institutional address holds before an administrator says what the person is.
    /// </summary>
    private Task<bool> HoldsAnOperationalRoleAsync(Guid userId, CancellationToken cancellationToken) =>
        HoldsAnyOfAsync(userId, RoleNames.Operational, cancellationToken);

    /// <summary>Whether this person holds any of the roles an administrator named.</summary>
    private Task<bool> HoldsAnyOfAsync(
        Guid userId, IReadOnlyList<string> roles, CancellationToken cancellationToken) =>
        roles.Count == 0
            ? Task.FromResult(false)
            : db.UserRoles
                .Where(ur => ur.UserId == userId)
                .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
                .AnyAsync(name => name != null && roles.Contains(name), cancellationToken);

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
