namespace Tms.SharedKernel.Contracts;

/// <summary>Read-only view of users for modules that must reason about people they do not own (implemented by Platform).</summary>
public interface IUserDirectory
{
    /// <summary>Permissions the user holds through their roles. Empty for unknown or inactive users.</summary>
    Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken = default);

    /// <summary>Name and email of active users, for notifications. Unknown or inactive users are simply absent.</summary>
    Task<IReadOnlyDictionary<Guid, UserContact>> GetContactsAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken = default);
}
