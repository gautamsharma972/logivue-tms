# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig Tms.slnx ./
COPY src ./src
RUN dotnet publish src/Tms.Api/Tms.Api.csproj -c Release -o /app --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
# Run as a non-root user; uploaded files live on a mounted volume.
RUN mkdir -p /data/storage && chown -R app:app /data
USER app
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Storage__RootPath=/data/storage
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --retries=3 CMD ["dotnet", "Tms.Api.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "Tms.Api.dll"]
