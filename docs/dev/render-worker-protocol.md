# Render worker protocol (version 1)

`therion-cli render-worker --server URL --token-file FILE` makes a machine with Blender (a GPU
machine, a *render node*) take render jobs from a server, such as SilexGIS, that stores render
bundles. This page is for whoever writes that server. The node pulls: the server never starts a
process and never reaches the node; it answers the node's calls.

- C# messages: `src/Therion.Blender/RenderNodeProtocol.cs` (`RenderNodeClaim`, `RenderNodeJob`,
  `RenderNodeHeartbeat`, `RenderNodeHeartbeatReply`, `RenderNodeComplete`, `RenderNodeFail`,
  `RenderNodeFailure`).
- JSON schema: `src/Therion.Blender/render-node.schema.json`, one `$defs` entry for each message.
  Its objects are closed (`additionalProperties: false`): it describes what a sender writes. A
  receiver must not hold incoming bodies to that, because it ignores unknown properties; use
  `RenderNodeProtocol.Validate(messageKind, json)` (below) instead.
- Receiver-side check: `RenderNodeProtocol.Validate(string messageKind, string json)` returns the
  problems of a body (empty when it is good). `messageKind` is one of
  `RenderNodeProtocol.MessageKinds`: `claim`, `job`, `heartbeat`, `heartbeatReply`, `complete`,
  `fail`; any other value throws `ArgumentException`. It holds the body to the schema with unknown
  properties accepted; a missing required property, a wrong type or value, a `protocol` other than
  1 or text that is not JSON is a problem (each names its path, e.g. `$.protocol: must be 1`).
  A server calls it on a claim, heartbeat, complete and fail it receives, and answers 400 when the
  list is not empty. It is a plain function of the two strings, with no reference to the
  execution assembly.
- The worker: `src/Therion.Blender.Execution/RenderNodeWorker.cs`.

## Basics

| Point | Rule |
|---|---|
| Endpoints | relative to the URL given with `--server` (a folder: `https://example.org/api/v1/render-nodes/`) |
| Transport | HTTPS. Plain HTTP only to this machine (`localhost`, `127.0.0.1`, `::1`); anything else is refused before a call is made. Redirects are not followed |
| Auth | `Authorization: Bearer <node token>` on every call to the server's own origin (scheme, host, port). The token comes from the first line of `--token-file`; on Unix the worker refuses a file that others may read or write, and a link. A bundle URL on another origin (a storage service with its own signed URL) gets no token |
| Bodies | JSON, UTF-8, `application/json`; property names in camelCase; absent values are left out, never `null`. Receivers ignore properties they do not know |
| Version | `protocol: 1` in a claim and in the job the server gives. A server that does not speak the claim's version answers 400; a job of another version is failed with `badJob` |
| Job id | `^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$` (ECMA-262: `$` is the end of the string, so a line break after the id is refused): it names the node's scratch folder and the result files. The server's row id (`"1234"`) is fine |
| Attempt | the server's try count for the job, from 1, given with the claim; every later call names it. A call for an attempt that is no longer the current one is answered **409** |
| Idempotency | every call may be retried with the same job id and attempt: a heartbeat renews again, a PUT of a file replaces the same bytes, a second `complete` or `fail` for an attempt already ended is answered 200 (or 409) and changes nothing |

### What the node does with an answer

A server never needs to answer **403**: a token that is bad, unknown or withdrawn is **401**. The
node treats 403 like 401, but there is no case in the protocol that calls for it.

| Answer | Claim | Heartbeat | Upload, complete, fail |
|---|---|---|---|
| 2xx | 200: a job; 204: none | renews the lease | done |
| 401, 403 | the worker stops (exit 3); Blender is killed, frames kept | same | same |
| 404, 409, 410 | retried later | **lease lost**: Blender is killed, nothing more is sent for the job, frames kept | lease lost |
| 408, 429, 5xx, network error | retried after a wait | retried at the next heartbeat; when no heartbeat got through for a whole lease, the lease is lost | retried after a wait while the lease holds |
| 413 | | | the job fails with `upload` |
| other 4xx | retried later | logged | the job fails with `upload` (files) |

Waits after failures: 2 s doubled each time up to 5 minutes, each spread by a factor of 0.5 to 1.0
(`RenderNodeProtocol.Backoff`). `Retry-After` is not read.

## Endpoints

### `POST claim` — ask for a job

```json
{ "protocol": 1, "node": "gpu-1", "worker": "0.4.0.0", "blenderVersions": ["4.5.16"],
  "device": "GPU (OPTIX)", "freeDiskBytes": 412000000000, "containers": ["mp4", "mkv", "webm"],
  "keptJobs": ["1234"], "waitSeconds": 30 }
```

