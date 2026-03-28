using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Template.Tests.FeedParser;

/// <summary>
/// Returns a fixed string body for all requests so parser tests never touch the network.
/// </summary>
internal sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly string _responseBody;

    public MockHttpMessageHandler(string responseBody)
    {
        _responseBody = responseBody;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(_responseBody)
        };
        return Task.FromResult(response);
    }
}
