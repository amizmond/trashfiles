using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Estimation.Core.JiraIntegration.Client;
using Xunit;

namespace Estimation.Core.Tests.JiraIntegration;

public class JiraResilienceHandlerTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<int, HttpResponseMessage> _respond;

        public StubHandler(Func<int, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public int Calls;

        public static StubHandler Always(HttpStatusCode status) =>
            new(_ => new HttpResponseMessage(status));

        public static StubHandler Then(HttpStatusCode first, HttpStatusCode rest) =>
            new(call => new HttpResponseMessage(call == 1 ? first : rest));

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref Calls);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_respond(call));
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpMessageHandler inner, CancellationToken cancellationToken = default)
    {
        var handler = new JiraResilienceHandler { InnerHandler = inner };
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://jira.example.test/rest/api/2/issue/PROJ-1");

        return await invoker.SendAsync(request, cancellationToken);
    }

    [Fact]
    public async Task A_successful_response_is_returned_without_retrying()
    {
        var stub = StubHandler.Always(HttpStatusCode.OK);

        var response = await SendAsync(stub);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, stub.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task A_client_error_is_surfaced_immediately_rather_than_retried(HttpStatusCode status)
    {
        var stub = StubHandler.Always(status);

        var response = await SendAsync(stub);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(1, stub.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task A_transient_failure_is_retried_and_the_retry_is_returned(HttpStatusCode status)
    {
        var stub = StubHandler.Then(status, HttpStatusCode.OK);

        var response = await SendAsync(stub);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, stub.Calls);
    }

    [Fact]
    public async Task Being_rate_limited_is_treated_as_transient()
    {
        var stub = StubHandler.Then((HttpStatusCode)429, HttpStatusCode.OK);

        var response = await SendAsync(stub);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, stub.Calls);
    }

    [Fact]
    public async Task A_dropped_connection_is_retried()
    {
        var stub = new StubHandler(call =>
        {
            if (call == 1)
            {
                throw new HttpRequestException("connection reset");
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var response = await SendAsync(stub);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, stub.Calls);
    }

    private static async Task<HttpResponseMessage> SendOnceAsync(HttpMessageHandler inner)
    {
        var handler = new JiraResilienceHandler { InnerHandler = inner };
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://jira.example.test/rest/api/2/issueLink");
        request.Options.Set(JiraResilienceHandler.SingleAttemptKey, true);

        return await invoker.SendAsync(request, CancellationToken.None);
    }

    [Fact]
    public async Task A_request_marked_single_attempt_is_not_retried_on_a_transient_failure()
    {
        var stub = StubHandler.Always(HttpStatusCode.ServiceUnavailable);

        var response = await SendOnceAsync(stub);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, stub.Calls);
    }

    [Fact]
    public async Task A_request_marked_single_attempt_lets_a_dropped_connection_through()
    {
        var stub = new StubHandler(_ => throw new HttpRequestException("connection reset"));

        await Assert.ThrowsAsync<HttpRequestException>(() => SendOnceAsync(stub));

        Assert.Equal(1, stub.Calls);
    }

    [Fact]
    public async Task A_retry_after_delay_is_waited_out_rather_than_the_shorter_default()
    {
        var stub = new StubHandler(call =>
        {
            if (call > 1)
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            var response = new HttpResponseMessage((HttpStatusCode)429);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
            return response;
        });

        var clock = Stopwatch.StartNew();
        var response = await SendAsync(stub);
        var elapsed = clock.Elapsed;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            elapsed >= TimeSpan.FromMilliseconds(800),
            $"waited only {elapsed.TotalMilliseconds:F0}ms, so Retry-After was not honoured");
    }

    [Fact]
    public async Task A_retry_after_supplied_as_a_date_is_waited_out_too()
    {
        var stub = new StubHandler(call =>
        {
            if (call > 1)
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(1));
            return response;
        });

        var clock = Stopwatch.StartNew();
        var response = await SendAsync(stub);
        var elapsed = clock.Elapsed;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            elapsed >= TimeSpan.FromMilliseconds(800),
            $"waited only {elapsed.TotalMilliseconds:F0}ms, so the Retry-After date was not honoured");
    }

    [Fact]
    public async Task A_retry_after_date_already_in_the_past_is_ignored()
    {
        var stub = new StubHandler(call =>
        {
            if (call > 1)
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(-5));
            return response;
        });

        var response = await SendAsync(stub);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, stub.Calls);
    }

    [Fact]
    public async Task The_backoff_keeps_growing_across_attempts()
    {
        var stub = StubHandler.Always(HttpStatusCode.ServiceUnavailable);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SendAsync(stub, cts.Token));

        Assert.True(stub.Calls >= 2, $"expected more than one attempt inside a second, saw {stub.Calls}");
    }

    [Fact]
    public async Task Shutdown_during_the_backoff_stops_the_retry_loop()
    {
        var stub = StubHandler.Always(HttpStatusCode.ServiceUnavailable);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SendAsync(stub, cts.Token));

        Assert.True(stub.Calls <= 2, $"kept retrying after cancellation, saw {stub.Calls} attempts");
    }

    [Fact]
    public async Task A_response_that_is_about_to_be_retried_is_disposed()
    {
        var failed = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("boom")
        };

        var stub = new StubHandler(call => call == 1 ? failed : new HttpResponseMessage(HttpStatusCode.OK));

        await SendAsync(stub);

        await Assert.ThrowsAsync<ObjectDisposedException>(() => failed.Content.ReadAsStringAsync());
    }
}
