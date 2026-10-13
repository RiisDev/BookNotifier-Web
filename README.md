# BookNotifier Web

A self-hosted dashboard for [BookNotifier](https://github.com/RiisDev/BookNotifier) — tracks new chapters and books across GoodReads, ScribbleHub, RoyalRoad, AO3, and Literotica, predicts release dates, and shows everything in a MudBlazor dashboard with an arr-stack-style dark UI.

Built with Blazor Server (.NET 10) + MudBlazor 9.5. Runs the BookNotifier CLI as a managed subprocess, ingests its JSON output into SQLite, and adds a calendar view, release predictions, and optional Amazon pre-order lookups on top.

## Requirements

- Docker and Docker Compose (recommended), **or**
- .NET 10 SDK + a built copy of the [BookNotifier CLI](https://github.com/RiisDev/BookNotifier) for local development

## Quick start (Docker)

```bash
git clone https://github.com/RiisDev/BookNotifier-Web.git
cd BookNotifier-Web
docker compose up -d
```

Then open `http://localhost:8080` and complete the setup wizard.

`docker-compose.yml` also starts [FlareSolverr](https://github.com/FlareSolverr/FlareSolverr), required for ScribbleHub, AO3 and RoyalRoad (all Cloudflare-protected). The setup wizard auto-detects it at `http://flaresolverr:8191/v1`.

Data persists in two named volumes:

- `booknotifier_data` — SQLite database, DataProtection keys
- `booknotifier_cli_data` — CLI's own JSON caches

## Setup wizard

On first run, the app walks you through:

1. Enabling the platforms you want tracked
2. Per-platform credentials/IDs (GoodReads shelf, ScribbleHub/RoyalRoad user ID, AO3 username, Literotica login) and the FlareSolverr URL (required for ScribbleHub, AO3 and RoyalRoad — the Settings page warns if it's missing and one of them is enabled)
3. A Discord (or compatible) webhook URL for notifications
4. Optionally enabling Amazon pre-order lookups and the calendar view

You can re-run a check manually from the Setup page ("Run Notifier Now") instead of waiting for the next scheduled interval.

## Local development

```bash
dotnet restore
dotnet run
```

The app needs a built BookNotifier CLI to actually fetch data. It looks for it at, in order:

1. `BOOKNOTIFIER_CLI_PATH` environment variable
2. `./cli` next to the app (the Docker image layout)
3. `../BookNotifier/bin/{Debug,Release}/net10.0` relative to a parent directory (sibling repo checkout)

Without a CLI, background fetches fail gracefully but the dashboard won't show real data.

The CLI's data folder (`data/`) must be persistent: besides the JSON caches it holds `schedule.json` (when each notifier last ran, so restarts don't reset the check interval) and `goodreads.fulllist` (one-time GoodReads backfill marker). Don't wipe it when upgrading. Daily logs are written to `data/log-yyyy-MM-dd.txt` (3 days kept).

In `DEBUG` builds the app seeds mock data automatically so you can develop the UI without a live CLI or credentials. Force this in any build with `--mock`.

## Configuration

Settings are managed entirely through the web UI (Setup/Settings pages) and stored in SQLite — no manual config file editing required. Internally, saved settings are translated into the environment variables the BookNotifier CLI expects; see `Services/CliEnv.cs` for the mapping.

`appsettings.json` only controls ASP.NET Core logging levels and allowed hosts.

## Architecture notes

- `CliRunnerService` launches the BookNotifier CLI as a subprocess, streams its logs into the database, and polls its JSON output files (`goodreads.json`, `scribblehub.json`, etc.) to ingest new books/chapters. Series positions in `goodreads.json` can be decimals (e.g. 2.5), and `goodreads.json` now holds each author's full book list, so the library is larger than with older CLI versions. Keep the Web and CLI images on matching versions.
- `DatabaseService` owns the SQLite schema and also estimates release cadence (fixed-interval or weekday-pattern) from chapter history for the calendar/prediction views.
- `AmazonService` optionally looks up pre-order listings for newly tracked GoodReads books.
- `SettingsService` is the single source of truth for app settings, persisted to SQLite.

## AI disclosure

Parts of this project (code, and this README) were written with AI assistance (Claude Code). All changes are reviewed before merging.

## License

See the [BookNotifier](https://github.com/RiisDev/BookNotifier) CLI project for licensing of the scraping backend. This repository's license applies to the web dashboard only.
