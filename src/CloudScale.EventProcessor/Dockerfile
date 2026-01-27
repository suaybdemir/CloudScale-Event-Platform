FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/CloudScale.EventProcessor/CloudScale.EventProcessor.csproj", "src/CloudScale.EventProcessor/"]
COPY ["src/CloudScale.Shared/CloudScale.Shared.csproj", "src/CloudScale.Shared/"]
COPY ["src/CloudScale.Data/CloudScale.Data.csproj", "src/CloudScale.Data/"]
RUN dotnet restore "src/CloudScale.EventProcessor/CloudScale.EventProcessor.csproj"
COPY . .
WORKDIR "/src/src/CloudScale.EventProcessor"
RUN dotnet build "CloudScale.EventProcessor.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "CloudScale.EventProcessor.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CloudScale.EventProcessor.dll"]
