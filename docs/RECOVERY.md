# Recovery after an interrupted run

Incoming uploads are first written to an owned staging directory within their selected destination. The server checks the complete length, sender SHA-256 and stored bytes before moving the file into the receive folder. The move remains on the same volume and never overwrites an existing completed file. A folder change affects later uploads; an active upload keeps its original destination.

When sharing starts or a receive folder is selected, LAN Drop attempts to discard inactive staging left by earlier interrupted runs in that folder. It requires its own exact marker, known temporary filenames and an available exclusive lease before deleting anything. Cleanup does not recurse, follow links, or sweep arbitrary `.part` files from your receive folder. Live runs, unknown entries, malformed markers and unsafe paths are preserved.

The Windows app shows how many incomplete uploads were discarded, and whether some staging was left untouched. Another running instance can legitimately keep its staging in use. The [diagnostic export](DIAGNOSTICS.md) includes only recovery counts, with no names, paths or file contents.

## After a crash

1. Restart LAN Drop, select the intended receive folder and start sharing. Enter your certificate password again.
2. Check the receive folder for any already completed file before sending it again. A crash or lost response just after a successful move can leave a complete file even though the sender did not receive confirmation.
3. Pair and approve the phone again, then request a new transfer for unfinished files. Old sessions and approvals do not survive the server restart.
4. If staging is left untouched, stop any other LAN Drop instance using that folder, then select the folder again. Unexpected entries remain preserved; inspect them manually rather than broadly deleting files by extension.

Recovery is best-effort and scans only a bounded number of staging candidates. It applies to the folder currently selected at startup and folders subsequently selected. It does not search every folder previously used on the PC. A staging directory abandoned in an older destination is considered when that destination is selected again.

Partial files from releases before rc.4 were created without this staging ownership record. They are preserved rather than guessed to be garbage. Completed received files are never a cleanup target. Transfer queues, activity and approvals remain current-run only; discarded partial data cannot be resumed.

Checks reject reparse points and validate paths at storage boundaries, but do not provide atomic protection against a hostile local process replacing paths between OS calls. This is process-crash cleanup, not proof of power-loss durability or a backup system. Physical-device, network interruption and clean Windows checks remain pending.
