using System.Net;
using System.Text;

namespace Integricode.Tests.Fakes;

public sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Body, string? Authorization);

/// <summary>An in-memory HTTP handler: every request is recorded and answered by the first matching route, else 404.</summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly List<(HttpMethod Method, string PathAndQuery, Func<RecordedRequest, HttpResponseMessage> Respond)> routes = [];

    public List<RecordedRequest> Requests { get; } = [];

    public FakeHttpHandler On(HttpMethod method, string pathAndQuery, Func<RecordedRequest, HttpResponseMessage> respond)
    {
        routes.Add((method, pathAndQuery, respond));
        return this;
    }

    public FakeHttpHandler OnJson(HttpMethod method, string pathAndQuery, string json, string? link = null) =>
        On(method, pathAndQuery, _ => Json(json, link: link));

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK, string? link = null)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (link is not null)
        {
            response.Headers.Add("Link", link);
        }

        return response;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new RecordedRequest(request.Method, request.RequestUri!, body, request.Headers.Authorization?.ToString());
        Requests.Add(recorded);
        foreach (var (method, pathAndQuery, respond) in routes)
        {
            if (method == request.Method && pathAndQuery == request.RequestUri!.PathAndQuery)
            {
                var response = respond(recorded);
                response.RequestMessage = request;
                return response;
            }
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request, Content = new StringContent("{\"message\":\"Not Found\"}") };
    }
}
