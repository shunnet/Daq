# Snet.Iot.Daq.Web 部署指南（Linux / Ubuntu / 多架构）

支持三种部署方式，均支持 **x86_64 与 arm64**（树莓派、飞腾/鲲鹏等 ARM 服务器）。

## 方式一：Docker Compose（推荐）

```bash
cd F:/Snet/Daq          # 仓库根（Windows 上只需把仓库传到服务器）
docker compose -f Snet.Iot.Daq.Web/docker-compose.yml up -d --build
```

- Web：`http://<服务器IP>:5051`
- 数据持久化：命名卷 `snet-daq-data`（含 config/ db/ lib/ cer/），重建容器不丢数据
- 默认映射了 MQTT（1883）与 OPC UA（6688）端口，不需要可注释

## 方式二：Docker 多架构镜像（一次构建，多平台运行）

```bash
docker buildx build --platform linux/amd64,linux/arm64 \
  -f Snet.Iot.Daq.Web/Dockerfile \
  -t <仓库>/snet-iot-daq-web:latest --push .
```

运行时只需：

```bash
docker run -d --name snet-daq-web \
  -p 5051:5051 \
  -v snet-daq-data:/data \
  -e SNET_IOT_DAQ_DATA=/data \
  -e ASPNETCORE_URLS=http://+:5051 \
  --restart unless-stopped \
  <仓库>/snet-iot-daq-web:latest
```

## 方式三：Ubuntu 裸机（无 Docker，systemd）

```bash
# 在服务器上把整个仓库传过去（含 Snet.Iot.Daq.Core），然后：
sudo bash Snet.Iot.Daq.Web/deploy/ubuntu-deploy.sh
# 自定义端口/数据目录：
sudo bash Snet.Iot.Daq.Web/deploy/ubuntu-deploy.sh --port 8080 --data /srv/snet-daq
```

脚本自动：安装 .NET 10 运行时 → Release 发布 → 创建数据目录（www-data 属主）→ 注册并启动 systemd 服务。

```bash
journalctl -u snet-daq-web -f   # 查看日志
```

## 通用注意事项

1. **数据目录**：Web 数据目录由环境变量 `SNET_IOT_DAQ_DATA` 决定（优先级：env > appsettings > 程序目录）。**容器/裸机部署必须显式指定并做持久化**，否则程序目录内的 config/ db/ 在容器重建或发布更新时丢失。
2. **WebApi 功能端口**：非 root 用户绑定 <1024 端口会被内核拒绝（"拒绝访问"）。容器（aspnet 非 root 用户）与 systemd（www-data）下，**WebApi 端口请配置 ≥ 1024**（如 8080）。
3. **容器网络**：WebApi / MQTT / OPC UA 服务绑定在容器内。若采集设备需直连这些服务，compose 中已映射 1883/6688；WebApi 属 HTTP 管理接口，一般仅供本机/内网调用，默认绑 127.0.0.1 即可（保持默认）。
4. **HTTPS**：默认 HTTP 部署。需要 TLS 时在反向代理（Nginx/Caddy）终止，代理到 5051 端口。
5. **首次登录**：默认账号 `admin`，密码以 User.json 中的记录为准（测试环境重置为 admin123，生产部署后请立即修改）。
