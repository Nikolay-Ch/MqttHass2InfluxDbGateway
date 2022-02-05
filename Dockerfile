#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:6.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["MqttHass2InfluxDbGateway/MqttHass2InfluxDbGateway/MqttHass2InfluxDbGateway.csproj", "MqttHass2InfluxDbGateway/MqttHass2InfluxDbGateway/"]
COPY ["HassSensorService/HassDeviceBaseWorkers/HassDeviceBaseWorkers.csproj", "HassSensorService/HassDeviceBaseWorkers/"]
COPY ["HassSensorService/HassMqttIntegration/HassMqttIntegration.csproj", "HassSensorService/HassMqttIntegration/"]
COPY ["HassSensorService/HassSensorConfiguration/HassSensorConfiguration.csproj", "HassSensorService/HassSensorConfiguration/"]
RUN dotnet restore "MqttHass2InfluxDbGateway/MqttHass2InfluxDbGateway/MqttHass2InfluxDbGateway.csproj"
COPY . .
WORKDIR "/src/MqttHass2InfluxDbGateway/MqttHass2InfluxDbGateway"
RUN dotnet build "MqttHass2InfluxDbGateway.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "MqttHass2InfluxDbGateway.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MqttHass2InfluxDbGateway.dll"]