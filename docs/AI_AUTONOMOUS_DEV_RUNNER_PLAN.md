## Recommended Technical Direction

Use .NET 8 as the main stack.

Preferred architecture:
- One ASP.NET Core application that hosts:
  - Local Web API
  - Local Web Dashboard
  - Background scheduler using HostedService
- The app must be installable/runnable as a Windows Service.
- Use SQLite for local storage.
- Use EF Core if useful, but keep the schema simple.
- Bind the web server to localhost only by default.
- No login/authentication required for MVP.
- Use plain Razor Pages, MVC views, or a lightweight frontend inside the same ASP.NET Core app for the local dashboard.
- Avoid Angular for MVP unless absolutely necessary, because this is a local admin tool and should be simple to build, run, and maintain.
- Design the provider layer with interfaces:
  - Codex provider
  - Claude provider
- Codex CLI must be supported from the first MVP.
- Claude CLI must also be supported from the first MVP.