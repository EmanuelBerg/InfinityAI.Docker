FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY ["InfinityAI.Docker/InfinityAI.Docker.csproj", "InfinityAI.Docker/"]
RUN dotnet restore "InfinityAI.Docker/InfinityAI.Docker.csproj"
COPY InfinityAI.Docker/ InfinityAI.Docker/
WORKDIR /src/InfinityAI.Docker
RUN dotnet publish "InfinityAI.Docker.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "InfinityAI.Docker.dll"]