- `waitSeconds`: the server may hold the request open that long waiting for a job (a long poll),
  or answer at once. After 204 the node claims again when `--poll-seconds` (default 30) have gone
  by since the claim started. The node's timeout for a claim is `waitSeconds` + 30 s.
- `keptJobs`: jobs whose frames the node still holds. Give such a job back to the node that keeps
  it when it is claimable: the render goes on from its frames instead of starting again.
- `device` is the one the node's last render used; absent before its first.

**200** — the job, now leased to the node:

```json
{ "protocol": 1, "jobId": "1234", "attempt": 1, "leaseSeconds": 120, "heartbeatSeconds": 30,
  "name": "Invented tour",
  "bundle": { "url": "/api/v1/render-nodes/jobs/1234/bundle", "size": 48213114,
              "sha256": "<64 lower-case hex digits>" },
  "plan": "full", "container": "mp4",
  "limits": { "maxHours": 24, "maxTriangles": 50000000, "maxResultBytes": 4294967296 } }
```

| Field | Meaning |
|---|---|
| `leaseSeconds` | how long the job stays the node's without a heartbeat (at least 3) |
| `heartbeatSeconds` | optional; the node uses it when it is at most a third of the lease, a third of the lease otherwise |
| `bundle.url` | absolute, or relative to the server URL; HTTPS (or loopback HTTP) |
| `bundle.size`, `bundle.sha256` | checked against what is downloaded before anything of it is read |
| `plan` | `full` plays `scene-plan.json`; `draft` plays `scene-plan.draft.json` (a bundle written with a draft) |
| `container` | `mp4` (default when absent), `webm` or `mkv`: the movie is written in it whatever the plan says. A plan that writes frames or a still keeps them. Another value fails the job with `badJob` |
| `limits` | optional; each can only lower the node's own limit (`maxHours`, `maxUnpackedBytes`, `maxTriangles`, `maxWork` = frames × pixels × samples). `maxResultBytes`: a result file larger fails the job with `upload` before it is sent |

**204** — no job.

### `GET <bundle.url>` — the bundle

The zip, streamed. The node writes it to its scratch folder, stops reading past `bundle.size`, and
fails the job with `checksum` (retryable) when the size or the SHA-256 differ. It tries 5 times
on network errors and 5xx before failing with `download`. A bundle that the node already holds
from an earlier try, with the same size and SHA-256, is not downloaded again.

The server answers **409** when the node does not hold the job (it was not leased to it, or it
was taken back); the node then counts the lease as lost and does not retry the download.

### `POST jobs/{jobId}/heartbeat` — progress; renews the lease

Sent from the claim to the end of the job (download and upload included), every heartbeat interval.

```json
{ "attempt": 1, "phase": "rendering", "message": "Rendering frame 37/240", "segment": 1,
  "segmentCount": 3, "frame": 37, "frameCount": 240, "secondsPerFrame": 2.41, "secondsLeft": 489.2 }
```

- `phase`: `claimed`, `downloading`, `checking`, `rendering`, `composing`, `collectingOutputs`,
  `done`, `uploading`.
- `frame` / `frameCount` count over the whole job (every shot of a film), when known.

**200**:

```json
{ "action": "continue" }
```

`action` is `continue` or `cancel`. With `cancel` the node kills Blender's process tree, sends
`fail` with reason `cancelled`, uploads nothing and deletes the job's scratch folder. An optional
`leaseSeconds` changes the lease from then on. **409** (or 404, 410): the job is no longer this
attempt's; the node kills Blender and sends nothing more for it.

### `PUT jobs/{jobId}/files/{name}?attempt=N` — one result file

The body is the file (`application/octet-stream`, `Content-Length` set);
`X-Content-SHA256: <64 lower-case hex digits>` is its SHA-256. The server checks it and answers
400 on a mismatch (the node tries again). A second PUT of the same name replaces the first.

Only files the worker names are sent, and only after a render that succeeded:

| Name | Role |
|---|---|
| `<jobId>.mp4` / `.webm` / `.mkv` | `movie` |
| `<jobId>.png`, `<jobId>_0001.png`… | `still` (a plan that writes a still or frames) |
| `<jobId>.srt`, `<jobId>.vtt` | `subtitles` (when the film has captions) |
| `<jobId>.log` | `log`: Blender's output |
| `<jobId>.status.json` | `status`: the job's record (state, result, device, seconds per frame, notices; file names, not the node's paths) |

Names match `^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$`, to the end of the string (nothing, not even a line break, after the name). One PUT per file; there are no chunked uploads
in version 1, so the server's request size limit must allow the largest movie it takes.

