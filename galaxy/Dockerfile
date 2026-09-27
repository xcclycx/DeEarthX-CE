FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props .
COPY Galaxy.slnx .
COPY Galaxy.csproj .
COPY Program.cs .
COPY appsettings.json .
COPY appsettings.Development.json .
COPY src/Galaxy.Core/ src/Galaxy.Core/
COPY src/Galaxy.Data/ src/Galaxy.Data/
COPY src/Galaxy.Auth/ src/Galaxy.Auth/
COPY src/Galaxy.Mods/ src/Galaxy.Mods/
COPY src/Galaxy.OAuth/ src/Galaxy.OAuth/
RUN dotnet publish Galaxy.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app
COPY --from=build /app/publish .
COPY wwwroot/ wwwroot/
ENV ASPNETCORE_ENVIRONMENT=Production
ENV GALAXY_PORT=9800
EXPOSE 9800
ENTRYPOINT ["dotnet", "Galaxy.dll"]
