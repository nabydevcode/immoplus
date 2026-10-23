# Image de développement : compilation + dotnet watch pour le hot-reload.
# Pas destinée à la production (voir README pour le déploiement VPS actuel).
FROM mcr.microsoft.com/dotnet/sdk:10.0

WORKDIR /src

# La base est PostgreSQL (service compose « postgres » ; psql : docker compose exec postgres psql -U immoplus immoplus).
# sqlite3 reste installé pour lire l'ancienne base SQLite conservée pour mémoire (immoplus.db, à ne jamais modifier) :
# copie cohérente avec docker compose exec -T api sqlite3 immoplus.db ".backup '/src/copie.db'".
RUN apt-get update && apt-get install -y --no-install-recommends sqlite3 && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:5107 \
    ASPNETCORE_ENVIRONMENT=Development \
    DOTNET_USE_POLLING_FILE_WATCHER=true \
    DOTNET_RUNNING_IN_CONTAINER=true

COPY ImmoPlus.Api.csproj ./
RUN dotnet restore

COPY . .

EXPOSE 5107

CMD ["dotnet", "watch", "run", "--no-launch-profile"]
