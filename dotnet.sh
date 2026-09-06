sudo apt update
sudo apt install -y wget apt-transport-https

wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
sudo apt update

sudo apt install -y dotnet-runtime-10.0

## INICIAR EL SERVICIO
APP_NAME="lin-cloud-demon"
APP_DLL="LIN.Cloud.Node.Monitor.dll"
APP_DIR="/var/www/demon/publish"
SERVICE_USER="www-data"

# Crear directorio si no existe
sudo mkdir -p $APP_DIR

# Permisos
sudo chown -R $SERVICE_USER:$SERVICE_USER $APP_DIR
sudo usermod -aG docker $SERVICE_USER

# Crear archivo de servicio
sudo tee /etc/systemd/system/$APP_NAME.service > /dev/null <<EOF
[Unit]
Description=LIN Cloud Demon
After=network.target

[Service]
WorkingDirectory=$APP_DIR
ExecStart=/usr/bin/dotnet $APP_DIR/$APP_DLL
Restart=always
RestartSec=5
User=$SERVICE_USER

SyslogIdentifier=$APP_NAME

[Install]
WantedBy=multi-user.target
EOF

# Recargar systemd
sudo systemctl daemon-reload

# Habilitar e iniciar servicio
sudo systemctl enable $APP_NAME
sudo systemctl start $APP_NAME

echo "✅ Servicio $APP_NAME instalado y ejecutándose"
echo "📄 Logs: journalctl -u $APP_NAME -f"

# Ver estado
systemctl status lin-cloud-demon

# Reiniciar servicio
systemctl restart lin-cloud-demon.service