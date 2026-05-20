// FakeHttpMessageHandler — capture-and-respond handler for tests.
//
// Why we don't hand the test the live HttpRequestMessage:
//   The gateway wraps its request in `using var httpReq = new HttpRequestMessage(...)`,
//   which disposes the request (and its Content) the moment the call returns.
//   A test that reads request.Content AFTER the call throws ObjectDisposedException.
//
// Fix: we read the body and copy the headers at intercept time into a plain
// CapturedRequest record. Tests assert against those captures, not the live
// HttpRequestMessage.
//
// Refs: AIRMVP1-105

using System.Net;

namespace Aireq.Api.Tests.Llm;

public sealed class FakeHttpMessageHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    : HttpMessageHandler
{
    public sealed record CapturedRequest(
        HttpMethod Method,
        Uri Uri,
        IReadOnlyDictionary<string, string[]> Headers,
        string Body);

    public List<CapturedRequest> Captured { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null
            ? ""
            : await request.Content.ReadAsStringAsync(ct);

        // Snapshot the headers (request-level + content-level) before anything
        // downstream can dispose them.
        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in request.Headers)
            headers[h.Key] = h.Value.ToArray();
        if (request.Content is not null)
            foreach (var h in request.Content.Headers)
                headers[h.Key] = h.Value.ToArray();

        Captured.Add(new CapturedRequest(
            request.Method,
            request.RequestUri ?? new Uri("about:blank"),
            headers,
            body));

        return await handler(request, ct);
    }

    public static FakeHttpMessageHandler RespondingWith(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        }));
}
