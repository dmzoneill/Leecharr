.PHONY: setup test-setup test integration build clean restore frontend \
       stack-init stack-build stack-up stack-down stack-configure stack-healthy stack-rebuild stack-clean \
       test-unit test-integration test-all publish coverage-report lint format \
       quality-report container-build container-build-test \
       bump-patch bump-minor bump-major

SOLUTION := src/Leecharr.sln
UNIT_TEST := src/Leecharr.Core.Test/Leecharr.Core.Test.csproj
INTEGRATION_TEST := src/NzbDrone.Integration.Test/Leecharr.Integration.Test.csproj
CONSOLE := src/NzbDrone.Console/Leecharr.Console.csproj
FRONTEND := src/Leecharr.Frontend
COMPOSE := podman-compose
SERVICES := leecharr seedarr sonarr radarr prowlarr jackett
DEPS := seedarr sonarr radarr prowlarr jackett

SEEDARR_API_KEY := 1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d
LEECHARR_API_KEY := 2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e
SONARR_API_KEY := 4b8c9d0e1f2a3b4c5d6e7f8a9b0c1d2e
RADARR_API_KEY := 5c9d0e1f2a3b4c5d6e7f8a9b0c1d2e3f
PROWLARR_API_KEY := 3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f

# --- Build targets (called by upstream CI: make setup) ---

setup:
	dotnet restore $(SOLUTION)
	@if [ -f $(FRONTEND)/package.json ]; then cd $(FRONTEND) && npm ci; fi
	@command -v podman-compose > /dev/null 2>&1 || pip install podman-compose 2>/dev/null || true

lint:
	@echo "🔍 Checking Prettier style..."
	@npx --yes prettier --check "src/Leecharr.Frontend/**/*.{ts,tsx,css,json,js,html}"
	@echo "🔍 Checking C# format..."
	@dotnet format $(SOLUTION) --verify-no-changes

format:
	@echo "✨ Formatting frontend with Prettier..."
	@npx --yes prettier --write "src/Leecharr.Frontend/**/*.{ts,tsx,css,json,js,html}"
	@echo "✨ Formatting C# with dotnet format..."
	@dotnet format $(SOLUTION)

test-setup:
	dotnet build $(SOLUTION) --configuration Release

build: setup frontend test-setup

publish: frontend
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
		--settings .runsettings \
		--logger "trx;LogFileName=test-results.trx" \
		--logger "console;verbosity=normal" \
		--collect:"XPlat Code Coverage"

test-unit: test

# --- Integration test stack ---

stack-init:
	@mkdir -p config/leecharr config/seedarr config/sonarr config/radarr config/prowlarr config/jackett data/downloads data/movies data/series
	@if [ ! -f config/leecharr/config.xml ]; then cp tests/config/leecharr/config.xml config/leecharr/config.xml; fi
	@if [ ! -f config/seedarr/config.xml ]; then cp tests/config/seedarr/config.xml config/seedarr/config.xml; fi
	@if [ ! -f config/sonarr/config.xml ]; then cp tests/config/sonarr/config.xml config/sonarr/config.xml; fi
	@if [ ! -f config/radarr/config.xml ]; then cp tests/config/radarr/config.xml config/radarr/config.xml; fi
	@if [ ! -f config/prowlarr/config.xml ]; then cp tests/config/prowlarr/config.xml config/prowlarr/config.xml; fi
	@chmod -R 777 config data 2>/dev/null || true

stack-build:
	$(COMPOSE) build

stack-rebuild:
	$(COMPOSE) build --no-cache leecharr

stack-up: stack-init
	$(COMPOSE) up -d $(DEPS)
	@echo "Waiting for dependency services..."
	@for i in $$(seq 1 120); do \
		if curl -sf http://localhost:8989/ping > /dev/null 2>&1 && \
		   curl -sf http://localhost:7878/ping > /dev/null 2>&1 && \
		   curl -sf http://localhost:9696/ping > /dev/null 2>&1 && \
		   curl -sf http://localhost:9898/api/v1/system/status > /dev/null 2>&1 && \
		   curl -sf http://localhost:9117/UI/Dashboard > /dev/null 2>&1; then \
			echo "Dependencies healthy after $${i}s"; \
			break; \
		fi; \
		sleep 1; \
	done
	$(COMPOSE) up --no-deps -d leecharr

