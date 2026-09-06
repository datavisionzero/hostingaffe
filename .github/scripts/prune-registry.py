#!/usr/bin/env python3
"""Work out which versions of the container package have stopped being useful.

Every commit on the trunk sets a `sha-<commit>` tag, and nothing has ever taken
one away. The tag earns its place while it is young: it is what an installation
pins itself to when the trunk turns out bad, without waiting for the next
commit. A `sha-` tag from half a year ago pins nobody — it only makes the
package page unreadable.

So the rule is narrow on purpose, and it is a rule about the `sha-` prefix
rather than about age alone:

  * A version carrying any tag that is not `sha-` is never touched. `:main`,
    `:latest` and every release tag fall out here, whatever else they carry.
  * A `sha-` version is a candidate only when it is both older than the window
    and outside the newest few. Either alone would be enough on a quiet week
    and wrong on a busy one.
  * An untagged version goes only when nothing that survives refers to it. The
    manifests under a multi-architecture index are untagged too, and so is the
    attestation buildx attaches, so "untagged" on its own is not a fact about
    whether something is still in use.

Nothing is deleted here. This prints the version ids to delete, and the
workflow hands them to the action that does it.
"""

import json
import os
import sys
import urllib.error
import urllib.request
from datetime import datetime, timedelta, timezone

OWNER = os.environ["OWNER"]
PACKAGE = os.environ["PACKAGE"]
TOKEN = os.environ["GH_TOKEN"]
KEEP_NEWEST = int(os.environ.get("KEEP_NEWEST", "20"))
MAX_AGE_DAYS = int(os.environ.get("MAX_AGE_DAYS", "60"))
# The action takes them in one input, and a run that would delete more than
# this is a run worth looking at before it does. The schedule catches up.
MAX_PER_RUN = int(os.environ.get("MAX_PER_RUN", "100"))

CUTOFF = datetime.now(timezone.utc) - timedelta(days=MAX_AGE_DAYS)

# What a manifest may be. Without these the registry answers with the schema-1
# manifest of whichever architecture it feels like, and the children are lost.
MANIFEST_TYPES = ", ".join(
    [
        "application/vnd.oci.image.index.v1+json",
        "application/vnd.oci.image.manifest.v1+json",
        "application/vnd.docker.distribution.manifest.list.v2+json",
        "application/vnd.docker.distribution.manifest.v2+json",
    ]
)


def created(version):
    """The API stamps a trailing `Z`, which only Python 3.11 and up reads."""
    return datetime.fromisoformat(version["created_at"].replace("Z", "+00:00"))


def get_json(url, headers):
    request = urllib.request.Request(url, headers=headers)
    with urllib.request.urlopen(request, timeout=30) as answer:
        return json.load(answer)


def versions():
    """Every version of the package, newest first, as the API pages them."""
    page = 1
    while True:
        batch = get_json(
            f"https://api.github.com/users/{OWNER}/packages/container/{PACKAGE}"
            f"/versions?per_page=100&page={page}",
            {
                "Authorization": f"Bearer {TOKEN}",
                "Accept": "application/vnd.github+json",
                "X-GitHub-Api-Version": "2022-11-28",
            },
        )
        if not batch:
            return
        yield from batch
        page += 1


def registry_token():
    """A pull token. The package is public, so this needs no credentials."""
    return get_json(
        f"https://ghcr.io/token?service=ghcr.io&scope=repository:{OWNER}/{PACKAGE}:pull",
        {},
    )["token"]


def children(digest, token):
    """The digests one manifest refers to, or nothing when it refers to none."""
    try:
        manifest = get_json(
            f"https://ghcr.io/v2/{OWNER}/{PACKAGE}/manifests/{digest}",
            {"Authorization": f"Bearer {token}", "Accept": MANIFEST_TYPES},
        )
    except urllib.error.HTTPError as error:
        # A manifest that is not there refers to nothing, and a registry that
        # will not answer is not a licence to delete: say so and keep going,
        # having assumed the safe half.
        print(f"  ! {digest}: {error.code}, treated as referring to nothing")
        return set()

    referred = set()
    for child in manifest.get("manifests", []):
        referred.add(child["digest"])
    for layer in [manifest.get("config")] + manifest.get("layers", []):
        # Layers are blobs, not versions; only manifests appear in the package
        # listing. Collected anyway so the set is the whole truth about what
        # this manifest points at.
        if layer:
            referred.add(layer["digest"])
    return referred


def main():
    tagged_keep, sha_versions, untagged = [], [], []

    for version in versions():
        tags = version["metadata"]["container"]["tags"]
        if not tags:
            untagged.append(version)
        elif all(tag.startswith("sha-") for tag in tags):
            sha_versions.append(version)
        else:
            tagged_keep.append(version)

    # Newest first, so that "the newest few" is the head of the list. The API
    # orders them this way already; sorting says so rather than relying on it.
    sha_versions.sort(key=created, reverse=True)

    doomed = [
        version
        for version in sha_versions[KEEP_NEWEST:]
        if created(version) < CUTOFF
    ]
    doomed_ids = {version["id"] for version in doomed}

    print(
        f"{len(tagged_keep)} versions carry a tag that is not `sha-`, "
        f"{len(sha_versions)} carry only `sha-` tags, {len(untagged)} carry none."
    )
    print(
        f"Of the `sha-` ones, {len(doomed)} are both older than {MAX_AGE_DAYS} days "
        f"and outside the newest {KEEP_NEWEST}."
    )

    # What everything that survives points at. An untagged version in this set
    # is a live child, whatever its age.
    survivors = tagged_keep + [v for v in sha_versions if v["id"] not in doomed_ids]
    token = registry_token()
    referenced = set()
    for version in survivors:
        referenced |= children(version["name"], token)

    orphans = [
        version
        for version in untagged
        if version["name"] not in referenced
        and created(version) < CUTOFF
    ]
    print(
        f"{len(orphans)} untagged versions are older than {MAX_AGE_DAYS} days and "
        f"referred to by nothing that stays."
    )

    going = doomed + orphans
    for version in going:
        tags = version["metadata"]["container"]["tags"]
        print(f"  - {version['id']} {version['created_at']} {tags or '(untagged)'}")

    if len(going) > MAX_PER_RUN:
        print(f"Capped at {MAX_PER_RUN}; the rest goes on the next run.")
        going = going[:MAX_PER_RUN]

    ids = ",".join(str(version["id"]) for version in going)
    if output := os.environ.get("GITHUB_OUTPUT"):
        with open(output, "a", encoding="utf-8") as handle:
            handle.write(f"ids={ids}\n")
            handle.write(f"count={len(going)}\n")
    print(f"\n{len(going)} to delete.")


if __name__ == "__main__":
    sys.exit(main())
