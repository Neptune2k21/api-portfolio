# Build Native AOT : l'édition de liens a besoin de clang
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
RUN apt-get update \
    && apt-get install -y --no-install-recommends clang zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /src

# Restauration d'abord : la couche reste en cache tant que les dépendances ne changent pas
COPY Directory.Build.props ./
COPY src/Portfolio.Api/Portfolio.Api.csproj src/Portfolio.Api/
RUN dotnet restore src/Portfolio.Api/Portfolio.Api.csproj -r linux-x64

COPY src/ src/
RUN dotnet publish src/Portfolio.Api/Portfolio.Api.csproj -c Release -r linux-x64 --no-restore -o /app \
    && rm -f /app/*.dbg

# Image d'exécution sans shell ni gestionnaire de paquets, utilisateur non root.
# « extra » embarque tzdata, nécessaire pour Europe/Paris.
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled-extra
WORKDIR /app
COPY --from=build /app ./
ENTRYPOINT ["./Portfolio.Api"]
