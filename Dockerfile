FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/HelloSud.Api/HelloSud.Api.csproj src/HelloSud.Api/
RUN dotnet restore src/HelloSud.Api/HelloSud.Api.csproj
COPY src/HelloSud.Api/ src/HelloSud.Api/
RUN dotnet publish src/HelloSud.Api -c Release --no-restore -o /out
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:8000
EXPOSE 8000
USER $APP_UID
ENTRYPOINT ["dotnet", "HelloSud.Api.dll"]
