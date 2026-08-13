#!/usr/bin/env bash
# ============================================================
# Snet.Iot.Daq.Web Ubuntu 裸机部署脚本（无 Docker）
# 功能：安装 .NET 10 运行时 → 发布 → 数据目录 → systemd 服务
# 用法：sudo bash ubuntu-deploy.sh [--port 5051] [--data /var/lib/snet-daq-web]
# 支持 x86_64 / arm64（树莓派、飞腾等 ARM 服务器）
# ============================================================
set -euo pipefail

PORT=5051
DATA_DIR=/var/lib/snet-daq-web
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
SERVICE=snet-daq-web

while [[ $# -gt 0 ]]; do
    case "$1" in
        --port) PORT="$2"; shift 2 ;;
        --data) DATA_DIR="$2"; shift 2 ;;
        *) echo "未知参数: $1（支持 --port / --data）"; exit 1 ;;
    esac
done

if [[ $EUID -ne 0 ]]; then
    echo "请用 root 运行（sudo）"
    exit 1
fi

echo "==> 1/5 检测并安装 .NET 10 运行时"
if ! command -v dotnet >/dev/null 2>&1; then
    . /etc/os-release
    if [[ "$ID" != "ubuntu" ]]; then
        echo "仅支持 Ubuntu（检测到 $ID，请手动安装 .NET 10 后重试）"
        exit 1
    fi
    # 微软 apt 源
    wget https://packages.microsoft.com/config/ubuntu/"${VERSION_ID}"/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb
    dpkg -i /tmp/packages-microsoft-prod.deb
    apt-get update -y
    apt-get install -y aspnetcore-runtime-10.0
fi
DOTNET_VERSION="$(dotnet --version)"
echo "    dotnet: $DOTNET_VERSION"

echo "==> 2/5 发布项目（Release）"
PUBLISH_DIR="$SCRIPT_DIR/publish"
rm -rf "$PUBLISH_DIR"
pushd "$REPO_ROOT/Snet.Iot.Daq.Web" >/dev/null
dotnet publish -c Release -o "$PUBLISH_DIR"
popd >/dev/null

echo "==> 3/5 准备数据目录 $DATA_DIR"
install -d -o www-data -g www-data "$DATA_DIR"
echo "    数据（config/ db/ lib/ cer/）将写入 $DATA_DIR"

echo "==> 4/5 安装 systemd 服务"
cat > /etc/systemd/system/$SERVICE.service <<EOF
[Unit]
Description=Snet.Iot.Daq.Web - 工业物联网数据采集工具 Web 版
After=network.target

[Service]
Type=simple
User=www-data
Group=www-data
WorkingDirectory=$PUBLISH_DIR
Environment=SNET_IOT_DAQ_DATA=$DATA_DIR
Environment=ASPNETCORE_URLS=http://0.0.0.0:$PORT
Environment=DOTNET_EnableDiagnostics=0
ExecStart=/usr/bin/dotnet $PUBLISH_DIR/Snet.Iot.Daq.Web.dll
Restart=on-failure
RestartSec=5
# 安全加固
NoNewPrivileges=true
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=$DATA_DIR $PUBLISH_DIR

[Install]
WantedBy=multi-user.target
EOF

echo "==> 5/5 启动服务"
systemctl daemon-reload
systemctl enable $SERVICE
systemctl restart $SERVICE

echo
echo "部署完成：http://<服务器IP>:$PORT  （默认账号 admin/admin123，首次登录请修改）"
echo
echo "注意事项："
echo "  1. 若需对外提供 MQTT 服务端，开放 1883/tcp；OPC UA 服务端开放 6688/tcp（视设备配置而定）"
echo "  2. WebApi 功能端口配置须 >= 1024（如 8080），非 root 用户绑定 <1024 端口会被拒绝"
echo "  3. 查看日志：journalctl -u $SERVICE -f"
echo "  4. 数据备份：$DATA_DIR 整目录拷贝即备份（含用户、项目配置、采集地址库）"
