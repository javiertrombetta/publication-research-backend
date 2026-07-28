# syntax=docker/dockerfile:1

# ---------- Build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the project file first so `restore` is cached separately from source changes.
COPY PublicationSite.Api.csproj .
RUN dotnet restore PublicationSite.Api.csproj

COPY . .
RUN dotnet publish PublicationSite.Api.csproj -c Release -o /app/publish --no-restore

# ---------- Runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Run as a non-root user; own the dirs the app writes to at runtime
# (local file uploads, Serilog's rolling file log).
RUN useradd --uid 1001 --create-home appuser \
    && mkdir -p /app/App_Data/uploads /app/logs \
    && chown -R appuser:appuser /app

COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

USER appuser

# Render (and most PaaS targets) inject PORT at runtime; Program.cs reads it and binds
# Kestrel accordingly. 8080 is just the documented default when PORT isn't set.
EXPOSE 8080

# No Docker-level HEALTHCHECK: the aspnet runtime image ships without curl/wget, and
# Render already polls GET /health from outside the container to gate deploys/restarts
# (see render.yaml). Re-added here if that ever changes.

ENTRYPOINT ["dotnet", "PublicationSite.Api.dll"]
