# Azure

`deploy.sh` puts the API on Azure Container Apps and creates what it needs: the resource group, the
Container Apps environment shared with the frontend, and a storage account for uploads.

It needs four values that do not belong in a repository:

| Variable | What it is |
|---|---|
| `CONNECTION_STRING` | Aiven's MySQL connection string, including `SslMode=Required` |
| `JWT_SIGNING_KEY` | 32 characters or more of randomness. Changing it signs everyone out |
| `SEED_ADMIN_EMAIL` | The administrator account created on first start |
| `SEED_ADMIN_PASSWORD` | Its password |

```bash
az login
az account set --subscription "Azure for Students"

export CONNECTION_STRING='Server=...;SslMode=Required;TreatTinyAsBoolean=true;'
export JWT_SIGNING_KEY="$(openssl rand -base64 48)"
export SEED_ADMIN_EMAIL='you@ais.ac.nz'
export SEED_ADMIN_PASSWORD='...'

./azure/deploy.sh
```

Safe to run again: it updates what is there rather than recreating it, which is also how you change
a setting later.

The full walkthrough, the costs and the troubleshooting are in `docs/azure.md`.
