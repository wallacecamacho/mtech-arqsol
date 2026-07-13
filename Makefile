.PHONY: up down build test logs clean

up:
	@cp -n .env.example .env || true
	docker compose up --build -d

down:
	docker compose down

build:
	docker compose build

test:
	dotnet test CashFlow.sln --configuration Release --logger "console;verbosity=detailed"

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
