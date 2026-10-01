using System.Net;
using System.Text.Json.Nodes;

namespace Claudio.Core.Tests;

/// <summary>
/// Stands in for Anthropic: the usage and profile endpoints, and the token endpoint the sign-in
/// and the refresh post to. No test ever reaches the network.
/// </summary>
internal sealed class StubbedAnthropic : HttpMessageHandler
{
    public (HttpStatusCode Status, string Body) Usage { get; set; } = (HttpStatusCode.InternalServerError, "");
    public (HttpStatusCode Status, string Body) Profile { get; set; } = (HttpStatusCode.NotFound, "");
    public (HttpStatusCode Status, string Body) Token { get; set; } = (HttpStatusCode.InternalServerError, "");
    public Action<string?>? OnUsage { get; set; }

    /// <summary>The next request to <see cref="HoldPath"/> waits for this before answering.</summary>
    public TaskCompletionSource? Hold { get; set; }
    public string HoldPath { get; set; } = "/api/oauth/usage";
    /// <summary>Completed when the held request has reached the stub.</summary>
    public TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int UsageRequests { get; private set; }
    public List<JsonObject> TokenRequests { get; } = [];
    public string? LastAuthorization { get; private set; }
    public string? LastBeta { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!;
        (HttpStatusCode Status, string Body) answer;
        if (url.Host == "platform.claude.com" && url.AbsolutePath == "/v1/oauth/token")
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            TokenRequests.Add(JsonNode.Parse(body)!.AsObject());
            Assert.Equal("application/json", request.Content.Headers.ContentType?.MediaType);
            answer = Token;
        }
        else
        {
            Assert.Equal("api.anthropic.com", url.Host);
            LastAuthorization = request.Headers.Authorization?.ToString();
            LastBeta = request.Headers.TryGetValues("anthropic-beta", out var beta) ? beta.Single() : null;
            if (url.AbsolutePath.EndsWith("/usage", StringComparison.Ordinal))
            {
                UsageRequests++;
                OnUsage?.Invoke(LastAuthorization);
                answer = Usage;
            }
            else
            {
                answer = Profile;
            }
        }

        // Decided on arrival: a held request answers as the server stood when it was sent.
        if (Hold is { } hold && url.AbsolutePath == HoldPath)
        {
            Hold = null;
            Arrived.TrySetResult();
            await hold.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        }
        return new HttpResponseMessage(answer.Status) { Content = new StringContent(answer.Body) };
    }
}
