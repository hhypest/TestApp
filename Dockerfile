FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
# Суффикс предрелиза приходит из релизного пайплайна: VersionPrefix живёт в Directory.Build.props,
# а тег v1.0.0-rc.1 добавляет к нему "rc.1". Пустое значение даёт релизную версию без суффикса.
ARG VERSION_SUFFIX=""
WORKDIR /src
COPY . .
RUN dotnet restore TestApp.slnx
RUN dotnet publish src/TestApp.Api/TestApp.Api.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    -p:VersionSuffix="$VERSION_SUFFIX" \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "TestApp.Api.dll"]
