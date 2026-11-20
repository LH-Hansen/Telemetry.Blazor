using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Components.Authorization;
using Telemetry.Blazor.Components;
using Telemetry.Blazor.Services;

var builder = WebApplication.CreateBuilder(args);
var demo = builder.Configuration.GetValue<bool>("DemoMode");
if (demo && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException("DemoMode is permitted only in Development.");

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Read", p => p.RequireAuthenticatedUser().RequireClaim("permissions", "read:telemetry"));
    options.AddPolicy("Write", p => p.RequireAuthenticatedUser().RequireClaim("permissions", "write:servo"));
});
builder.Services.AddHttpClient<TelemetryApiService>(client =>
    client.BaseAddress = new Uri(builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5087/"));

var auth = builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options => { options.LoginPath = "/account/login"; options.AccessDeniedPath = "/account/denied"; });
if (!demo)
{
    auth.AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
    {
        var domain = builder.Configuration["Auth0:Domain"] ?? throw new InvalidOperationException("Auth0:Domain missing");
        options.Authority = $"https://{domain.TrimEnd('/')}/";
        options.ClientId = builder.Configuration["Auth0:ClientId"] ?? throw new InvalidOperationException("Auth0:ClientId missing");
        options.ClientSecret = builder.Configuration["Auth0:ClientSecret"] ?? throw new InvalidOperationException("Auth0:ClientSecret missing");
        options.ResponseType = "code";
        options.SaveTokens = true;
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.CallbackPath = "/signin-oidc";
        options.SignedOutCallbackPath = "/signout-callback-oidc";
        options.Events.OnRedirectToIdentityProvider = context =>
        {
            context.ProtocolMessage.SetParameter("audience", builder.Configuration["Auth0:Audience"] ?? "https://telemetry-api");
            return Task.CompletedTask;
        };
        options.Events.OnTicketReceived = context =>
        {
            var token = context.Properties?.GetTokenValue("access_token");
            if (!string.IsNullOrEmpty(token) && context.Principal?.Identity is ClaimsIdentity identity)
            {
                identity.AddClaim(new Claim("server_access_token", token));
                if (new JwtSecurityTokenHandler().CanReadToken(token))
                {
                    var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
                    foreach (var permission in jwt.Claims.Where(c => c.Type == "permissions"))
                        identity.AddClaim(new Claim("permissions", permission.Value));
                }
            }
            return Task.CompletedTask;
        };
    });
}

var app = builder.Build();
if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler("/error"); app.UseHsts(); }
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/account/login", (HttpContext ctx) =>
{
    if (demo) return Results.Redirect("/demo");
    return Results.Challenge(new AuthenticationProperties { RedirectUri = "/" }, [OpenIdConnectDefaults.AuthenticationScheme]);
});
app.MapGet("/account/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    if (demo) return Results.Redirect("/demo");
    return Results.SignOut(new AuthenticationProperties { RedirectUri = "/" }, [OpenIdConnectDefaults.AuthenticationScheme]);
});
app.MapGet("/account/denied", () => Results.Content("Access denied (403).", "text/plain"));

if (demo)
{
    app.MapGet("/demo/login/{profile}", async (string profile, HttpContext ctx) =>
    {
        string[] permissions = profile switch
        {
            "read" => ["read:telemetry"],
            "admin" => ["read:telemetry", "write:servo"],
            "none" => [],
            _ => throw new BadHttpRequestException("Unknown profile")
        };
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Name, $"Demo {profile}") }.Concat(permissions.Select(x => new Claim("permissions", x))),
            CookieAuthenticationDefaults.AuthenticationScheme);
        await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = false });
        return Results.Redirect("/");
    });
}
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
