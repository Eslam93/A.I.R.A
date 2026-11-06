# A.I.R.A Quickstart Without API Keys

This guide is for someone who just cloned the repository and wants to run A.I.R.A locally without configuring any API keys.

The important default behavior is:

- the app runs successfully with no secrets
- report generation works out of the box
- the app automatically falls back to local mock mode
- live providers are disabled
- report chat is disabled
- autonomous mode should not be used until OpenAI Responses is configured

## What You Need

Install:

- .NET 10 SDK
- Git

Optional but useful:

- Docker Desktop, if you want to run the container version
- Visual Studio 2026, or VS Code with the C# Dev Kit, if you want an IDE (Visual Studio 2022 does not support .NET 10)

## 1. Clone the Repository

```bash
git clone <your-repo-url>
cd A.I.R.A
```

If the repo name on disk is different, use that folder name instead.

## 2. Confirm the Default No-Key Behavior

You do not need to edit `appsettings.json`.

The default configuration already supports a no-key local run because:

- `ResearchJobs:Mode` defaults to `Auto`
- `OpenAiResponses:Enabled` defaults to `false`
- live providers default to disabled

In that state:

- `Auto` resolves to `Mock`
- the standard report pipeline works
- seeded mock data is used
- autonomous mode is not available
- report chat is not available

## 3. Restore, Build, and Run

From the repository root:

```bash
dotnet restore
dotnet build Aira.sln
dotnet run --project Aira.Api --launch-profile http
```

Why `--launch-profile http`:

- it avoids HTTPS certificate issues for a quick local run
- it uses the built-in HTTP profile from `launchSettings.json`

Default local URL:

- `http://localhost:5214`

## 4. Open the App

Open:

- `http://localhost:5214/`

You can also open Swagger at:

- `http://localhost:5214/swagger`

## 5. Generate a Report

On the home page:

1. Search for a company or ticker
2. Pick a resolved company
3. Keep the mode set to `Standard`
4. Click `Generate Report`

Do not select `Autonomous` in the no-key setup.

Expected result:

- the report runs successfully
- progress updates appear while the job is processing
- the completed report loads using seeded mock data

## 6. What Should Work Without Keys

These features should work immediately:

- health endpoint
- Swagger
- ticker search
- standard async report generation
- status polling
- result retrieval
- built-in UI
- trace/progress timeline
- machine-readable result JSON

## 7. What Will Not Work Without Keys

These features require configuration and are expected to be unavailable:

- live provider data from SEC, Twelve Data, and Alpha Vantage
- report chat
- autonomous coordinator/specialist mode

Expected behavior without keys:

- `Standard` mode works
- `Autonomous` mode will fail because OpenAI Responses is not configured
- `Chat with the report` will stay disabled
- provider provenance in reports will show mock providers

## 8. How to Verify the No-Key Run

### Health

Open:

- `http://localhost:5214/health`

Expected response:

```json
{
  "status": "Healthy"
}
```

### Submit a Report Through the API

Example request:

```bash
curl -X POST http://localhost:5214/api/v1/research-jobs \
  -H "Content-Type: application/json" \
  -d "{\"identifier\":\"AAPL\",\"researchMode\":\"Standard\"}"
```

Expected behavior:

- returns `202 Accepted`
- includes a `jobId`

Then poll:

```bash
curl http://localhost:5214/api/v1/research-jobs/<jobId>
curl http://localhost:5214/api/v1/research-jobs/<jobId>/result
```

## 9. How to Run the Tests

From the repository root:

```bash
dotnet test Aira.sln
```

This validates the no-key default path too, because the test suite runs with mock-oriented defaults unless a test explicitly overrides configuration.

## 10. Common Reviewer Notes

If you are evaluating the project quickly, this is the simplest path:

1. clone the repo
2. run `dotnet run --project Aira.Api --launch-profile http`
3. open `http://localhost:5214/`
4. choose `Standard`
5. generate a report for `AAPL`
6. inspect the trace, provenance, and final JSON

That is the intended no-key demo path.

## 11. If the App Does Not Start

### Port already in use

Run with a different URL:

```bash
ASPNETCORE_URLS=http://localhost:5087 dotnet run --project Aira.Api --no-launch-profile
```

On PowerShell:

```powershell
$env:ASPNETCORE_URLS = "http://localhost:5087"
dotnet run --project Aira.Api --no-launch-profile
```

### Build issues

Make sure:

- you are using the .NET 10 SDK
- you are running commands from the repository root

## 12. When You Want More Than the No-Key Path

### To enable live data

Configure:

- `SecEdgar`
- `TwelveData`
- `AlphaVantage`

### To enable report chat

Configure:

- `OpenAiResponses:Enabled=true`
- `OpenAiResponses:ApiKey`
- `OpenAiResponses:ChatModel`

### To enable autonomous mode

Configure:

- `OpenAiResponses:Enabled=true`
- `OpenAiResponses:ApiKey`
- `OpenAiResponses:CoordinatorModel`
- `OpenAiResponses:SpecialistModel`

Until that is done, stay on:

- `Standard`

## Bottom Line

For a fresh GitHub clone with no keys:

- run the app
- use `Standard`
- expect mock-backed reports
- expect chat and autonomous mode to remain disabled

That is the correct and supported zero-secret local path.
