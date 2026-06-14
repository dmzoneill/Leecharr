#!/bin/sh
set -e
if [ "${COVERAGE_ENABLED}" = "1" ]; then
    mkdir -p /coverage
    exec dotnet-coverage collect \
        --output /coverage/coverage.xml \
        --output-format xml \
        -- dotnet /app/Leecharr.Console.dll --data=/config
else
    exec dotnet Leecharr.Console.dll --data=/config
fi
