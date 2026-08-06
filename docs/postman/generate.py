#!/usr/bin/env python3
"""
Rebuilds the Postman collection from the API's own OpenAPI description.

The collection is a reference somebody reads to find out what the API does, so it has to say what
the API currently says. Written by hand it drifts: the committed one had fallen sixteen endpoints
behind and still carried wording that had been rewritten in the source months earlier, which is
worse than having no reference at all because it looks current.

Run it against a running instance, or against a saved spec:

    python3 docs/postman/generate.py                       # http://localhost:5020
    python3 docs/postman/generate.py --api https://host    # somewhere else
    python3 docs/postman/generate.py --spec swagger.json   # a file

Descriptions come from the XML documentation comments on the controllers, so the way to improve
what this produces is to improve those.
"""

import argparse
import json
import pathlib
import re
import urllib.request
from typing import Dict, List, Optional

HERE = pathlib.Path(__file__).parent
COLLECTION = HERE / "PublicationResearchBackend.postman_collection.json"
ENVIRONMENT = HERE / "PublicationResearchBackend.postman_environment.json"

# Kept from the previous collection so re-importing updates it in place rather than leaving two.
COLLECTION_ID = "084676b9-7af0-4ea4-9de0-61e3be6a4298"
ENVIRONMENT_ID = "424dde38-0048-4565-b8b7-4baf525701d0"

# Localhost, deliberately. A collection that points at a hosted instance stops working the day that
# instance is taken down, and it sends whatever you were experimenting with to a shared database.
# Anyone wanting the deployed one overrides base_url in the environment, which is what it is for.
DEFAULT_BASE_URL = "http://localhost:5020"

# Folder order, so the collection reads in the order somebody works through the system rather than
# alphabetically. Anything not named here follows, sorted.
FOLDER_ORDER = [
    "Health", "DevTools", "Auth", "Users", "Departments", "Containers", "Proposals",
    "Ethics", "Publications", "Committees", "SupervisorGroups", "Catalogue", "Dashboard",
    "Notifications", "ContainerMessages", "Settings", "WorkflowRules", "AuditLog",
    "Invitations", "Support",
]

# Saves the tokens so every other request in the collection just works.
LOGIN_TEST = [
    "const body = pm.response.json();",
    "if (body && body.data && body.data.accessToken) {",
    "    pm.collectionVariables.set('access_token', body.data.accessToken);",
    "    pm.collectionVariables.set('refresh_token', body.data.refreshToken);",
    "    console.log('Saved access_token and refresh_token to collection variables.');",
    "} else {",
    "    console.warn('Login did not return an accessToken. Check the response body.');",
    "}",
]

COLLECTION_DESCRIPTION = (
    "AIS Research Publication Site API. Generated from the live OpenAPI description by "
    "docs/postman/generate.py, so it says what the API says.\n\n"
    "Quick start:\n"
    "1. Run Auth > Login with your Admin (or any) credentials. The access and refresh tokens are "
    "saved automatically into collection variables.\n"
    "2. Every other request inherits Bearer auth from the collection using {{access_token}}.\n"
    "3. When the access token expires, run Auth > Refresh, which saves the new pair the same way.\n\n"
    "base_url is http://localhost:5020, so this works against an API you are running yourself and "
    "keeps experiments off any shared instance. Point it elsewhere by editing the environment."
)


def load_spec(api: Optional[str], spec: Optional[str]) -> dict:
    if spec:
        return json.loads(pathlib.Path(spec).read_text())

    base = (api or "http://localhost:5020").rstrip("/")

    # The document's version is in its path, and it is raised whenever endpoints are added, so a
    # list of versions written here goes stale exactly when this script is most needed. The first
    # candidate is read out of the source that declares it; the rest are there for an older
    # instance somebody is pointing at.
    candidates = [c for c in (declared_api_version(), "v1.2", "v1.1", "v1") if c]
    seen = set()

    for version in candidates:
        if version in seen:
            continue
        seen.add(version)
        try:
            with urllib.request.urlopen(f"{base}/swagger/{version}/swagger.json", timeout=30) as f:
                return json.load(f)
        except Exception:
            continue

    raise SystemExit(
        f"Could not read the OpenAPI description from {base} at any of {', '.join(candidates)}. "
        "Start the API, or pass --spec."
    )


def declared_api_version() -> Optional[str]:
    """The version the API says it answers as, read from the one file that declares it."""
    source = HERE.parent.parent / "src" / "PublicationSite.Api" / "Common" / "ApiVersion.cs"
    if not source.exists():
        return None

    found = re.search(r'Current\s*=\s*"([^"]+)"', source.read_text())
    return found.group(1) if found else None


def resolve(schema: dict, spec: dict, depth: int = 0) -> object:
    """A skeleton request body from a schema: the shape, with empty values to fill in."""
    if depth > 4 or not isinstance(schema, dict):
        return None

    if "$ref" in schema:
        name = schema["$ref"].rsplit("/", 1)[-1]
        return resolve(spec.get("components", {}).get("schemas", {}).get(name, {}), spec, depth + 1)

    for key in ("allOf", "oneOf", "anyOf"):
        if key in schema and schema[key]:
            return resolve(schema[key][0], spec, depth + 1)

    kind = schema.get("type")
    if kind == "object" or "properties" in schema:
        return {k: resolve(v, spec, depth + 1) for k, v in schema.get("properties", {}).items()}
    if kind == "array":
        return [resolve(schema.get("items", {}), spec, depth + 1)]
    if kind == "boolean":
        return False
    if kind in ("integer", "number"):
        return 0
    if schema.get("format") == "uuid":
        return "00000000-0000-0000-0000-000000000000"
    return ""


