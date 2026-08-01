using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Data;
using PublicationSite.Api.DTOs.Proposals;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

/// <summary>
/// Saved sets of supervisors, one coordinator's at a time.
///
/// A coordinator sends the same handful of proposals to the same handful of people every cycle,
/// and rebuilding that list by hand each time is where the mistakes come from. A group is a
/// shortcut for filling in the form and nothing more: it grants nobody anything, and sending to
/// one goes through exactly the checks sending to the same people by hand would.
/// </summary>
public class SupervisorGroupService(ApplicationDbContext db) : ISupervisorGroupService
{
    public async Task<IReadOnlyList<SupervisorGroupDto>> GetMineAsync(
        Guid ownerId, CancellationToken cancellationToken = default) =>
        await db.SupervisorGroups
            .Where(g => g.OwnerId == ownerId)
            .OrderBy(g => g.Name)
            .Select(Projection)
            .ToListAsync(cancellationToken);

    public async Task<SupervisorGroupDto> CreateAsync(
        Guid ownerId, SaveSupervisorGroupRequest request, CancellationToken cancellationToken = default)
    {
        var name = request.Name.Trim();
        await EnsureNameIsFreeAsync(ownerId, name, null, cancellationToken);

        var members = await ResolveMembersAsync(request.SupervisorIds, cancellationToken);

        var group = new SupervisorGroup { OwnerId = ownerId, Name = name };
        foreach (var supervisorId in members)
        {
            group.Members.Add(new SupervisorGroupMember { SupervisorId = supervisorId });
        }

        db.SupervisorGroups.Add(group);
        await db.SaveChangesAsync(cancellationToken);

        return await GetOneAsync(group.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<SupervisorGroupDto>> GetAllAsync(
        string? search = null, CancellationToken cancellationToken = default)
    {
        var query = db.SupervisorGroups.AsQueryable();

        // One box for three things. An administrator tidying up is looking for a group by its
        // name, or for everything one coordinator has left behind, or for every group that still
        // names a supervisor who has gone, and making them choose which of those first would slow
        // down all three.
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(g =>
                g.Name.Contains(search)
                || g.Owner.FirstName.Contains(search)
                || g.Owner.LastName.Contains(search)
                || g.Members.Any(m => m.Supervisor.FirstName.Contains(search)
                                   || m.Supervisor.LastName.Contains(search)));
        }

        return await query
            .OrderBy(g => g.Owner.LastName)
            .ThenBy(g => g.Owner.FirstName)
            .ThenBy(g => g.Name)
            .Select(Projection)
            .ToListAsync(cancellationToken);
    }

    public async Task<SupervisorGroupDto> UpdateAsync(
        Guid groupId, Guid? ownerId, SaveSupervisorGroupRequest request, CancellationToken cancellationToken = default)
    {
        var group = await db.SupervisorGroups
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken)
            ?? throw new NotFoundException(nameof(SupervisorGroup), groupId);

        EnsureOwned(group, ownerId);

        var name = request.Name.Trim();

        // Checked against the group's owner rather than the person editing: an administrator
        // renaming somebody's group must not be allowed to give them two of the same name.
        await EnsureNameIsFreeAsync(group.OwnerId, name, groupId, cancellationToken);

        var members = await ResolveMembersAsync(request.SupervisorIds, cancellationToken);

        group.Name = name;
        group.UpdatedAt = DateTime.UtcNow;

        // Replaced wholesale rather than diffed: membership is what the coordinator has just
        // ticked, so anything not ticked is no longer in the group.
        group.Members.Clear();
        foreach (var supervisorId in members)
        {
            group.Members.Add(new SupervisorGroupMember { SupervisorId = supervisorId });
        }

        await db.SaveChangesAsync(cancellationToken);

        return await GetOneAsync(group.Id, cancellationToken);
    }

    public async Task DeleteAsync(Guid groupId, Guid? ownerId, CancellationToken cancellationToken = default)
    {
        var group = await db.SupervisorGroups.FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken)
            ?? throw new NotFoundException(nameof(SupervisorGroup), groupId);

        EnsureOwned(group, ownerId);

