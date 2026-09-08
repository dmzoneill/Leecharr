#!/bin/sh
set -e

UMASK=${UMASK:-022}
umask "$UMASK"

EXEC_PREFIX=""

if [ -n "$PUID" ] || [ -n "$PGID" ]; then
    PUID=${PUID:-1000}
    PGID=${PGID:-1000}

    if [ "$(id -u)" = "0" ]; then
        if ! getent group "$PGID" >/dev/null 2>&1; then
            groupadd -g "$PGID" leecharr 2>/dev/null || addgroup -g "$PGID" leecharr 2>/dev/null || true
        fi
        GROUP_NAME=$(getent group "$PGID" | cut -d: -f1)
        GROUP_NAME=${GROUP_NAME:-$PGID}

        if ! getent passwd "$PUID" >/dev/null 2>&1; then
            useradd -u "$PUID" -g "$GROUP_NAME" -d /config -s /bin/sh -M -N leecharr 2>/dev/null || adduser -u "$PUID" -G "$GROUP_NAME" -h /config -s /bin/sh -D leecharr 2>/dev/null || true
        fi

        mkdir -p /config /downloads
        chown -R "$PUID:$PGID" /config /downloads 2>/dev/null || true

        if command -v gosu >/dev/null 2>&1; then
            EXEC_PREFIX="gosu $PUID:$PGID"
        elif command -v su-exec >/dev/null 2>&1; then
            EXEC_PREFIX="su-exec $PUID:$PGID"
        fi
    fi
fi

if [ "${COVERAGE_ENABLED}" = "1" ]; then
    mkdir -p /coverage
    if [ -n "$PUID" ] && [ -n "$PGID" ] && [ "$(id -u)" = "0" ]; then
        chown -R "$PUID:$PGID" /coverage 2>/dev/null || true
    fi
    exec $EXEC_PREFIX dotnet-coverage collect \
        --output /coverage/coverage.xml \
        --output-format xml \
        -- dotnet /app/Leecharr.Console.dll --data=/config "$@"
else
    exec $EXEC_PREFIX dotnet /app/Leecharr.Console.dll --data=/config "$@"
fi

