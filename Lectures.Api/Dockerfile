FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Lab5.sln", "./"]
COPY ["Lab5.Data/Lab5.Data.csproj", "Lab5.Data/"]
COPY ["Lab5.Tests/Lab5.Tests.csproj", "Lab5.Tests/"]
COPY ["Lectures.Api/Lectures.Api.csproj", "Lectures.Api/"]

RUN dotnet restore "Lab5.sln"

COPY . .
WORKDIR "/src/Lectures.Api"
RUN dotnet publish "Lectures.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Lectures.Api.dll"]
