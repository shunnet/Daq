# 多阶段构建：SDK 构建 → aspnet 运行时（非 root 用户）
# 注意：项目引用了 ../Snet.Iot.Daq.Core，构建上下文必须指向仓库根 F:\Snet\Daq：
#   单架构构建：  docker build -f Snet.Iot.Daq.Web/Dockerfile -t snet-iot-daq-web:latest .
#   多架构构建：  docker buildx build --platform linux/amd64,linux/arm64 \
#                   -f Snet.Iot.Daq.Web/Dockerfile -t snet-iot-daq-web:latest --push .
#   （基础镜像 mcr.microsoft.com/dotnet/* 官方支持多架构，一条命令同时产出 amd64/arm64 镜像）

# ---- 构建阶段 ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 先 COPY 两个 csproj 单独还原，利用 Docker 层缓存（依赖不变则还原层不重建）
COPY Snet.Iot.Daq.Core/Snet.Iot.Daq.Core.csproj Snet.Iot.Daq.Core/
COPY Snet.Iot.Daq.Web/Snet.Iot.Daq.Web.csproj Snet.Iot.Daq.Web/
RUN dotnet restore Snet.Iot.Daq.Web/Snet.Iot.Daq.Web.csproj

COPY Snet.Iot.Daq.Core/ Snet.Iot.Daq.Core/
COPY Snet.Iot.Daq.Web/ Snet.Iot.Daq.Web/
RUN dotnet publish Snet.Iot.Daq.Web/Snet.Iot.Daq.Web.csproj -c Release -o /app/publish --no-restore

# ---- 运行阶段 ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

COPY --from=build /app/publish .

# 数据目录 /data：config/ db/ lib/ cer/ 全部在此（WebPaths 优先级：env > appsettings > 程序目录）
# 必须挂载可写卷，否则容器重建后配置与采集数据丢失（见 docker-compose.yml）
ENV SNET_IOT_DAQ_DATA=/data \
    ASPNETCORE_URLS=http://+:5051 \
    DOTNET_EnableDiagnostics=0

# aspnet 镜像预置非 root 用户 $APP_UID（UID 1654），数据目录归其所有
RUN mkdir -p /data && chown -R $APP_UID:$APP_UID /data
USER $APP_UID

EXPOSE 5051

# 健康检查：aspnet 镜像无 curl/wget，用 bash /dev/tcp
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD bash -c 'exec 3<>/dev/tcp/127.0.0.1/5051'

ENTRYPOINT ["dotnet", "Snet.Iot.Daq.Web.dll"]