### `POST jobs/{jobId}/complete` — the job succeeded

After every file was uploaded.

```json
{ "attempt": 1, "files": [ { "name": "1234.mp4", "role": "movie", "size": 81234567,
  "sha256": "<hex>" }, … ], "device": "GPU (OPTIX)", "secondsPerFrame": 2.4, "seconds": 612.3,
  "frameCount": 240, "notices": [] }
```

The server checks that each file listed is the one it holds (name, size, SHA-256), and answers 200.

### `POST jobs/{jobId}/fail` — the job failed on this node

```json
{ "attempt": 1, "reason": "refused", "message": "scene-plan.json: the plan names module folders…",
  "retryable": false }
```

| Reason | When | Retryable |
|---|---|---|
| `refused` | the bundle broke a rule (paths leaving it, links, module folders, features outside the player's, plans that do not validate, a `.blend` output, a missing draft plan, sizes, triangles, work). Nothing of it was played | no |
| `badJob` | the claim's job cannot be taken: protocol, attempt, plan, container, bundle size over the node's limit, a SHA-256 not as written, a bundle URL that is not allowed | no |
| `checksum` | the bundle downloaded is not the one described | yes |
| `download` | the bundle could not be downloaded | yes |
| `upload` | a result could not be uploaded, or is larger than `maxResultBytes` | yes |
| `cancelled` | the server cancelled the job (sent after a heartbeat's `cancel`; answer 200 or 409, both are fine) | no |
| `timedOut` | the job ran longer than its time limit; its frames stay on the node | no |
| `blenderNotFound`, `blenderTooOld` | the node has no usable Blender | yes |
| `diskSpace` | the node's disk is too full | yes |
| `scriptError`, `noOutput` | the player failed on the plans | no |
| `crashed`, `error` | Blender stopped, or the node failed otherwise | yes |

`retryable` is a hint; the server decides whether to queue the job again.

## A job's life on the node

1. Claim. The job's scratch folder is `<scratch>/<jobId>/`.
2. Download to `bundle.zip`; check its size and SHA-256.
3. Unpack and check it as data, as the folder worker does: only the plans and the files they name
   leave the zip; its own player and scripts are never run; Blender runs with `-b --factory-startup`.
4. Render, with heartbeats. The frames are kept in `<scratch>/<jobId>/frames/`.
5. Upload the files, then `complete`; or `fail`. The heartbeats end before `complete` is sent,
   so a server may answer a heartbeat of a job it has completed with 409 without harm; once
   `complete` is answered 200 the job has succeeded on the node. A download or an upload that
   moves no byte for 60 s is given up and tried again. The scratch folder is deleted after
   success, a refused bundle and a cancel; after a lost lease, a time-out or a failure in Blender
   the frames (and the bundle) stay, so that the same job claimed again on this node goes on from
   them, for 14 days untouched.
6. Claim the next.

A node stopped with Ctrl+C or SIGTERM kills Blender, keeps the frames and sends nothing: the job's
lease runs out on the server, which queues it again. A node that is killed takes Blender with it
(on Windows and Linux). A fault of the node's with one job (a disk that fails, a job it cannot
handle) is sent as `fail` with `error`, and the node goes on with the next claim. A bundle whose
plans cannot even be read is `refused`, not `error`.

## On a SilexGIS `ProcessingJob`

| ProcessingJob | Protocol |
|---|---|
| `Kind` | a kind of its own, for instance `blender-render` |
| `Payload` | the stored bundle (its id, size and SHA-256), `plan`, `container`, who asked; the claim's job is built from it |
| `Status` `Queued` → `Running` | a claim that hands the job out sets it, `StartedAt`, `LeaseExpiresAt` = now + `leaseSeconds`, and increments `Attempts` (= `attempt`) |
| `LeaseExpiresAt` | renewed by each heartbeat; a job whose lease ran out is taken back by the existing sweep and claimable again |
| `ProgressDone` / `ProgressTotal` | the heartbeat's `frame` / `frameCount` |
| `Succeeded` | on `complete`, once the files are checked and stored |
| `Failed`, `Error` | on `fail`: `reason: message`; `retryable` may put it back to `Queued` with a `NotBefore` |
| cancel | the status has no cancelled: the server needs either a new status `Cancelled`, or a flag beside the row read at the next heartbeat and ending as `Failed` with `Error` = `cancelled: …`. The node learns it at its next heartbeat (`action: cancel`) |
| lanes | the general lane claims every kind it is not told about: the new kind must be excluded from it (a lane of its own claimed only by nodes), or the in-process worker takes a job it has no handler for |
