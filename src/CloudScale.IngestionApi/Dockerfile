FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/CloudScale.IngestionApi/CloudScale.IngestionApi.csproj", "src/CloudScale.IngestionApi/"]
COPY ["src/CloudScale.Shared/CloudScale.Shared.csproj", "src/CloudScale.Shared/"]
RUN dotnet restore "src/CloudScale.IngestionApi/CloudScale.IngestionApi.csproj"
COPY . .
WORKDIR "/src/src/CloudScale.IngestionApi"
RUN dotnet build "CloudScale.IngestionApi.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "CloudScale.IngestionApi.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CloudScale.IngestionApi.dll"]
