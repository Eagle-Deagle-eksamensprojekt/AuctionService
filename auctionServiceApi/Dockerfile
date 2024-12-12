# Brug en .NET 8 SDK image som build image
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /app

# Kopiér .csproj filen og gendan afhængigheder
COPY *.csproj ./
RUN dotnet restore

# Kopiér resten af koden og publicér applikationen
COPY . . 
RUN dotnet publish -c Release -o /app/published-app

# Brug en runtime image til at køre applikationen
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
WORKDIR /app

# Kopiér de publicerede filer fra build step
COPY --from=build /app/published-app /app

# Kør applikationen
ENTRYPOINT ["dotnet", "auctionServiceApi.dll"]