.PHONY: setup test-setup test integration build clean restore frontend \
       test-unit test-all publish

SOLUTION := src/Leecharr.sln
UNIT_TEST := src/Leecharr.Core.Test/Leecharr.Core.Test.csproj
CONSOLE := src/NzbDrone.Console/Leecharr.Console.csproj
FRONTEND := src/Leecharr.Frontend

setup:
	dotnet restore $(SOLUTION)
	@if [ -f $(FRONTEND)/package.json ]; then cd $(FRONTEND) && npm ci; fi

test-setup:
	dotnet build $(SOLUTION) --configuration Release

build: setup test-setup

publish:
	dotnet publish $(CONSOLE) --configuration Release --output _output

frontend:
	@if [ -f $(FRONTEND)/package.json ]; then cd $(FRONTEND) && npm run build; fi

restore:
	dotnet restore $(SOLUTION)

clean:
	dotnet clean $(SOLUTION) 2>/dev/null || true
	rm -rf _output _temp _tests

test:
	dotnet test $(SOLUTION) --configuration Release --no-build \
		--logger "trx;LogFileName=test-results.trx" \
		--collect:"XPlat Code Coverage"

test-unit: test

test-all: test
