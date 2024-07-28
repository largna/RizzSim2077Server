FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 13233

ENV ASPNETCORE_URLS=http://+:13233
ENV ASPNETCORE_ENVIRONMENT=Production

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG configuration=Release
WORKDIR /src
COPY ["UMS/UMS.csproj", "UMS/"]
COPY ["Common/Common.csproj", "Common/"]
RUN dotnet restore "UMS/UMS.csproj"
COPY . .
WORKDIR "/src/UMS"
RUN dotnet build "UMS.csproj" -c $configuration -o /app/build

FROM build AS publish
ARG configuration=Release
RUN dotnet publish "UMS.csproj" -c $configuration -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY UMS/appsettings.json .
COPY UMS/appsettings.Production.json .
ENTRYPOINT ["dotnet", "UMS.dll"]