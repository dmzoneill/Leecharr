# Leecharr Development Guide

## Prerequisites

- .NET 10 SDK
- Node.js 20+ and npm
- SQLite (bundled) or PostgreSQL (optional)

## Getting Started

```bash
# Clone
git clone https://github.com/dmzoneill/Leecher.git
cd Leecher

# Build backend
dotnet build src/Leecharr.sln

# Install frontend dependencies
cd src/Leecharr.Frontend
npm install
cd ../..

# Run (backend + dev server)
dotnet run --project src/NzbDrone.Console/Leecharr.Console.csproj
```

Application starts on `http://localhost:9899`.

## Project Layout

```
Leecher/
  docs/                     Architecture & protocol documentation
  src/
    NzbDrone.Common/        Shared infrastructure (DryIoc, logging, HTTP, disk)
    NzbDrone.Core/          Base Servarr framework (datastore, migrations, commands)
    Leecharr.Core/          Torrent engine, piece picker, async disk I/O, media enrichment
    NzbDrone.SignalR/       SignalR real-time messaging hub
    Leecharr.Http/          REST framework & middleware
    Leecharr.Api.V1/        API controllers & download client compatibility RPC
    NzbDrone.Host/          Kestrel web server startup pipeline
    NzbDrone.Console/       Console entry point
    Leecharr.Frontend/      React 18 SPA (TypeScript, Webpack 5 / Vite)
    Leecharr.Core.Test/     Unit tests (NUnit 4.6)
  Makefile                  Build and test automation
  Containerfile             Multi-stage container build
```

## Testing

```bash
# Run unit tests
dotnet test src/Leecharr.sln

# Run frontend tests
cd src/Leecharr.Frontend && npm test
```
