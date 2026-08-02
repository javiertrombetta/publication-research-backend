# Running this on Azure for Students

Both applications as containers on Azure Container Apps, with the database staying on Aiven. This
is a testing deployment: the sample dataset is on and the database reset endpoint works, which is
what makes the data here disposable.

## Why Container Apps and not App Service

An Azure for Students subscription is a fixed grant of credit, so the question is not the monthly
price but how long the grant lasts.

| | Cost | What that means for a $100 grant |
|---|---|---|
| **Container Apps, scale to zero** | Billed by the second, with a monthly free allowance per subscription. Nothing runs between requests. | Effectively free for testing. The grant is spent on storage and little else. |
| App Service, B1 Linux | About US$13 per app per month, billed whether or not anybody visits. | Two apps is about US$26 a month, so the grant is gone in under four months. |
| Container Instances | No scale to zero. Billed continuously. | Similar to App Service, without the conveniences. |

Container Apps is the only one of the three where an idle testing deployment costs nothing, which
is what a testing deployment mostly is.

Scaling to zero costs almost nothing and makes the first request after a quiet period wait ten to
thirty seconds. The scripts therefore keep one replica awake by default (`MIN_REPLICAS=1`), because
a testing deployment somebody is being shown is the wrong place for that wait.

A replica that is running but not answering anything is billed at Azure's reduced idle rate, which
is what makes this affordable: measured on a live deployment, an idle request stays at about 100 ms
whether or not it follows a quiet spell, and there is no cold start at all.

To go back to paying nearly nothing, export `MIN_REPLICAS=0` and run the script again. Two things
follow from scaling to zero: the background sweep that closes expired proposal rounds only runs
while a replica is awake, and the first request after a database reset is slow because the sample
dataset is built on startup.

## What it will cost

| Resource | Plan | Roughly |
|---|---|---|
| Container Apps environment | Consumption | Nothing while idle |
| Two container apps | 0.5 vCPU, 1 GiB, one replica always awake | About US$24 a month at the idle rate, less the free monthly allowance |
| Storage account for uploads | Standard LRS, hot | Cents per month at this size |
| Log Analytics workspace | Pay as you go | First 5 GB of ingestion each month is free |
| MySQL | **Stays on Aiven** | Unchanged |

Those are estimates from the published rates and the free monthly allowance, not a quote. Watch the
real figure in Cost Management for the first week.

On a US$100 grant, two always-awake apps at that size last roughly four months. Halve it by dropping
to the smallest size Container Apps offers, which is plenty for testing:

```bash
CPU=0.25 MEMORY=0.5Gi ./azure/deploy.sh
```

Set a budget alert anyway: Azure portal, Cost Management, Budgets. On a student subscription
Azure stops the resources when the grant runs out rather than charging you, but knowing beforehand
is better than finding out.

## Before you start

1. **Install the Azure CLI** and the Container Apps extension.

   ```bash
   brew install azure-cli
   az extension add --name containerapp --upgrade
   az provider register --namespace Microsoft.App --wait
   az provider register --namespace Microsoft.OperationalInsights --wait
   az provider register --namespace Microsoft.Storage --wait
   ```

   The deploy scripts do this too, so you can skip it. It is here because a subscription that has
   never used one of these reports the failure as `SubscriptionNotFound`, which sends you looking
   at the subscription when the subscription is fine.

2. **Sign in** with the account that holds the student subscription.

   ```bash
   az login
   az account list --output table
   az account set --subscription "Azure for Students"
   ```

3. **Have the Aiven connection string to hand.** Aiven requires TLS, so it needs `SslMode=Required`:

   ```
   Server=<host>;Port=<port>;Database=defaultdb;User=avnadmin;Password=<password>;SslMode=Required;TreatTinyAsBoolean=true;
   ```

   In the Aiven console, check that the service allows connections from anywhere, or add the
   container app environment's outbound IP once it exists
   (`az containerapp env show ... --query properties.staticIp`). Aiven's free plans also power the
   service down when it has been idle for a while; the first request after that wakes it and is
   slow.

