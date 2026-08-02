#!/usr/bin/env bash
#
# Deploys the API to Azure Container Apps, and creates whatever it needs that is not there yet.
#
# Safe to run again. Every step either creates something or updates it to match, so this is how you
# deploy the first time and how you change a setting afterwards. Nothing here deletes anything.
#
# Container Apps rather than App Service because of how this is paid for: an Azure for Students
# subscription is a fixed grant, and Container Apps bills by the second with a monthly free
# allowance and scales to zero between requests. An App Service plan that can run a container is
# billed whether or not anybody visits, which on a testing deployment is most of the time.
#
# Usage:
#   az login
#   az account set --subscription "<your subscription>"
#   ./azure/deploy.sh
#
# Everything below can be overridden from the environment, so a second deployment for a different
# purpose is a matter of exporting a different RESOURCE_GROUP and APP_NAME.
set -euo pipefail

LOCATION="${LOCATION:-australiaeast}"
RESOURCE_GROUP="${RESOURCE_GROUP:-publication-research}"
ENVIRONMENT_NAME="${ENVIRONMENT_NAME:-publication-research-env}"
# Always awake, and how big. A replica that is running but not answering anything is billed at
# Azure's reduced idle rate, so "never sleeps" costs a few dollars a month rather than the full
# rate. Set MIN_REPLICAS=0 to go back to scaling to zero and paying almost nothing, at the price of
# a ten to thirty second wait on the first request after a quiet spell.
MIN_REPLICAS="${MIN_REPLICAS:-1}"
CPU="${CPU:-0.5}"
MEMORY="${MEMORY:-1.0Gi}"

APP_NAME="${APP_NAME:-publication-research-backend}"
IMAGE="${IMAGE:-docker.io/javiertrombetta/publication-research-backend:latest}"

# The storage account that holds uploads. Names are global and may only be lower case letters and
# digits, so a suffix keeps it unique without asking anybody to invent one.
STORAGE_ACCOUNT="${STORAGE_ACCOUNT:-}"
BLOB_CONTAINER="${BLOB_CONTAINER:-uploads}"

need() {
  if [ -z "${!1:-}" ]; then
    echo "error: $1 is not set. See azure/README.md for what each one is." >&2
    exit 1
  fi
}

# The values that cannot be guessed. Kept out of this file deliberately: a connection string and a
# signing key do not belong in a repository.
need CONNECTION_STRING     # Aiven's MySQL connection string, with SslMode=Required
need JWT_SIGNING_KEY       # 32+ random characters
need SEED_ADMIN_EMAIL
need SEED_ADMIN_PASSWORD

echo "==> Subscription"
az account show --query "{name:name, id:id}" -o tsv

echo "==> Resource providers"
# Registered here rather than left to the reader. A subscription that has never used one of these
# reports the failure as "SubscriptionNotFound", which sends you looking at the wrong thing: the
# subscription is fine, Azure simply does not know it for that provider yet. Registering is
# idempotent and returns immediately once it has been done.
for provider in Microsoft.App Microsoft.OperationalInsights Microsoft.Storage; do
  state="$(az provider show --namespace "$provider" --query registrationState -o tsv 2>/dev/null || echo Unknown)"
  if [ "$state" != "Registered" ]; then
    echo "    registering $provider (first time on this subscription, takes a minute)"
    az provider register --namespace "$provider" --wait
  fi
done

echo "==> Resource group: $RESOURCE_GROUP ($LOCATION)"
az group create --name "$RESOURCE_GROUP" --location "$LOCATION" --output none

echo "==> Container Apps environment: $ENVIRONMENT_NAME"
# Shared with the frontend. Creating it here and again there is the same call; whichever runs first
# makes it. The workspace it logs to is created along with it.
if ! az containerapp env show --name "$ENVIRONMENT_NAME" --resource-group "$RESOURCE_GROUP" --output none 2>/dev/null; then
  az containerapp env create \
    --name "$ENVIRONMENT_NAME" \
    --resource-group "$RESOURCE_GROUP" \
    --location "$LOCATION" \
    --output none
fi

echo "==> Storage account for uploads"
# A container app has no disk that survives a restart, so uploads have to go somewhere else. Blob
# Storage is pennies at this size and is on the same subscription as everything else.
if [ -z "$STORAGE_ACCOUNT" ]; then
  STORAGE_ACCOUNT="prsuploads$(az account show --query id -o tsv | tr -d '-' | cut -c1-8)"
fi

if ! az storage account show --name "$STORAGE_ACCOUNT" --resource-group "$RESOURCE_GROUP" --output none 2>/dev/null; then
  az storage account create \
    --name "$STORAGE_ACCOUNT" \
    --resource-group "$RESOURCE_GROUP" \
    --location "$LOCATION" \
    --sku Standard_LRS \
    --kind StorageV2 \
    --allow-blob-public-access false \
    --output none
