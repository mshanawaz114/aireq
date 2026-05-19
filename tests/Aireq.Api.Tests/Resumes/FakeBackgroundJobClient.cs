// FakeBackgroundJobClient — minimal IBackgroundJobClient stand-in for tests.
// Records every Create / Enqueue so assertions can verify the job that would
// have been scheduled (type, method name, args) without spinning up Hangfire.
//
// Refs: AIRMVP1-104

using Hangfire;
using Hangfire.Common;
using Hangfire.States;

namespace Aireq.Api.Tests.Resumes;

public sealed class FakeBackgroundJobClient : IBackgroundJobClient
{
    public sealed record EnqueuedJob(Type Type, string Method, object?[] Args, string State);

    private readonly List<EnqueuedJob> _enqueued = new();
    public IReadOnlyList<EnqueuedJob> Enqueued => _enqueued;

    public string Create(Job job, IState state)
    {
        _enqueued.Add(new EnqueuedJob(
            job.Type,
            job.Method.Name,
            job.Args.ToArray(),
            state.Name));
        return Guid.NewGuid().ToString("N");
    }

    public bool ChangeState(string jobId, IState state, string expectedState) => true;
}