        db.SupervisorGroups.Remove(group);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> DeleteManyAsync(
        IReadOnlyList<Guid> groupIds, CancellationToken cancellationToken = default)
    {
        if (groupIds.Count == 0) return 0;

        var ids = groupIds.Distinct().ToList();
        return await db.SupervisorGroups
            .Where(g => ids.Contains(g.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<int> DeleteAllAsync(CancellationToken cancellationToken = default) =>
        await db.SupervisorGroups.ExecuteDeleteAsync(cancellationToken);

    /// <summary>
    /// Not found rather than forbidden. A coordinator has no way to learn that another
    /// coordinator's group exists, and answering "you may not touch that one" would tell them.
    /// A null owner is an administrator, who may touch any of them.
    /// </summary>
    private static void EnsureOwned(SupervisorGroup group, Guid? ownerId)
    {
        if (ownerId is { } owner && group.OwnerId != owner)
        {
            throw new NotFoundException(nameof(SupervisorGroup), group.Id);
        }
    }

    private async Task EnsureNameIsFreeAsync(
        Guid ownerId, string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        var taken = await db.SupervisorGroups.AnyAsync(
            g => g.OwnerId == ownerId && g.Id != exceptId && g.Name.ToLower() == name.ToLower(),
            cancellationToken);

        if (taken)
        {
            throw new ConflictException($"You already have a group called \"{name}\". Pick another name.");
        }
    }

    /// <summary>
    /// The ids that may go into a group: supervisors, and accounts that actually exist. An id the
    /// request named but the database does not have would otherwise vanish silently, leaving the
    /// coordinator with a group smaller than the one they believe they saved.
    ///
    /// Availability is deliberately not checked here. A group is a list kept over months, and
    /// refusing to save one because somebody is away this week would make it useless as a list;
    /// who can actually be asked is settled when the proposals go out.
    /// </summary>
    private async Task<List<Guid>> ResolveMembersAsync(
        IReadOnlyList<Guid> supervisorIds, CancellationToken cancellationToken)
    {
        var wanted = supervisorIds.Distinct().ToList();

        var found = await db.Users
            .Where(u => wanted.Contains(u.Id))
            .Select(u => new
            {
                u.Id,
                Name = u.FirstName + " " + u.LastName,
                IsSupervisor = db.UserRoles
                    .Where(ur => ur.UserId == u.Id)
                    .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name!)
                    .Any(r => r == RoleNames.Supervisor)
            })
            .ToListAsync(cancellationToken);

        if (found.Count != wanted.Count)
        {
            throw new BusinessRuleException(
                "One of the people chosen no longer has an account. Refresh the page and try again.");
        }

        var notSupervisors = found.Where(u => !u.IsSupervisor).Select(u => u.Name).ToList();
        if (notSupervisors.Count > 0)
        {
            throw new BusinessRuleException(
                $"A group holds supervisors, and these are not: {string.Join(", ", notSupervisors)}.");
        }

        return found.Select(u => u.Id).ToList();
    }

    private async Task<SupervisorGroupDto> GetOneAsync(Guid groupId, CancellationToken cancellationToken) =>
        await db.SupervisorGroups
            .Where(g => g.Id == groupId)
            .Select(Projection)
            .FirstAsync(cancellationToken);

    /// <summary>
    /// Shared by the list and the single reads so both say the same thing about a group. Held as
    /// an expression rather than written as a method, because a method would have to be handed a
    /// loaded entity and this has to run in the database.
    /// </summary>
    private static readonly Expression<Func<SupervisorGroup, SupervisorGroupDto>> Projection = g =>
        new SupervisorGroupDto(
            g.Id,
            g.Name,
            g.OwnerId,
            g.Owner.FirstName + " " + g.Owner.LastName,
            g.Members.Count,
            g.Members.Count(m => m.Supervisor.IsAvailable && m.Supervisor.Status == UserStatus.Enabled),
            g.Members
                .OrderBy(m => m.Supervisor.LastName)
                .ThenBy(m => m.Supervisor.FirstName)
                .Select(m => new SupervisorGroupMemberDto(
                    m.SupervisorId,
                    m.Supervisor.FirstName + " " + m.Supervisor.LastName,
                    m.Supervisor.IsAvailable && m.Supervisor.Status == UserStatus.Enabled))
                .ToList());
}
