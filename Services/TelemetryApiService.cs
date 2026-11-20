using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Telemetry.Blazor.Components.Pages;
using Telemetry.Blazor.Models;

namespace Telemetry.Blazor.Services;

public sealed class TelemetryApiService(HttpClient http, AuthenticationStateProvider authState, IConfiguration config)
{
    private List<TelemetryReading> _lastData = [];
    public IReadOnlyList<TelemetryReading> CachedData => _lastData;

    private async Task<HttpRequestMessage> RequestAsync(HttpMethod method, string relativeUrl)
    {
        var user = (await authState.GetAuthenticationStateAsync()).User;
        if (user.Identity?.IsAuthenticated != true)
            throw new UnauthorizedAccessException("Sign in first");
        var request = new HttpRequestMessage(method, relativeUrl);
        if (!config.GetValue<bool>("DemoMode"))
        {
            var token = user.FindFirst("server_access_token")?.Value;
            if (string.IsNullOrEmpty(token))
                throw new UnauthorizedAccessException("Access token missing; please sign in again");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return request;
    }

    public async Task<List<TelemetryReading>> LoadAsync(string range, CancellationToken ct = default)
    {
        using var request = await RequestAsync(HttpMethod.Get, "telemetry");
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var rows = await response.Content.ReadFromJsonAsync<List<TelemetryReading>>(cancellationToken: ct) ?? [];
        _lastData = rows.OrderBy(r => r.Time).ToList();
        return FilterCached(range);
    }

    // Filter cashe
    public List<TelemetryReading> FilterCached(string range)
    {
        var cutoff = DateTime.UtcNow.Add(range switch
        {
            "hour" => TimeSpan.FromHours(-1),
            "day" => TimeSpan.FromDays(-1),
            _ => TimeSpan.FromDays(-7)
        });
        return _lastData.Where(r => r.Time.ToUniversalTime() >= cutoff).ToList();
    }

    //Servo
    public async Task SetServoAsync(bool enabled, CancellationToken ct = default)
    {
        using var request = await RequestAsync(HttpMethod.Post, $"servo?state={(enabled ? "on" : "off")}");
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}
