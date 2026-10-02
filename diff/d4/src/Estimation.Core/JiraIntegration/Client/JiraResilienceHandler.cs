using System.Net;
using System.Net.Http.Headers;
using Serilog;

namespace Estimation.Core.JiraIntegration.Client;

public sealed class JiraResilienceHandler : DelegatingHandler
{
    private const int MaxAttempts = 6;
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MaxDelayPerAttempt = TimeSpan.FromSeconds(60);
    private const double JitterFactor = 0.3;

    public static readonly HttpRequestOptionsKey<bool> SingleAttemptKey = new("JiraSingleAttempt");

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        HttpResponseMessage? response = null;
        var delay = InitialDelay;
        var maxAttempts = request.Options.TryGetValue(SingleAttemptKey, out var single) && single ? 1 : MaxAttempts;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                response = await base.SendAsync(request, cancellationToken);
                if (!IsTransient(response.StatusCode) || attempt == maxAttempts)
                {
                    return response;
                }

                var serverHint = GetRetryAfter(response.Headers.RetryAfter);
                var wait = ApplyJitter(serverHint ?? delay);
                if (wait > MaxDelayPerAttempt)
                {
                    wait = MaxDelayPerAttempt;
                }

                Log.Warning("Jira request {Method} {Uri} failed with {StatusCode} (attempt {Attempt}/{Max}); retrying after {Delay}ms{Hint}",
                    request.Method, request.RequestUri, (int)response.StatusCode, attempt, maxAttempts,
                    (int)wait.TotalMilliseconds, serverHint.HasValue ? " (Retry-After honoured)" : string.Empty);

                response.Dispose();
                response = null;
                await Task.Delay(wait, cancellationToken);
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                lastException = ex;
                var wait = ApplyJitter(delay);
                if (wait > MaxDelayPerAttempt)
                {
                    wait = MaxDelayPerAttempt;
                }

                Log.Warning(ex, "Jira request {Method} {Uri} threw on attempt {Attempt}/{Max}; retrying after {Delay}ms",
                    request.Method, request.RequestUri, attempt, maxAttempts, (int)wait.TotalMilliseconds);
                await Task.Delay(wait, cancellationToken);
            }

            delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2);
        }

        if (response is not null)
        {
            return response;
        }
        throw lastException ?? new HttpRequestException("Jira request failed after retries.");
    }

    private static TimeSpan? GetRetryAfter(RetryConditionHeaderValue? header)
    {
        if (header is null)
        {
            return null;
        }
        if (header.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }
        if (header.Date is { } date)
        {
            var until = date - DateTimeOffset.UtcNow;
            if (until > TimeSpan.Zero)
            {
                return until;
            }
        }
        return null;
    }

    private static TimeSpan ApplyJitter(TimeSpan baseDelay)
    {
        var multiplier = 1.0 + (Random.Shared.NextDouble() * JitterFactor);
        return TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * multiplier);
    }

    private static bool IsTransient(HttpStatusCode status)
    {
        if ((int)status >= 500)
        {
            return true;
        }
        return status == HttpStatusCode.RequestTimeout
            || status == (HttpStatusCode)429;
    }
}
