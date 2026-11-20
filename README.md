# Telemetry Blazor Dashboard

A standalone **Blazor Web App** for viewing sensor telemetry and controlling a servo through an existing ASP.NET Core Web API.

This repository contains **only the frontend**. The API, MQTT broker, InfluxDB database and Arduino/ESP hardware are separate components from the earlier `MqttWebApiInfluxDB` project.

## Features

- Displays the latest **temperature, humidity, pressure and light** measurements.
- Shows four SVG-based graphs with **last hour, day or week** filtering.
- Refreshes telemetry approximately every **20 seconds** while the dashboard is open.
- Shows the measurement timestamp in local time (the Blazor server's local timezone).
- Provides **servo ON/OFF** controls through the existing API.
- Supports **Auth0 OpenID Connect login** and development-only demo users.
- Restricts dashboard and servo pages using `Read` and `Write` authorization policies.
- Displays navigation links according to the signed-in user's permissions.
- Retains the most recently retrieved measurements **in server memory** when an API request fails.

## Technology

| Component | Technology |
|---|---|
| Frontend | Blazor Web App, .NET 10 |
| Rendering | Interactive Server |
| Language | C# / Razor |
| Charts | Built-in SVG rendering (no chart package) |
| API communication | `HttpClient` with dependency injection |
| Authentication | Auth0 / OpenID Connect and cookie authentication |
| Authorization | Claims-based `Read` and `Write` policies |

NuGet dependency: `Microsoft.AspNetCore.Authentication.OpenIdConnect` **10.0.12**. Requires the .NET 10 SDK.

## Project structure

```text
Telemetry.Blazor/
├── Components/
│   ├── Layout/
│   │   ├── MainLayout.razor
│   │   └── NavMenu.razor
│   ├── Pages/
│   │   ├── Dashboard.razor      # Measurements, filtering and refresh
│   │   ├── TelemetryChart.razor # SVG charts
│   │   ├── Servo.razor          # Servo ON/OFF buttons
│   │   ├── Demo.razor           # Development test accounts
│   │   └── Home.razor
│   ├── Routes.razor
│   └── App.razor
├── Models/
│   └── TelemetryReading.cs
├── Services/
│   └── TelemetryApiService.cs # API calls and in-memory cache
├── Properties/
│   └── launchSettings.json
├── Program.cs                 # DI, authentication, authorization
├── appsettings.json
└── appsettings.Development.json
```

## Getting started

### Prerequisites

- Visual Studio 2026 or another IDE with the **.NET 10 SDK** installed.
- Your existing **`MqttWebApiInfluxDB` Web API** running separately if you want real telemetry and servo control.
- InfluxDB, MQTT and your device configured in that API project, if using physical hardware.

### 1. Configure the API URL

In `Telemetry.Blazor/appsettings.json`, set:

```json
{
  "ApiBaseUrl": "http://localhost:5087/"
}
```

Replace `5087` if your API uses another port. Since this is a **server-side** Blazor app, `localhost` refers to the machine running Blazor.

### 2. Run the frontend

From the repository root:

```powershell
dotnet restore Telemetry.Blazor/Telemetry.Blazor.csproj
dotnet run --project Telemetry.Blazor --launch-profile http
```

Open **http://localhost:5090/demo**.

In Visual Studio, open `Telemetry.Blazor.csproj`, restore NuGet packages and start the project with the `http` launch profile.

### 3. Sign in using a development demo account

`appsettings.Development.json` contains:

```json
{
  "DemoMode": true
}
```

Choose one of the test profiles:

| Demo user | Telemetry dashboard | Servo controls |
|---|---|---|
| Read-only | Yes | No |
| Read + Write | Yes | Yes |
| No permissions | No | No |

**Demo accounts simulate frontend permission checks only.** They do not generate JWT access tokens and **do not secure your existing API**. They also do not generate sensor readings: start the real API to display real measurements. Demo sign-in is blocked outside the Development environment.

## Existing Web API integration

The frontend uses these existing endpoints:

| Method | Endpoint | Purpose |
|---|---|---|
| `GET` | `/telemetry` | Retrieve historical sensor measurements |
| `POST` | `/servo?state=on` | Turn servo on |
| `POST` | `/servo?state=off` | Turn servo off |

Example response shape supported by the frontend:

```json
[
  {
    "time": "2026-11-20T09:00:00Z",
    "temperature": 22.4,
    "humidity": 48.2,
    "pressure": 1012.5,
    "light": 640,
    "servoState": 1
  }
]
```

The parser is case-insensitive. `pressure`, `light` and `servoState` are optional; missing pressure/light readings are shown as unavailable. The API must supply enough historical measurements for the hour/day/week graphs. The frontend filters the returned data by timestamp; it does **not** request a different time range from the API.

Data flow:

```text
Sensors → MQTT broker → Existing API/worker → InfluxDB
                                              ↑
Blazor dashboard ── GET /telemetry ────────────┘

Blazor servo page ── POST /servo?state=on|off → Existing API → MQTT → Servo
```

The Blazor server calls the API using `HttpClient`, so the browser does not call the API directly and usually does not need API-side CORS configuration for this integration.

## Auth0 configuration

For actual Auth0 sign-in, set `DemoMode` to `false` and configure the following in `appsettings.json` or through environment variables / .NET User Secrets:

```json
{
  "DemoMode": false,
  "Auth0": {
    "Domain": "YOUR_TENANT.eu.auth0.com",
    "Audience": "https://telemetry-api",
    "ClientId": "YOUR_CLIENT_ID",
    "ClientSecret": ""
  }
}
```

Create an **Auth0 Regular Web Application** and configure the callback URL `/signin-oidc` and logout callback `/signout-callback-oidc` using your frontend's full HTTPS origin. Create/register an Auth0 API whose identifier matches `Auth0:Audience`, with these permissions:

- `read:telemetry`
- `write:servo`

Enable role-based access control and permissions in access tokens in Auth0. Set the client secret using .NET User Secrets instead of committing it:

```powershell
dotnet user-secrets init --project Telemetry.Blazor
dotnet user-secrets set "Auth0:ClientSecret" "YOUR_SECRET" --project Telemetry.Blazor
```

The frontend uses an OpenID Connect login flow and forwards the access token as a Bearer token when Auth0 mode is enabled. The frontend's `Read` and `Write` policies control which pages and menu links are available.

### Important API security requirement

**Authentication in the frontend is not sufficient to secure the API.**

The separate existing Web API must independently:

1. Validate Auth0-issued JWT access tokens (issuer, audience, signature and expiry).
2. Require `read:telemetry` for the telemetry endpoint.
3. Require `write:servo` for the servo endpoint.
4. Return `401` for unauthenticated calls and `403` for authenticated calls without the required permission.

Those backend security changes are **not included in this frontend-only repository**. Until they are implemented, API security requirements from the assignment are not fully satisfied. Auth0 integration also needs live testing with a configured Auth0 tenant.

## Offline and network handling

If a telemetry request fails, the dashboard displays an error and attempts to show the last successfully retrieved measurements from its **in-memory cache**. This data is not persisted across server restarts, and an Interactive Server app cannot provide full offline functionality without additional work.

Servo requests are **not automatically retried**, avoiding duplicate commands after ambiguous network failures.

## Assignment checklist

| Requirement | Status |
|---|---|
| Blazor dashboard showing telemetry data | Implemented |
| Filter graphs by hour/day/week | Implemented (depends on API history) |
| Servo control through the existing API | Implemented in frontend; hardware test required |
| Authentication with Auth0 | Code/configuration present; requires Auth0 setup and testing |
| API only accessible to authenticated users | **Not completed in this repository**; requires API changes |
| Read permission on telemetry API | **Frontend policy only**; backend enforcement missing |
| Write permission on servo API | **Frontend policy only**; backend enforcement missing |
| Permission-based navigation | Implemented |
| Latest temperature/humidity and measurement time | Implemented |
| Dependency injection | Implemented |
| MVVM pattern | No separate ViewModel; Razor component state and a service are used |
| Last measurements available during network failure | Partially implemented; in-memory only |
| Robust against unstable connectivity | Partial; error display and retained data, no advanced retry/reconnection handling |
| README and GitHub submission | README provided; publishing remains to be done |
| Optional room/session selection | Not implemented |
| Optional temperature alarm | Not implemented |

## Known limitations

- The app depends on the separately running API for real data and servo operations.
- Its original archived API version had temperature/humidity data only; pressure/light support needs testing against the later API changes.
- Time conversion uses the **Blazor host's** local timezone, which may differ from the browser's timezone if hosted remotely.
- No persistent offline cache or optional room/alarm features.
- API-level authorization must be implemented separately.
- Compilation, Auth0 login and real-device integration still require local verification.

## References

- [ASP.NET Core Blazor documentation](https://learn.microsoft.com/en-us/aspnet/core/blazor/)
- [Blazor authentication and authorization](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/)
- [Auth0 documentation](https://auth0.com/docs)
