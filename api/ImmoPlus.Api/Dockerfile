# Image de développement : compilation + dotnet watch pour le hot-reload.
# Pas destinée à la production (voir README pour le déploiement VPS actuel).
FROM mcr.microsoft.com/dotnet/sdk:10.0

WORKDIR /src

# sqlite3 pour inspecter/modifier la base depuis le conteneur (docker compose exec api sqlite3 immoplus.db).
# Ne jamais écrire dans immoplus.db depuis macOS pendant que l'API tourne : le mode WAL de SQLite
# ne se synchronise pas à travers le montage Docker et la base se corrompt.
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
