FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Astronomy.MediaFactory.Worker/Astronomy.MediaFactory.Worker.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app
RUN adduser --disabled-password --home /app appuser
COPY --from=build /app/publish .
ENV DOTNET_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
USER appuser
ENTRYPOINT ["dotnet", "Astronomy.MediaFactory.Worker.dll"]
