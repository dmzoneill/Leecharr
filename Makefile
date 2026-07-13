.PHONY: setup test-setup test integration build clean restore frontend \
       test-unit test-integration test-all publish coverage-report

SOLUTION := src/Leecharr.sln
UNIT_TEST := src/Leecharr.Core.Test/Leecharr.Core.Test.csproj
INTEGRATION_TEST := src/NzbDrone.Integration.Test/Leecharr.Integration.Test.csproj
CONSOLE := src/NzbDrone.Console/Leecharr.Console.csproj
FRONTEND := src/Leecharr.Frontend

# --- Build targets (called by upstream CI: make setup) ---

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
	rm -rf _output _temp _tests coverage-report

# --- Tests (called by upstream CI: make test / make integration) ---

test:
	dotnet test $(UNIT_TEST) --configuration Release --no-build \
		--logger "trx;LogFileName=test-results.trx" \
		--collect:"XPlat Code Coverage"

test-unit: test

integration:
	@if [ -f $(INTEGRATION_TEST) ]; then \
		dotnet test $(INTEGRATION_TEST) --configuration Release --no-build \
			--logger "trx;LogFileName=integration-test-results.trx" \
			--collect:"XPlat Code Coverage"; \
	fi

test-integration: integration

coverage-report:
	@REPORTS=$$(find . -name "coverage.cobertura.xml" -path "*/TestResults/*" 2>/dev/null | tr '\n' ';'); \
	if [ -n "$$REPORTS" ]; then \
		dotnet reportgenerator -reports:"$$REPORTS" -targetdir:coverage-report -reporttypes:Html 2>/dev/null && \
		echo "Coverage report generated: coverage-report/index.html" || \
		echo "Install reportgenerator: dotnet tool install -g dotnet-reportgenerator-globaltool"; \
	else \
		echo "No coverage files found"; \
	fi

test-all: test integration
