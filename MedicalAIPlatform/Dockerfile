# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project file first for better layer caching
COPY MedicalAIPlatform/MedicalAIPlatform.csproj MedicalAIPlatform/

# Restore dependencies
WORKDIR "/src/MedicalAIPlatform"
RUN dotnet restore MedicalAIPlatform.csproj

# Copy everything else and build
WORKDIR /src
COPY MedicalAIPlatform/ MedicalAIPlatform/
WORKDIR "/src/MedicalAIPlatform"
RUN dotnet build MedicalAIPlatform.csproj -c Release -o /app/build

# Stage 2: Publish
FROM build AS publish
RUN dotnet publish MedicalAIPlatform.csproj -c Release -o /app/publish /p:UseAppHost=false

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Create a non-root user
RUN adduser --disabled-password --gecos '' appuser && chown -R appuser /app
USER appuser

# Copy published app
COPY --from=publish /app/publish .

# Expose port
EXPOSE 8080
EXPOSE 8081

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Run the app
ENTRYPOINT ["dotnet", "MedicalAIPlatform.dll"]
