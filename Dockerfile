FROM mcr.microsoft.com/dotnet/sdk:8.0@sha256:6ba9644da07bb5b560a402acec9ca38711a0762969f1b61da261df5c9f6f85da AS build
WORKDIR /source
COPY global.json ./
COPY src/ ./src/
RUN dotnet restore src/FiapCloudGames.Catalog.Api/FiapCloudGames.Catalog.Api.csproj --locked-mode
RUN dotnet publish src/FiapCloudGames.Catalog.Api/FiapCloudGames.Catalog.Api.csproj -c Release --no-restore -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0@sha256:ea721098d6ea2894ff9bd8387821a1659d4765d6c014b8d648bb1da16f016f5a AS final
WORKDIR /app
COPY --from=build /app ./
USER $APP_UID
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080
ENTRYPOINT ["dotnet", "FiapCloudGames.Catalog.Api.dll"]
