# Stage 1: Base runtime environment
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# Stage 2: SDK build environment
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies first (utilizes Docker layer caching)
COPY ["FhirPatientApp.csproj", "./"]
RUN dotnet restore "./FhirPatientApp.csproj"

# Copy the remaining project files and build
COPY . .
RUN dotnet build "FhirPatientApp.csproj" -c Release -o /app/build

# Stage 3: Publish output binaries
FROM build AS publish
RUN dotnet publish "FhirPatientApp.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 4: Final runtime image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Run as an unprivileged user (default in .NET 8 container images)
USER app

ENTRYPOINT ["dotnet", "FhirPatientApp.dll"]