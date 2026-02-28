#!/usr/bin/env bash
# One-time EC2 setup script for Chaos Server
# Run as: sudo bash setup.sh
set -euo pipefail

echo "=== Chaos Server EC2 Setup ==="

# Install .NET 8 runtime
echo "[1/5] Installing .NET 8 runtime..."
apt-get update
apt-get install -y dotnet-runtime-8.0

# Create deploy directory
echo "[2/5] Creating /opt/chaos..."
mkdir -p /opt/chaos
chown ubuntu:ubuntu /opt/chaos

# Install systemd service
echo "[3/5] Installing systemd service..."
cp "$(dirname "$0")/chaos.service" /etc/systemd/system/chaos.service
systemctl daemon-reload
systemctl enable chaos

# Configure firewall (UFW)
echo "[4/5] Configuring firewall..."
ufw allow 22/tcp    # SSH
ufw allow 5000/tcp  # SignalR / HTTP
ufw allow 9000/udp  # Voice relay
ufw --force enable

echo "[5/5] Setup complete!"
echo ""
echo "Next steps:"
echo "  1. Deploy the server files to /opt/chaos/"
echo "  2. Run: sudo systemctl start chaos"
echo "  3. Check status: sudo systemctl status chaos"
echo "  4. View logs: sudo journalctl -u chaos -f"
