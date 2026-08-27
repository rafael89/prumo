FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore Prumo.sln
RUN dotnet publish src/Prumo.Api/Prumo.Api.csproj -c Release -o /app/api --no-restore
RUN dotnet publish src/Prumo.Worker/Prumo.Worker.csproj -c Release -o /app/worker --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app
COPY --from=build /app/api .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Prumo.Api.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS worker
WORKDIR /app
COPY --from=build /app/worker .
ENTRYPOINT ["dotnet", "Prumo.Worker.dll"]
