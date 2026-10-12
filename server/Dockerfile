FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY server/Nms.sln ./
COPY server/src/Nms.Api/Nms.Api.csproj src/Nms.Api/
RUN dotnet restore src/Nms.Api/Nms.Api.csproj
COPY server/src ./src
RUN dotnet publish src/Nms.Api/Nms.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app ./
COPY db ./db
ENV ASPNETCORE_URLS=http://+:8080 \
    Database__SqlDirectory=/app/db
EXPOSE 8080
ENTRYPOINT ["dotnet", "Nms.Api.dll"]
