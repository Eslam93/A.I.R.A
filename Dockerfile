FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Proofsheet.slnx global.json ./
COPY Proofsheet.Api/Proofsheet.Api.csproj Proofsheet.Api/
COPY Proofsheet.Application/Proofsheet.Application.csproj Proofsheet.Application/
COPY Proofsheet.Domain/Proofsheet.Domain.csproj Proofsheet.Domain/
COPY Proofsheet.Infrastructure/Proofsheet.Infrastructure.csproj Proofsheet.Infrastructure/
COPY Proofsheet.Tests.Unit/Proofsheet.Tests.Unit.csproj Proofsheet.Tests.Unit/
COPY Proofsheet.Tests.Integration/Proofsheet.Tests.Integration.csproj Proofsheet.Tests.Integration/
RUN dotnet restore Proofsheet.slnx

COPY . .
RUN dotnet publish Proofsheet.Api/Proofsheet.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080

COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Proofsheet.Api.dll"]