## Deploying, the first time

The two applications need each other's addresses, and neither exists yet, so the order is: API,
then site, then the API again to tell it where the site ended up.

**1. The API.**

```bash
cd publication-research-backend

export CONNECTION_STRING='Server=...;SslMode=Required;TreatTinyAsBoolean=true;'
export JWT_SIGNING_KEY="$(openssl rand -base64 48)"
export SEED_ADMIN_EMAIL='you@ais.ac.nz'
export SEED_ADMIN_PASSWORD='<something long>'

./azure/deploy.sh
```

It creates the resource group, the Container Apps environment, a storage account for uploads and
the app itself, then prints the address. Keep the signing key somewhere: changing it signs everyone
out.

**2. The site.**

```bash
cd ../publication-research-frontend
./azure/deploy.sh
```

It reads the API's address out of the deployment rather than asking you for it, so the two cannot
drift apart. It prints the site's address.

**3. The API again**, now that there is a site to point at:

```bash
cd ../publication-research-backend
./azure/deploy.sh
```

This fills in `Frontend__BaseUrl` and the CORS origin, which are what the links in verification and
password-reset emails use.

**4. Check it.**

```bash
curl https://<api>.azurecontainerapps.io/health
open https://<site>.azurecontainerapps.io
```

Sign in with `admin.test@ais.ac.nz` and `DevTest123!` once the sample dataset has finished building
(`GET /api/dev/demo-data` says how far along it is).

## Deploying afterwards

Two ways, and they do different things.

**A new image**, which is what happens on every push to `main`: GitHub Actions builds it, pushes it
to Docker Hub and updates the container app. Set these once per repository, under Settings:

| Where | Name | Value |
|---|---|---|
| Secrets | `AZURE_CREDENTIALS` | the JSON from the command below |
| Variables | `AZURE_RESOURCE_GROUP` | `publication-research` |
| Variables | `AZURE_APP_NAME` | `publication-research-backend` or `-frontend` |

```bash
az ad sp create-for-rbac \
  --name "publication-research-deploy" \
  --role contributor \
  --scopes "/subscriptions/$(az account show --query id -o tsv)/resourceGroups/publication-research" \
  --sdk-auth
```

Paste the whole JSON object as `AZURE_CREDENTIALS`. The scope is that one resource group, so the
credential cannot touch anything else in the subscription. Without `AZURE_RESOURCE_GROUP` set, the
deploy job is skipped and the workflow behaves exactly as it did before.

**A changed setting**: run `./azure/deploy.sh` again. It updates what is there rather than
recreating it.

## Where uploaded files go

A container app has no disk that survives a restart, so ethics documents, paper versions and
profile photos cannot live on one. The API's deploy script creates a storage account and starts the
deployment on Azure Blob Storage.

That setting normally lives in the database, which is right: an administrator can change it in
System settings without a redeploy. The awkward part is that this is a testing deployment where the
database gets reset, and a reset would put storage back to a local directory and lose everything
uploaded since. So the environment states a starting point, and the application writes it once when
the database has no value of its own. An administrator changing it afterwards is never overruled on
the next restart.

You can still switch destination from System settings, File storage, and copy what is already
stored to the new one from the same screen.

## Turning it off

```bash
az group delete --name publication-research --yes
```

That removes both apps, the environment, the storage account and the logs. It does not touch Aiven,
which is a separate account.

To stop paying without losing anything, scale the apps to nothing instead:

```bash
az containerapp update -g publication-research -n publication-research-backend --min-replicas 0 --max-replicas 0
```

## When something is wrong

```bash
# What the app is saying
az containerapp logs show -g publication-research -n publication-research-backend --follow

# What it thinks its configuration is (secrets show as references, not values)
az containerapp show -g publication-research -n publication-research-backend \
  --query "properties.template.containers[0].env" -o table

# Whether a revision actually started
az containerapp revision list -g publication-research -n publication-research-backend -o table
```

A replica that starts and stops repeatedly is almost always the database: the connection string is
wrong, `SslMode=Required` is missing, or Aiven is asleep or blocking the address.
