using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;

namespace Auth_API.Tests.Helpers;

/// <summary>
/// Dictionary-backed fake of the per-user encryption key repository for crypto
/// tests: stateful get/create/delete plus a create-call counter for asserting
/// lazy single creation.
/// </summary>
public class InMemoryUserEncryptionKeyRepository : IUserEncryptionKeyRepository
{
    private readonly Dictionary<Guid, UserEncryptionKey> _keys = new();

    public int CreateCalls { get; private set; }

    public Task<UserEncryptionKey?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_keys.GetValueOrDefault(userId));

    public Task CreateAsync(UserEncryptionKey key, CancellationToken cancellationToken)
    {
        CreateCalls++;
        if (!_keys.TryAdd(key.UserId, key))
        {
            throw new InvalidOperationException("Duplicate key (unique constraint).");
        }

        return Task.CompletedTask;
    }

    public Task DeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        _keys.Remove(userId);
        return Task.CompletedTask;
    }
}
