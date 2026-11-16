#nullable enable
using System.Collections.Concurrent;
using System.Net;
using Microsoft.Azure.Cosmos;
using CosmosResponseFactory = Phantom.CosmosDB.Core.Infrastructure.SdkInterop.CosmosResponseFactory;

namespace Phantom.CosmosDB.Core.Application.Account;

/// <summary>
/// Manages in-memory Cosmos database users and user query iterators.
/// </summary>
internal sealed class InMemoryUserService
{
    private readonly ConcurrentDictionary<string, InMemoryUser> _users = new();

    public User GetUser(string id)
        => _users.TryGetValue(id, out var existing)
            ? existing
            : new InMemoryUser(id, () => _users.TryRemove(id, out _), _users);

    public Task<UserResponse> CreateUserAsync(string id)
    {
        var user = new InMemoryUser(id, () => _users.TryRemove(id, out _));
        if (!_users.TryAdd(id, user))
            throw InMemoryCosmosException.Create($"User '{id}' already exists.", HttpStatusCode.Conflict, 0, string.Empty, 0);

        return Task.FromResult(CosmosResponseFactory.CreateUserResponse(user, new UserProperties(id), HttpStatusCode.Created));
    }

    public Task<UserResponse> UpsertUserAsync(string id)
    {
        var created = false;
        var user = _users.GetOrAdd(id, uid =>
        {
            created = true;
            return new InMemoryUser(uid, () => _users.TryRemove(uid, out _));
        });

        return Task.FromResult(CosmosResponseFactory.CreateUserResponse(
            user,
            new UserProperties(id),
            created ? HttpStatusCode.Created : HttpStatusCode.OK));
    }

    public List<T> QueryUsers<T>()
        => _users.Values.Select(u => (T)(object)new UserProperties(u.Id)).ToList();

    public void Clear() => _users.Clear();
}
