# ---------- Stage 1: build & publish ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy only the csproj first so NuGet restore is cached between builds
COPY SecretApi/SecretApi.csproj SecretApi/
RUN dotnet restore SecretApi/SecretApi.csproj

# Copy the rest of the source and publish a Release build
COPY SecretApi/ SecretApi/
RUN dotnet publish SecretApi/SecretApi.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ---------- Stage 2: runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# Kestrel must listen on all interfaces inside the container
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true

EXPOSE 8080

# NOTE: API_SECRET_KEY is deliberately NOT baked into the image.
# Supply it at run time:  docker run -e API_SECRET_KEY=... (or --env-file .env)

# Run as the non-root user that ships with the .NET 8 images
USER $APP_UID

ENTRYPOINT ["dotnet", "SecretApi.dll"]
