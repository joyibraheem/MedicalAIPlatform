# syntax=docker/dockerfile:1

# Stage 1: Restore and build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY MedicalAIPlatform/MedicalAIPlatform.csproj MedicalAIPlatform/
RUN dotnet restore MedicalAIPlatform/MedicalAIPlatform.csproj

COPY MedicalAIPlatform/ MedicalAIPlatform/
WORKDIR /src/MedicalAIPlatform
RUN dotnet publish MedicalAIPlatform.csproj -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# fo-dicom native codecs and QuestPDF may load native libraries at runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

RUN adduser --disabled-password --gecos '' appuser \
    && mkdir -p /tmp/MedicalAiPlatform \
    && chown -R appuser /app /tmp/MedicalAiPlatform

COPY --from=build /app/publish .

USER appuser

EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
    CMD curl -fsS http://127.0.0.1:8080/ || exit 1

ENTRYPOINT ["dotnet", "MedicalAIPlatform.dll"]
