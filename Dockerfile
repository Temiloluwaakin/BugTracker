FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files
COPY ["BugTracker.API/BugTracker.API.csproj", "BugTracker.API/"]
COPY ["BugTracker.Data/BugTracker.Data.csproj", "BugTracker.Data/"]
COPY ["BugTracker.Services/BugTracker.Services.csproj", "BugTracker.Services/"]

RUN dotnet restore "BugTracker.API/BugTracker.API.csproj"

# Copy everything else
COPY . .

WORKDIR "/src/BugTracker.API"
RUN dotnet build "BugTracker.API.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "BugTracker.API.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BugTracker.API.dll"]