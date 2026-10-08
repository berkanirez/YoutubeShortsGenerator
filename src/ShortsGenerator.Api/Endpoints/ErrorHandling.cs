using Microsoft.AspNetCore.Diagnostics;

namespace ShortsGenerator.Api.Endpoints;

public static class ErrorHandling
{
    // Last line of defence for anything an endpoint didn't map itself. The caller gets the
    // same { "error": "..." } shape as every other error response and never a stack trace
    // (CLAUDE.md section 16); the exception itself is logged by the middleware.
    public static IApplicationBuilder UseJsonErrors(this WebApplication app)
    {
        return app.UseExceptionHandler(handler => handler.Run(async context =>
        {
            var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
            var (status, message) = exception switch
            {
                // Malformed request body etc. In Development minimal APIs throw these instead
                // of answering 400 themselves; without this they'd become 500s.
                BadHttpRequestException bad => (bad.StatusCode, bad.Message),
                // Our own "a local file is broken" errors carry an actionable message.
                InvalidDataException data => (StatusCodes.Status500InternalServerError, data.Message),
                _ => (StatusCodes.Status500InternalServerError,
                      "Unexpected server error. The details are in the API's console log."),
            };

            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(new { error = message });
        }));
    }
}