def build_request(path: str, method: str, op: dict, spec: dict) -> dict:
    # {id:guid} in the route becomes :id in Postman, which is what makes it a path variable.
    segments = [s for s in re.sub(r"\{([^}:]+)(?::[^}]+)?\}", r":\1", path).strip("/").split("/") if s]

    query = [
        {"key": p["name"], "value": "", "description": p.get("description", ""), "disabled": True}
        for p in op.get("parameters", [])
        if p.get("in") == "query"
    ]

    url: dict = {
        "raw": "{{base_url}}/" + "/".join(segments) + ("?" + "&".join(q["key"] + "=" for q in query) if query else ""),
        "host": ["{{base_url}}"],
        "path": segments,
    }
    if query:
        url["query"] = query

    variables = [
        {"key": p["name"], "value": "", "description": p.get("description", "")}
        for p in op.get("parameters", [])
        if p.get("in") == "path"
    ]
    if variables:
        url["variable"] = variables

    request: dict = {"method": method.upper(), "header": [], "url": url}

    body = op.get("requestBody", {}).get("content", {})
    if "application/json" in body:
        request["header"].append({"key": "Content-Type", "value": "application/json"})
        skeleton = resolve(body["application/json"].get("schema", {}), spec)
        request["body"] = {
            "mode": "raw",
            "raw": json.dumps(skeleton, indent=2),
            "options": {"raw": {"language": "json"}},
        }
    elif "multipart/form-data" in body:
        schema = body["multipart/form-data"].get("schema", {})
        resolved = schema if "properties" in schema else {}
        request["body"] = {
            "mode": "formdata",
            "formdata": [
                {"key": name, "type": "file" if prop.get("format") == "binary" else "text", "value": ""}
                for name, prop in resolved.get("properties", {}).items()
            ],
        }

    # Which requests must not send the collection's bearer token, taken from the description itself:
    # an operation that carries no security requirement is one the API serves without a token.
    # This was a list written out here, and a list is a second opinion that goes wrong: it had every
    # catalogue route as open, while downloading the full text of a paper has always needed an
    # account.
    if not op.get("security"):
        request["auth"] = {"type": "noauth"}

    summary = (op.get("summary") or "").strip()
    description = "\n\n".join(p for p in [summary, (op.get("description") or "").strip()] if p)
    if description:
        request["description"] = description

    name = summary.split(".")[0].strip() if summary else f"{method.upper()} {path}"
    item: dict = {"name": name or path, "request": request, "response": []}

    if path.rstrip("/") in ("/api/auth/login", "/api/auth/refresh"):
        item["name"] = "Login" if path.rstrip("/").endswith("login") else "Refresh"
        item["event"] = [{"listen": "test", "script": {"type": "text/javascript", "exec": LOGIN_TEST}}]

    return item


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--api", help="Base URL of a running API")
    parser.add_argument("--spec", help="Path to a saved swagger.json")
    parser.add_argument("--base-url", default=DEFAULT_BASE_URL, help="What {{base_url}} should be")
    args = parser.parse_args()

    spec = load_spec(args.api, args.spec)

    folders: Dict[str, List[dict]] = {}
    for path, operations in spec.get("paths", {}).items():
        for method, op in operations.items():
            if method.lower() not in ("get", "post", "put", "patch", "delete"):
                continue
            tag = (op.get("tags") or ["Other"])[0]
            folders.setdefault(tag, []).append(build_request(path, method, op, spec))

    ordered = [t for t in FOLDER_ORDER if t in folders] + sorted(set(folders) - set(FOLDER_ORDER))

    collection = {
        "info": {
            "_postman_id": COLLECTION_ID,
            "name": "Publication Research Backend",
            "description": COLLECTION_DESCRIPTION,
            "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json",
        },
        "auth": {"type": "bearer", "bearer": [{"key": "token", "value": "{{access_token}}", "type": "string"}]},
        "variable": [
            {"key": "base_url", "value": args.base_url, "type": "string"},
            {"key": "access_token", "value": "", "type": "string"},
            {"key": "refresh_token", "value": "", "type": "string"},
        ],
        "item": [{"name": tag, "item": folders[tag]} for tag in ordered],
    }

    COLLECTION.write_text(json.dumps(collection, indent=2) + "\n")

    environment = {
        "id": ENVIRONMENT_ID,
        "name": "Publication Research Backend",
        "values": [
            {"key": "base_url", "value": args.base_url, "type": "default", "enabled": True},
            {"key": "access_token", "value": "", "type": "secret", "enabled": True},
            {"key": "refresh_token", "value": "", "type": "secret", "enabled": True},
        ],
        "_postman_variable_scope": "environment",
    }
    ENVIRONMENT.write_text(json.dumps(environment, indent=2) + "\n")

    total = sum(len(v) for v in folders.values())
    print(f"{total} requests across {len(ordered)} folders, pointing at {args.base_url}")


if __name__ == "__main__":
    main()
