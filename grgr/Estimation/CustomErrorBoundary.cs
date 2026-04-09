using Microsoft.AspNetCore.Components.Web;
using Serilog;

namespace Estimation;

public class CustomErrorBoundary : ErrorBoundary
{
    protected override Task OnErrorAsync(Exception exception)
    {
        Log.Error(exception, exception.Message);

        return Task.CompletedTask;
    }
}
