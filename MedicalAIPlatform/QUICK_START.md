# Quick Start — Medical AI Platform (Docker)

## Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) installed and running
- At least 4 GB RAM free (SQL Server needs ~2 GB)

## Where the Docker files live

Run all commands from the folder that contains `docker-compose.yml`:

```text
MedicalAIPlatform/
├── .env.example          ← copy to .env (required)
├── docker-compose.yml
├── Dockerfile
├── .dockerignore
└── MedicalAIPlatform/    ← ASP.NET Core project
    └── MedicalAIPlatform.csproj
```

## One-time setup

```bash
# From the folder containing docker-compose.yml
cp .env.example .env        # Linux/macOS
# copy .env.example .env    # Windows CMD
# Copy-Item .env.example .env   # Windows PowerShell
```

Edit `.env` and set your local passwords (defaults in `.env.example` work for local demo).

## Run

```bash
docker compose up --build
```

Wait until logs show the web app listening on port **8080**, then open:

**http://localhost:8080**

## Demo login

After first run with `DOCKER_DEMO_SEED=true`, use the emails below with the passwords **you set in `.env`**:

| Role   | Email                 |
|--------|-----------------------|
| Admin  | admin@medicalai.com   |
| Doctor | doctor@medicalai.com  |

On first login you may be asked to change the password.

## Stop

```bash
docker compose down
```

## Rebuild after code changes

```bash
docker compose down
docker compose up --build
```

## Full reset (delete database)

```bash
docker compose down -v
docker compose up --build
```

## Known limitations

- **CheXNet / BioBERT / Ollama** are not included in Docker. AI analytics, chat, and model linking need those services on the host or as extra compose services.
- Core UI, auth, patients, CT viewer shell, reports, and admin features work without Python/Ollama.

See [DOCKER_README.md](./DOCKER_README.md) for troubleshooting.
