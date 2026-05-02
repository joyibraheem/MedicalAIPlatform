# Docker Setup Summary

## Files Created

All Docker-related files have been created in the `MedicalAIPlatform` directory:

1. **Dockerfile** - Multi-stage build for ASP.NET Core 9.0 application
2. **docker-compose.yml** - Orchestrates SQL Server + Web App containers
3. **.dockerignore** - Excludes unnecessary files from Docker build
4. **appsettings.Production.json** - Production configuration for Docker
5. **DOCKER_README.md** - Comprehensive Docker guide
6. **QUICK_START.md** - Quick reference for instructors

## File Locations

```
MedicalAIPlatform/
├── Dockerfile                          ← Docker image definition
├── docker-compose.yml                  ← Multi-container setup
├── .dockerignore                      ← Build exclusions
├── DOCKER_README.md                   ← Full documentation
├── QUICK_START.md                     ← Quick reference
├── MedicalAIPlatform/
│   ├── appsettings.Production.json    ← Docker config
│   └── Program.cs                     ← Modified (HTTPS handling)
└── ...
```

## Key Changes Made

### 1. Program.cs
- Modified HTTPS redirection to skip in Docker containers
- Uses `DOTNET_RUNNING_IN_CONTAINER` environment variable

### 2. Docker Configuration
- **Port:** Application runs on port 8080 (mapped to host)
- **Database:** SQL Server 2022 in separate container
- **Environment:** Production mode with Docker-specific settings
- **API Services:** Python APIs disabled (set via environment variables)

## Docker Commands

### Build and Run (Recommended)
```bash
docker-compose up --build
```

### Build Image Only
```bash
docker build -t medicalai-platform .
```

### Run Container Only
```bash
docker run -p 8080:8080 medicalai-platform
```

### Stop Containers
```bash
docker-compose down
```

### Stop and Remove Data
```bash
docker-compose down -v
```

## Port Configuration

- **Web Application:** `http://localhost:8080`
- **SQL Server:** `localhost:1433` (internal use only)

## Database Configuration

- **Server:** `sqlserver` (container name)
- **Database:** `MedicalAIPlatformDb`
- **Username:** `sa`
- **Password:** `YourStrong@Passw0rd`
- **Auto-migration:** Enabled (runs on startup)

## Environment Variables

Set in `docker-compose.yml`:
- `ASPNETCORE_ENVIRONMENT=Production`
- `ASPNETCORE_URLS=http://+:8080`
- `CHEXNET_API_AUTOSTART=false`
- `BIOBERT_API_AUTOSTART=false`
- `DOTNET_RUNNING_IN_CONTAINER=true`

## Testing the Setup

1. Build and start: `docker-compose up --build`
2. Wait for "Now listening on: http://[::]:8080" message
3. Open browser: `http://localhost:8080`
4. Login with admin credentials
5. Verify dashboard loads correctly

## Troubleshooting

### Check Logs
```bash
docker logs medicalai-webapp
docker logs medicalai-sqlserver
```

### Check Container Status
```bash
docker ps
```

### Restart Services
```bash
docker-compose restart
```

### Rebuild Everything
```bash
docker-compose down -v
docker-compose up --build
```

## Notes for Instructors

- **No .NET SDK required** - Everything runs in Docker
- **No SQL Server installation** - Database runs in container
- **Persistent data** - Database data survives container restarts
- **Easy reset** - Run `docker-compose down -v` to start fresh
- **Port conflicts** - Change port mapping in `docker-compose.yml` if needed

## Security Notes

⚠️ **For Production:**
- Change SQL Server password in `docker-compose.yml`
- Use environment variables for sensitive data
- Enable HTTPS with proper certificates
- Review firewall rules

## Next Steps

1. Test the setup locally
2. Share `QUICK_START.md` with instructors
3. Provide Docker Desktop installation link if needed
4. Document any custom configuration requirements
