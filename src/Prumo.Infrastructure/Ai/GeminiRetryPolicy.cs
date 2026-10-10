using System.Net;

namespace Prumo.Infrastructure.Ai;

internal static class GeminiRetryPolicy
{
    public const int MaxAttempts = 3;
    public static readonly TimeSpan Delay = TimeSpan.FromSeconds(15);

    public static bool IsTransient(HttpStatusCode? statusCode) =>
        statusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout or >= HttpStatusCode.InternalServerError;
}