stack-down:
	$(COMPOSE) down 2>/dev/null || true

stack-clean:
	@$(COMPOSE) down 2>/dev/null || true
	@podman rm -f $(SERVICES) arr-configure 2>/dev/null || true
	@podman unshare rm -rf config 2>/dev/null || rm -rf config 2>/dev/null || true
	@$(COMPOSE) down -v 2>/dev/null || true

stack-healthy:
	@echo "Waiting for Leecharr..."
	@for i in $$(seq 1 120); do \
		if curl -sf http://localhost:7889/ping > /dev/null 2>&1; then \
			echo "Leecharr healthy after $${i}s"; \
			exit 0; \
		fi; \
		sleep 1; \
	done; \
	echo "Timeout waiting for Leecharr"; exit 1

stack-configure:
	@$(COMPOSE) rm -f configure 2>/dev/null || true
	@$(COMPOSE) up --no-deps configure 2>&1 | tail -60

integration:
	@if [ -f $(INTEGRATION_TEST) ]; then \
		dotnet test $(INTEGRATION_TEST) --configuration Release --no-build \
			--settings .runsettings \
			--logger "trx;LogFileName=integration-test-results.trx" \
			--logger "console;verbosity=normal" \
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

quality-report:
	@mkdir -p _reports/jscpd _reports/coverage
	@echo "📊 ==> Running C# Static Analysis (Roslynator)..."
	@PATH="$$PATH:$$HOME/.dotnet/tools" roslynator analyze $(SOLUTION) --output _reports/roslynator.xml || true
	@echo "📊 ==> Running Duplicate Code Detection (jscpd)..."
	@cd $(FRONTEND) && npx jscpd ../../src --reporters html,json --output ../../_reports/jscpd --ignore "**/*.json,**/*.xml,**/bin/**,**/obj/**,**/node_modules/**" || true
	@echo "📊 ==> Running TypeScript Type Coverage..."
	@cd $(FRONTEND) && npx type-coverage --detail || true
	@echo "📊 ==> Quality reports generated in _reports/"
	@echo "    - C# Roslynator: _reports/roslynator.xml"
	@echo "    - Duplication (HTML): _reports/jscpd/html/index.html"

container-build:
	podman build -t leecharr:latest -f Containerfile .

container-build-test:
	podman build --target test --build-arg COVERAGE_TOOLS=true -t leecharr:test -f Containerfile .

bump-patch:
	@CURRENT=$$(grep '^version=' version | cut -d= -f2); \
	IFS='.' read -r major minor patch <<< "$$CURRENT"; \
	NEW_VER="$$major.$$minor.$$((patch + 1))"; \
	echo "Bumping version: $$CURRENT -> $$NEW_VER"; \
	echo "version=$$NEW_VER" > version; \
	if [ -f $(FRONTEND)/package.json ]; then (cd $(FRONTEND) && npm version $$NEW_VER --no-git-tag-version --allow-same-version); fi

bump-minor:
	@CURRENT=$$(grep '^version=' version | cut -d= -f2); \
	IFS='.' read -r major minor patch <<< "$$CURRENT"; \
	NEW_VER="$$major.$$((minor + 1)).0"; \
	echo "Bumping version: $$CURRENT -> $$NEW_VER"; \
	echo "version=$$NEW_VER" > version; \
	if [ -f $(FRONTEND)/package.json ]; then (cd $(FRONTEND) && npm version $$NEW_VER --no-git-tag-version --allow-same-version); fi

bump-major:
	@CURRENT=$$(grep '^version=' version | cut -d= -f2); \
	IFS='.' read -r major minor patch <<< "$$CURRENT"; \
	NEW_VER="$$((major + 1)).0.0"; \
	echo "Bumping version: $$CURRENT -> $$NEW_VER"; \
	echo "version=$$NEW_VER" > version; \
	if [ -f $(FRONTEND)/package.json ]; then (cd $(FRONTEND) && npm version $$NEW_VER --no-git-tag-version --allow-same-version); fi

