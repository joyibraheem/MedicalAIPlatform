# Docker Setup Summary

Configuration aligned with the current **ASP.NET Core 9** codebase and **GitHub-safe** secret handling.

## Committed Docker files

| File | Purpose |
|------|---------|
| `Dockerfile` | Multi-stage .NET 9 build |
| `docker-compose.yml` | SQL Server + web app (no hardcoded secrets) |
| `.dockerignore` | Build exclusions |
| `.env.example` | Secret template — **safe to commit** |
| `MedicalAIPlatform/appsettings.Production.example.json` | Production config template |
| `QUICK_START.md` / `DOCKER_README.md` | Team documentation |

## Gitignored (never push)

| File | Purpose |
|------|---------|
| `.env` | Real passwords for local Docker |
| `appsettings.Production.json` | Local production overrides |
| `appsettings.Development.json` | Local dev secrets |
| `bin/`, `obj/`, `_build_verify/` | Build artifacts |

## Before first Docker run

```bash
cp .env.example .env
docker compose up --build
```

## Stack

```text
.env (local secrets)
    ↓
docker compose up --build
    ├── sqlserver (health check → ready)
    └── webapp (migrations + DOCKER_DEMO_SEED → http://localhost:8080)
```

## Key environment variables

| Variable | Where set | Purpose |
|----------|-----------|---------|
| `MSSQL_SA_PASSWORD` | `.env` | SQL Server SA password |
| `MEDICALAI_DEV_ADMIN_PASSWORD` | `.env` | Demo admin password |
| `MEDICALAI_DEV_DOCTOR_PASSWORD` | `.env` | Demo doctor password |
| `DOCKER_DEMO_SEED` | compose | Creates demo users |
| `DOTNET_RUNNING_IN_CONTAINER` | compose | HTTP cookies + no HTTPS redirect |

## Demo accounts

- **Admin:** `admin@medicalai.com` + password from `.env`
- **Doctor:** `doctor@medicalai.com` + password from `.env`

## Commands

```bash
docker compose up --build              # normal run
docker compose down                    # stop, keep DB
docker compose down && docker compose up --build   # rebuild after code changes
docker compose down -v && docker compose up --build   # reset database
```

## Limitations

- CheXNet, BioBERT, and Ollama are **not** in Docker
- Demo passwords in `.env.example` are placeholders — change for your environment
- First login may require password change

## PR checklist

- [ ] No `.env` in commit
- [ ] No real passwords in `docker-compose.yml`
- [ ] `appsettings.Production.json` not staged
- [ ] Copied `.env.example` documented for new contributors
