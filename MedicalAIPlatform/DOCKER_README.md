# Docker Setup Guide for Medical AI Platform

This guide will help you run the Medical AI Platform using Docker. **You only need Docker installed** - no need to install .NET SDK, SQL Server, or any other dependencies.

## Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) installed and running
- At least 4GB of available RAM (for SQL Server)

## Quick Start (2 Commands)

### Option 1: Using Docker Compose (Recommended)

```bash
# 1. Build and start all services (SQL Server + Web App)
docker-compose up --build

# 2. Open your browser and navigate to:
# http://localhost:8080
```

That's it! The application will be available at `http://localhost:8080`

### Option 2: Manual Docker Commands

If you prefer to run commands separately:

```bash
# 1. Build the Docker image
docker build -t medicalai-platform .

# 2. Run SQL Server container
docker run -d --name medicalai-sqlserver \
  -e ACCEPT_EULA=Y \
  -e SA_PASSWORD=YourStrong@Passw0rd \
  -e MSSQL_PID=Developer \
  -p 1433:1433 \
  mcr.microsoft.com/mssql/server:2022-latest

# 3. Wait 30 seconds for SQL Server to start, then run the web app
docker run -d --name medicalai-webapp \
  -p 8080:8080 \
  --link medicalai-sqlserver:sqlserver \
  -e ConnectionStrings__DefaultConnection="Server=sqlserver;Database=MedicalAIPlatformDb;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True;MultipleActiveResultSets=true" \
  medicalai-platform

# 4. Open browser: http://localhost:8080
```

## Stopping the Application

```bash
# Stop all containers
docker-compose down

# Stop and remove volumes (clears database)
docker-compose down -v
```

## Default Login Credentials

After the first run, you can log in with:

- **Admin Account:**
  - Email: `admin@medicalai.com`
  - Password: `Admin@123`

- **Doctor Account:**
  - Email: `doctor@medicalai.com`
  - Password: `Doctor@123`

## Troubleshooting

### Port Already in Use

If port 8080 is already in use, modify `docker-compose.yml`:

```yaml
ports:
  - "8080:8080"  # Change 8080 to any available port (e.g., "5000:8080")
```

Then access the app at `http://localhost:5000`

### Database Connection Issues

If you see database connection errors:

1. Wait a bit longer - SQL Server takes 30-60 seconds to start
2. Check SQL Server logs: `docker logs medicalai-sqlserver`
3. Restart containers: `docker-compose restart`

### Viewing Logs

```bash
# View web app logs
docker logs medicalai-webapp

# View SQL Server logs
docker logs medicalai-sqlserver

# Follow logs in real-time
docker logs -f medicalai-webapp
```

### Rebuilding After Code Changes

```bash
# Rebuild and restart
docker-compose up --build --force-recreate
```

## Project Structure

```
MedicalAIPlatform/
├── Dockerfile                 # Docker image definition
├── docker-compose.yml         # Multi-container setup
├── .dockerignore             # Files to exclude from Docker build
├── DOCKER_README.md          # This file
└── MedicalAIPlatform/
    └── MedicalAIPlatform.csproj
```

## What's Included

- **SQL Server 2022**: Database server running in a container
- **ASP.NET Core 9.0**: Web application containerized
- **Automatic Database Migration**: Database is created automatically on first run
- **Persistent Storage**: Database data persists in a Docker volume

## Notes

- The database password is set to `YourStrong@Passw0rd` (change in `docker-compose.yml` for production)
- The application runs on HTTP (port 8080) inside Docker
- All data is stored in Docker volumes and persists between container restarts
- To completely reset: `docker-compose down -v` (removes all data)

## Support

If you encounter any issues, check the logs using the commands above or contact the development team.
