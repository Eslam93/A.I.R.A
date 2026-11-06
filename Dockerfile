FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Aira.sln global.json ./
COPY Aira.Api/Aira.Api.csproj Aira.Api/
COPY Aira.Application/Aira.Application.csproj Aira.Application/
COPY Aira.Domain/Aira.Domain.csproj Aira.Domain/
COPY Aira.Infrastructure/Aira.Infrastructure.csproj Aira.Infrastructure/
COPY Aira.Tests.Unit/Aira.Tests.Unit.csproj Aira.Tests.Unit/
COPY Aira.Tests.Integration/Aira.Tests.Integration.csproj Aira.Tests.Integration/
RUN dotnet restore Aira.sln

COPY . .
RUN dotnet publish Aira.Api/Aira.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080

COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Aira.Api.dll"]
