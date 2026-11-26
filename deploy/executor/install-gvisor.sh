#!/bin/sh
# Installs gVisor (runsc) from its official apt repository and registers it with Docker.
# gVisor puts a user-space kernel between user code and the host kernel: much stronger isolation
# than ordinary containers for untrusted code. Works on x86_64 and arm64 (e.g. Oracle Ampere VMs).
set -eu
apt-get update
apt-get install -y apt-transport-https ca-certificates curl gnupg
curl -fsSL https://gvisor.dev/archive.key | gpg --dearmor -o /usr/share/keyrings/gvisor-archive-keyring.gpg
echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/gvisor-archive-keyring.gpg] https://storage.googleapis.com/gvisor/releases release main" \
  > /etc/apt/sources.list.d/gvisor.list
apt-get update
apt-get install -y runsc
runsc install          # adds the "runsc" runtime to /etc/docker/daemon.json
systemctl restart docker
docker run --rm --runtime=runsc hello-world >/dev/null && echo "gVisor is working."
# Keep the host patched automatically.
apt-get install -y unattended-upgrades
dpkg-reconfigure -f noninteractive unattended-upgrades
