.PHONY: up down build test test-e2e load-test load-test-smoke load-test-stress load-test-aws load-test-azure reconcile logs clean

up:
	@cp -n .env.example .env || true
	docker compose up --build -d

down:
	docker compose down

build:
	docker compose build

test:
	dotnet test CashFlow.sln --configuration Release --logger "console;verbosity=detailed"

test-e2e:
	dotnet test tests/CashFlow.E2E.Tests/CashFlow.E2E.Tests.csproj --configuration Release --logger "console;verbosity=detailed"

# ── Load tests (RNF throughput evidence) ─────────────────────────────────────
load-test:
	@mkdir -p tests/load/results
	k6 run -e SCENARIO=load tests/load/k6-scenarios.js

load-test-smoke:
	@mkdir -p tests/load/results
	k6 run -e SCENARIO=smoke tests/load/k6-scenarios.js

load-test-stress:
	@mkdir -p tests/load/results
	k6 run -e SCENARIO=stress tests/load/k6-scenarios.js

load-test-aws:
	@source tests/load/cloud/aws.env && mkdir -p tests/load/results && \
	 k6 run -e BASE_URL=$${BASE_URL} -e SCENARIO=load tests/load/k6-scenarios.js

load-test-azure:
	@source tests/load/cloud/azure.env && mkdir -p tests/load/results && \
	 k6 run -e BASE_URL=$${BASE_URL} -e SCENARIO=load tests/load/k6-scenarios.js

reconcile:
	./scripts/reconcile-outbox.sh

logs:
	docker compose logs -f

clean:
	docker compose down -v --remove-orphans
	find . -name "bin" -type d -exec rm -rf {} + 2>/dev/null || true
	find . -name "obj" -type d -exec rm -rf {} + 2>/dev/null || true

infra-only:
	docker compose up -d postgres rabbitmq redis seq jaeger

status:
	docker compose ps
