#!/usr/bin/env bash
# Read-only diagnostics. Does not print environment variables, configs, or secrets.
set -u
printf 'CPU count: '; getconf _NPROCESSORS_ONLN
uptime
free -h
df -h /
if command -v docker >/dev/null 2>&1; then
    docker ps --format 'table {{.Names}}\t{{.Status}}\t{{.Ports}}'
    docker stats --no-stream --format 'table {{.Name}}\t{{.CPUPerc}}\t{{.MemUsage}}\t{{.NetIO}}'
    docker network ls --format 'table {{.Name}}\t{{.Driver}}'
fi
ss -lntu