fi

STORAGE_CONNECTION_STRING="$(az storage account show-connection-string \
  --name "$STORAGE_ACCOUNT" --resource-group "$RESOURCE_GROUP" --query connectionString -o tsv)"

echo "    $STORAGE_ACCOUNT, container '$BLOB_CONTAINER' (created on first use, private)"

# The frontend's address, for the links in verification and password-reset emails and for CORS.
# Empty on the very first run, because the frontend does not exist yet; run this again afterwards
# and it fills in. Nothing breaks in between except those links.
FRONTEND_URL="${FRONTEND_URL:-$(az containerapp show \
  --name "${FRONTEND_APP_NAME:-publication-research-frontend}" \
  --resource-group "$RESOURCE_GROUP" \
  --query "properties.configuration.ingress.fqdn" -o tsv 2>/dev/null || true)}"

if [ -n "$FRONTEND_URL" ] && [[ "$FRONTEND_URL" != https://* ]]; then
  FRONTEND_URL="https://$FRONTEND_URL"
fi

echo "==> Container app: $APP_NAME"

# Secrets are set separately from the environment variables that point at them, so the values never
# appear in the app's own configuration and are not readable back out of it.
SECRETS=(
  "connection-string=$CONNECTION_STRING"
  "jwt-signing-key=$JWT_SIGNING_KEY"
  "seed-admin-password=$SEED_ADMIN_PASSWORD"
  "storage-connection-string=$STORAGE_CONNECTION_STRING"
)

# This deployment is for testing, which is what Seed__DemoData and DevTools__EnableDatabaseReset
# say out loud: the data here is disposable and the reset endpoint refuses to run without both.
ENV_VARS=(
  "ASPNETCORE_ENVIRONMENT=Production"
  "PORT=8080"
  "Swagger__Enabled=true"
  "ConnectionStrings__Default=secretref:connection-string"
  "Jwt__Issuer=PublicationSite.Api"
  "Jwt__Audience=PublicationSite.Client"
  "Jwt__SigningKey=secretref:jwt-signing-key"
  "Jwt__AccessTokenMinutes=30"
  "Jwt__RefreshTokenDays=14"
  "Seed__AdminEmail=$SEED_ADMIN_EMAIL"
  "Seed__AdminPassword=secretref:seed-admin-password"
  "Seed__DemoData=true"
  "DevTools__EnableDatabaseReset=true"
  # Where uploads go, for a database that has not been told yet. An administrator can change it in
  # System settings afterwards and this will not overrule them.
  "Storage__Provider=azure-blob"
  "Storage__AzureContainer=$BLOB_CONTAINER"
  "Storage__AzureConnectionString=secretref:storage-connection-string"
)

if [ -n "$FRONTEND_URL" ]; then
  ENV_VARS+=("Frontend__BaseUrl=$FRONTEND_URL" "Cors__AllowedOrigins__0=$FRONTEND_URL")
fi

if az containerapp show --name "$APP_NAME" --resource-group "$RESOURCE_GROUP" --output none 2>/dev/null; then
  az containerapp secret set \
    --name "$APP_NAME" --resource-group "$RESOURCE_GROUP" \
    --secrets "${SECRETS[@]}" --output none

  az containerapp update \
    --name "$APP_NAME" --resource-group "$RESOURCE_GROUP" \
    --image "$IMAGE" \
    --cpu "$CPU" --memory "$MEMORY" \
    --min-replicas "$MIN_REPLICAS" --max-replicas 1 \
    --set-env-vars "${ENV_VARS[@]}" --output none
else
  az containerapp create \
    --name "$APP_NAME" \
    --resource-group "$RESOURCE_GROUP" \
    --environment "$ENVIRONMENT_NAME" \
    --image "$IMAGE" \
    --target-port 8080 \
    --ingress external \
    --cpu "$CPU" --memory "$MEMORY" \
    --min-replicas "$MIN_REPLICAS" --max-replicas 1 \
    --secrets "${SECRETS[@]}" \
    --env-vars "${ENV_VARS[@]}" \
    --output none
fi

FQDN="$(az containerapp show --name "$APP_NAME" --resource-group "$RESOURCE_GROUP" \
  --query "properties.configuration.ingress.fqdn" -o tsv)"

echo
echo "API:     https://$FQDN"
echo "Health:  https://$FQDN/health"
echo "Swagger: https://$FQDN/swagger"
echo
if [ -z "$FRONTEND_URL" ]; then
  echo "The frontend is not deployed yet, so email links and CORS are unset. Deploy it with"
  echo "Api__BaseUrl=https://$FQDN and then run this script again to fill them in."
fi
