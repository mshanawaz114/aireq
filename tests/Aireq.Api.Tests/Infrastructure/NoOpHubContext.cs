// NoOpHubContext — a do-nothing IHubContext for tests of services that push
// SignalR messages. Pushes go to /dev/null; persistence is asserted via the DB.
//
// Refs: AIRMVP1-403

using Microsoft.AspNetCore.SignalR;

namespace Aireq.Api.Tests.Infrastructure;

public sealed class NoOpHubContext<THub> : IHubContext<THub> where THub : Hub
{
    public IHubClients Clients { get; } = new NoOpClients();
    public IGroupManager Groups { get; } = new NoOpGroups();

    private sealed class NoOpClients : IHubClients
    {
        private static readonly IClientProxy Proxy = new NoOpProxy();
        public IClientProxy All => Proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Client(string connectionId) => Proxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy;
        public IClientProxy Group(string groupName) => Proxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy;
        public IClientProxy User(string userId) => Proxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy;
    }

    private sealed class NoOpProxy : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class NoOpGroups : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
