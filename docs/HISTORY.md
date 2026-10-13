# Opt-in local completion history

Local history is **off by default**. To retain completion records, start sharing and enable **Keep local completion history (optional)** in the Windows history panel. The app stores up to 200 recent completed records under its local application data directory. The remembered preference applies to later sharing runs.

History records include only a separate record ID, the actual completed filename, size, direction and completion time. They contain no source/destination paths, device names, session/pairing/admin credentials, certificate details or file contents. Filenames may still contain personal information. This is local metadata stored as ordinary JSON; it is separate from the [anonymous diagnostic summary](DIAGNOSTICS.md), which never includes history filenames.

An incoming record means the PC completed the length, checksum and stored-file checks before committing the file. An outgoing record means the PC prepared a file for an approved phone. It does **not** confirm that the phone downloaded or saved it. Historical records are not file links and grant no download access; they do not prove that a file still exists or still has the same bytes.

## Restart and privacy controls

- The Windows app can read saved history while sharing is stopped, including after restarting.
- Changing the preference or clearing records requires a running sharing server and its local administration channel.
- Disabling history stops new records but keeps existing ones. Use **Clear local history records** to remove the stored records.
- Clearing history removes only the metadata, never completed received files, live prepared files, queues or permission grants. Clearing does not disable future recording if history remains enabled.
- Restarting restores history only; old phone sessions, approvals, queues and download access are not restored. Check actual received files before retrying after a lost confirmation.

Only completed PC saves and outgoing preparations are recorded. Pending, failed and cancelled transfers do not become completion history. Recording is best-effort: a history write failure or crash between committing a file and its metadata may leave a completed file without a history row. A history row is not a substitute for inspecting the receive folder after an ambiguous response.

## Storage limits and errors

The history store uses a bounded, validated JSON checkpoint and a writer lease. Another server using the same app data directory cannot write concurrently. If storage is unavailable, malformed or redirected, history reports an error without failing normal transfers or silently replacing unknown/corrupt data. Review the local history status; [diagnostics](DIAGNOSTICS.md) contains no raw history file or credentials.

The app validates storage paths but does not claim protection from hostile local path replacement, power-loss durability, secure disk erasure or other users with access to the Windows account. Queues remain current-run only and transfers are not resumable. Physical phone and clean Windows tests still require verification.
