# ============================================================================
# Aireq — top-level Makefile
# Run `make help` to see the available commands.
# ============================================================================

SHELL := /usr/bin/env bash
.DEFAULT_GOAL := help

# Color helpers (works in any sane terminal).
COLOR_RESET   := \033[0m
COLOR_BOLD    := \033[1m
COLOR_BLUE    := \033[34m
COLOR_GREEN   := \033[32m

# -----------------------------------------------------------------------------
help: ## Show this help.
	@printf "$(COLOR_BOLD)Aireq — commands$(COLOR_RESET)\n\n"
	@awk 'BEGIN {FS = ":.*?## "} /^[a-zA-Z_-]+:.*?## / {printf "  $(COLOR_GREEN)%-22s$(COLOR_RESET) %s\n", $$1, $$2}' $(MAKEFILE_LIST)

# -----------------------------------------------------------------------------
# Bootstrap
# -----------------------------------------------------------------------------
bootstrap: ## Install all dependencies (.NET, Node, tools) and copy .env.example → .env.local.
	@./scripts/bootstrap.sh

# -----------------------------------------------------------------------------
# Dev — run all three apps with hot reload
# -----------------------------------------------------------------------------
dev: ## Run api + worker + web with hot reload (uses 'concurrently'-style output).
	@printf "$(COLOR_BLUE)Booting api + worker + web…$(COLOR_RESET)\n"
	@( $(MAKE) api & $(MAKE) worker & $(MAKE) web & wait )

# NOTE on env loading: we used to source .env.local into the recipe shell, but
# that breaks on unquoted special chars (e.g. RESEND_FROM=Aireq <noreply@…>).
# Instead:
#   - .NET projects use the DotNetEnv NuGet package which parses .env.local
#     directly (handles & < > = without quoting).
#   - Next.js auto-loads apps/web/.env.local. If you want NEXT_PUBLIC_* vars
#     in the web app, mirror them there (we'll automate this in AIR0002).

# DOTNET_USE_POLLING_FILE_WATCHER works around a macOS dotnet-watch bug where
# the FSEvents-based watcher chokes on the SDK manifest directory. Polling is
# slightly slower to detect changes but reliable.
api: ## Run the API project with hot reload (port 5180 via launchSettings).
	cd apps/api/Aireq.Api && DOTNET_USE_POLLING_FILE_WATCHER=true dotnet watch run

worker: ## Run the worker project with hot reload (port 5090 via launchSettings).
	cd apps/worker/Aireq.Worker && DOTNET_USE_POLLING_FILE_WATCHER=true dotnet watch run

web: ## Run the Next.js web app with hot reload (port 3000).
	cd apps/web && pnpm dev

# -----------------------------------------------------------------------------
# Quality gates
# -----------------------------------------------------------------------------
build: ## Build everything (dotnet + web).
	dotnet build Aireq.sln --configuration Release --nologo
	cd apps/web && pnpm build

test: ## Run all tests.
	dotnet test Aireq.sln --no-build --nologo --verbosity normal
	cd apps/web && pnpm test --run || true

lint: ## Lint everything.
	dotnet format Aireq.sln --verify-no-changes --severity warn
	cd apps/web && pnpm lint

format: ## Auto-format everything.
	dotnet format Aireq.sln
	cd apps/web && pnpm format

# -----------------------------------------------------------------------------
# Database (Neon dev branch)
# -----------------------------------------------------------------------------
db-migrate: ## Apply EF Core migrations to the dev DB ($DATABASE_URL_DEV).
	cd apps/api/Aireq.Api && dotnet ef database update

db-add-migration: ## Add a new EF Core migration. Usage: make db-add-migration name=AddTenants
	cd apps/api/Aireq.Api && dotnet ef migrations add $(name)

# -----------------------------------------------------------------------------
# House-keeping
# -----------------------------------------------------------------------------
clean: ## Remove build artifacts.
	dotnet clean Aireq.sln --nologo
	rm -rf apps/web/.next apps/web/node_modules

doctor: ## Print versions of required tools.
	@echo ".NET SDK:    $$(dotnet --version 2>/dev/null || echo MISSING)"
	@echo "Node:        $$(node -v 2>/dev/null || echo MISSING)"
	@echo "pnpm:        $$(pnpm -v 2>/dev/null || echo MISSING)"
	@echo "gh:          $$(gh --version 2>/dev/null | head -1 || echo MISSING)"
	@echo "git:         $$(git --version 2>/dev/null || echo MISSING)"

.PHONY: help bootstrap dev api worker web build test lint format db-migrate db-add-migration clean doctor
