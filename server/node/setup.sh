#!/usr/bin/env bash
# Instala o servidor online do port numa máquina Ubuntu (Oracle Cloud "Always Free", região São Paulo).
# Uso, dentro da máquina, na pasta com server.mjs e package.json:
#     sudo bash setup.sh
# Faz: Node.js 22, dependências, serviço systemd (sobe sozinho e reinicia se cair), Caddy com HTTPS automático
# (certificado Let's Encrypt para <IP>.sslip.io, sem precisar comprar domínio) e libera as portas 80/443 no
# firewall da própria máquina. As portas também precisam estar abertas no painel da Oracle (ver README).
set -euo pipefail

if [ "$(id -u)" -ne 0 ]; then
  echo "Rode com sudo: sudo bash setup.sh" >&2
  exit 1
fi

APP_DIR=/opt/adofai-online
SRC_DIR="$(cd "$(dirname "$0")" && pwd)"

PUBLIC_IP="$(curl -fsS https://api.ipify.org || curl -fsS https://ifconfig.me)"
HOSTNAME_SSLIP="$(echo "$PUBLIC_IP" | tr '.' '-').sslip.io"
echo "IP público: $PUBLIC_IP -> endereço do servidor: https://$HOSTNAME_SSLIP"

echo "== Node.js 22"
if ! command -v node >/dev/null || [ "$(node -p 'process.versions.node.split(".")[0]')" -lt 20 ]; then
  curl -fsSL https://deb.nodesource.com/setup_22.x | bash -
  apt-get install -y nodejs
fi

echo "== servidor em $APP_DIR"
mkdir -p "$APP_DIR"
cp "$SRC_DIR/server.mjs" "$SRC_DIR/package.json" "$APP_DIR/"
cd "$APP_DIR"
npm install --omit=dev --no-audit --no-fund
id -u adofai >/dev/null 2>&1 || useradd --system --home "$APP_DIR" --shell /usr/sbin/nologin adofai
chown -R adofai:adofai "$APP_DIR"

cat >/etc/systemd/system/adofai-online.service <<EOF
[Unit]
Description=Servidor online do port de A Dance of Fire and Ice
After=network-online.target

[Service]
User=adofai
WorkingDirectory=$APP_DIR
Environment=PORT=8787 HOST=127.0.0.1 REGION=SA SERVER_NAME=GRU
ExecStart=/usr/bin/node $APP_DIR/server.mjs
Restart=always
RestartSec=2

[Install]
WantedBy=multi-user.target
EOF
systemctl daemon-reload
systemctl enable --now adofai-online

echo "== Caddy (HTTPS automático)"
if ! command -v caddy >/dev/null; then
  apt-get install -y debian-keyring debian-archive-keyring apt-transport-https curl gnupg
  curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' | gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
  curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' >/etc/apt/sources.list.d/caddy-stable.list
  apt-get update
  apt-get install -y caddy
fi
cat >/etc/caddy/Caddyfile <<EOF
$HOSTNAME_SSLIP {
    reverse_proxy 127.0.0.1:8787
}
EOF

echo "== firewall da máquina (as imagens Ubuntu da Oracle bloqueiam tudo menos SSH)"
iptables -C INPUT -p tcp --dport 80 -j ACCEPT 2>/dev/null || iptables -I INPUT 5 -m state --state NEW -p tcp --dport 80 -j ACCEPT
iptables -C INPUT -p tcp --dport 443 -j ACCEPT 2>/dev/null || iptables -I INPUT 5 -m state --state NEW -p tcp --dport 443 -j ACCEPT
if command -v netfilter-persistent >/dev/null; then
  netfilter-persistent save
else
  apt-get install -y iptables-persistent && netfilter-persistent save
fi

systemctl restart caddy
sleep 3
echo
echo "== teste"
curl -fsS "http://127.0.0.1:8787/" && echo
echo
echo "Pronto. Endereço do servidor para colocar no jogo:"
echo "    https://$HOSTNAME_SSLIP"
echo "(o certificado HTTPS pode levar 1-2 minutos na primeira vez; teste abrindo esse endereço no navegador)"
