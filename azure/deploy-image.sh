#!/usr/bin/env bash
#
# Points the container app at the image built from the commit you are standing on, and nothing else.
#
# This exists because the deployment credential GitHub Actions would need cannot be created on the
# AIS tenant: registering an application is a directory permission ordinary accounts are not given,
# so the deploy job in the workflow skips itself and the last step stays manual. Everything up to
# that point is automatic, and this is the one command that finishes it.
#
# Nothing here touches configuration. Connection strings, secrets and environment variables were set
# by azure/deploy.sh and are left exactly as they are; only the image changes, which is the whole
# difference between this and a redeployment.
#
# Usage:
#   az login                  # once
#   ./azure/deploy-image.sh   # after CI has published the image for this commit
#
#   ./azure/deploy-image.sh sha-abc1234   # or name a tag yourself
set -euo pipefail

RESOURCE_GROUP="${RESOURCE_GROUP:-publication-research}"
APP_NAME="${APP_NAME:-publication-research-backend}"
REPOSITORY="${REPOSITORY:-javiertrombetta/publication-research-backend}"

# The commit's own tag, not "latest". Container Apps makes a new revision when the template changes,
# and "latest" is the same string every time, so deploying it can leave the revision already running
# in place and the deployment appears to have done nothing at all.
TAG="${1:-sha-$(git rev-parse --short=7 HEAD)}"

echo "==> Image: $REPOSITORY:$TAG"

# Asked for before deploying, because the alternative is a container app left pointing at a tag that
# does not exist, which fails minutes later as a pull error rather than here as a sentence.
if ! curl -fsS "https://hub.docker.com/v2/repositories/$REPOSITORY/tags/$TAG" >/dev/null 2>&1; then
  echo
  echo "That image is not on Docker Hub yet."
  echo
  echo "The build takes about five to eight minutes from the push. If you have just pushed, wait and"
  echo "run this again. If the commit was never pushed, push it first: the image is built by CI, not"
  echo "here."
  exit 1
fi

echo "==> Deploying to $APP_NAME in $RESOURCE_GROUP"
az containerapp update \
  --name "$APP_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --image "docker.io/$REPOSITORY:$TAG" \
  --output none

# A revision is created immediately but serves nothing until it is healthy, and asking the app a
# question in between gets you an answer from the revision being replaced. That is how a database
# reset once ran against the image it was meant to replace.
echo "==> Waiting for the new revision to take the traffic"
for _ in $(seq 1 40); do
  running="$(az containerapp revision list \
    --name "$APP_NAME" --resource-group "$RESOURCE_GROUP" \
    --query "[?properties.trafficWeight>\`0\`].properties.template.containers[0].image" -o tsv 2>/dev/null || true)"

  if [ "$running" = "docker.io/$REPOSITORY:$TAG" ]; then
    FQDN="$(az containerapp show --name "$APP_NAME" --resource-group "$RESOURCE_GROUP" \
      --query "properties.configuration.ingress.fqdn" -o tsv)"

    echo
    echo "Live: https://$FQDN"
    exit 0
  fi

  sleep 15
done

echo
echo "The new revision has not taken the traffic after ten minutes, which usually means it is"
echo "failing to start. What it is saying:"
echo
echo "  az containerapp logs show -g $RESOURCE_GROUP -n $APP_NAME --tail 100"
exit 1
