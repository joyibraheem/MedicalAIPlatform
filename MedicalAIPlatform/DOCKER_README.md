# Docker Guide — Medical AI Platform

Run the ASP.NET Core 9 application with SQL Server using Docker Compose. No local .NET SDK or SQL Server install required.

## File locations

| File | Purpose |
|------|---------|
| `.env.example` | Template for secrets (copy to `.env`) |
| `.env` | Local secrets (**gitignored** — do not commit) |
| `Dockerfile` | Multi-stage build for the web app |
| `docker-compose.yml` | SQL Server + web app stack |
| `.dockerignore` | Build context exclusions |
| `MedicalAIPlatform/appsettings.Production.example.json` | Production config template (no secrets) |

Run all `docker compose` commands from the folder containing `docker-compose.yml`.

## First-time setup

```bash
cp .env.example .env
# Edit .env and set MSSQL_SA_PASSWORD and demo account passwords
docker compose up --build
```

Open **http://localhost:8080**

## Architecture

```text
┌─────────────────────┐     ┌──────────────────────────────┐
│  medicalai-webapp   │────▶│  medicalai-sqlserver         │
│  ASP.NET Core 9     │     │  SQL Server 2022             │
│  http://localhost:  │     │  volume: sqlserver_data      │
│       8080          │     └──────────────────────────────┘
└─────────────────────┘
```

### Included in Docker

- Web app (Identity, SignalR, fo-dicom, CT viewer, reports, admin)
- SQL Server with EF Core migrations on startup
- Demo users when `DOCKER_DEMO_SEED=true` (passwords from `.env`)

### Not included (disabled in container)

| Service | Port | Flag |
|---------|------|------|
| CheXNet / LungAI | 8000 | `CHEXNET_API_AUTOSTART=false` |
| BioBERT | 8001 | `BIOBERT_API_AUTOSTART=false` |
| Ollama chat | 11434 | not bundled |

## Environment variables

Set in `.env` (see `.env.example`):

| Variable | Purpose |
|----------|---------|
| `MSSQL_SA_PASSWORD` | SQL Server SA password |
| `MEDICALAI_DEV_ADMIN_PASSWORD` | Demo admin password |
| `MEDICALAI_DEV_DOCTOR_PASSWORD` | Demo doctor password |

Compose also sets (no secrets):

| Variable | Value |
|----------|-------|
| `ASPNETCORE_URLS` | `http://+:8080` |
| `DOTNET_RUNNING_IN_CONTAINER` | `true` |
| `DOCKER_DEMO_SEED` | `true` |

Demo account emails: `admin@medicalai.com`, `doctor@medicalai.com`

## Commands

```bash
# Build and run
docker compose up --build

# Stop (keep database)
docker compose down

# Rebuild after code changes
docker compose down && docker compose up --build

# Full reset (wipe database)
docker compose down -v && docker compose up --build

# Logs
docker logs medicalai-webapp
docker logs medicalai-sqlserver
```

## Ports

| Service | Host | Container |
|---------|------|-----------|
| Web app | 8080 | 8080 |
| SQL Server | 1433 | 1433 |

Change host port in `docker-compose.yml` if 8080 is in use:

```yaml
ports:
  - "5000:8080"   # open http://localhost:5000
```

## Startup sequence

1. SQL Server starts and passes health check (~30–90 s first run)
2. Web app runs EF migrations and seeds roles/users
3. App listens on **http://localhost:8080** (HTTP only — no TLS in container)

## Troubleshooting

| Issue | Action |
|-------|--------|
| `Set MSSQL_SA_PASSWORD in .env` | Copy `.env.example` to `.env` |
| Port in use | Change `8080:8080` mapping or stop conflicting app |
| Login fails | Use `http://` not `https://` |
| AI features fail | Expected without host Python/Ollama services |
| Stuck starting | `docker compose ps` and check SQL logs |

## Security (team / GitHub)

- **Never commit `.env`** — it is in `.gitignore`
- Commit **`.env.example`** only (placeholder values)
- `appsettings.Production.json` is gitignored; use `appsettings.Production.example.json` as template
- Default `.env.example` passwords are for **local Docker demo only**

## Manual image build

```bash
docker build -t medicalai-platform .
```

Requires a separate SQL Server instance or compose stack for the database.
