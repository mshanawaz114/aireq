// FakeHttpMessageHandler — capture-and-respond HttpMessageHandler for tests.
// Lets the gateway tests assert on the request and stub the response without
// hitting api.anthropic.com.
//
// Refs: AIRMVP1-105

using System.Net;

namespace Aireq.Api.Tests.Llm;

public sealed class FakeHttpMessageHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        // Buffer the body before we hand the request off — once the test reads
        // request.Content the underlying stream is consumed.
        if (request.Content is not null)
        {
            var body = await request.Content.ReadAsStringAsync(ct);
            request.Content = new StringContent(body, request.Content.Headers.ContentType?.MediaType is { } mt
                ? new System.Net.Http.Headers.MediaTypeHeaderValue(mt)
                : null);
        }
        Requests.Add(request);
        return await handler(request, ct);
    }

    public static FakeHttpMessageHandler RespondingWith(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        }));
}
