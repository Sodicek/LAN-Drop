"""Run the native desktop's real server start/stop hook with isolated settings."""
from pathlib import Path
import argparse
import os
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument("--executable", type=Path, help="Check a published desktop package instead of the source build")
parser.add_argument("--without-sdk", action="store_true", help="Run the portable executable without SDK/runtime discovery through PATH or DOTNET_ROOT")
args = parser.parse_args()
if args.without_sdk and not args.executable:
    parser.error("--without-sdk requires --executable")
with tempfile.TemporaryDirectory(prefix="landrop-desktop-") as directory:
    data = Path(directory)
    command = ([str(args.executable.resolve())] if args.executable else
               ["dotnet", "run", "--project", str(ROOT / "src/LanDrop.Desktop"),
                "-c", "Release", "--no-build", "--"])
    environment = os.environ.copy()
    if args.without_sdk:
        system_root = next((value for key, value in environment.items()
                            if key.upper() in ("SYSTEMROOT", "WINDIR")), None)
        assert system_root, "Windows system directory must be available"
        environment["PATH"] = str(Path(system_root) / "System32")
        for key in list(environment):
            if key.upper().startswith("DOTNET_ROOT"):
                del environment[key]
        environment["DOTNET_MULTILEVEL_LOOKUP"] = "0"
    result = subprocess.run(
        command + ["--lifecycle-test", f"--TestDataDirectory={data}"],
        cwd=ROOT, env=environment, timeout=45, capture_output=True, text=True, errors="replace")
    assert result.returncode == 0, (result.returncode, result.stdout, result.stderr)
    # Hook must prove it ran; a normal window smoke test cannot count as lifecycle coverage.
    proof = data / "lifecycle-test.json"
    assert proof.exists(), "Desktop must write lifecycle-test.json after real start and stop"
    import json
    evidence = json.loads(proof.read_text(encoding="utf-8-sig"))
    assert evidence["Started"] is True and evidence["Stopped"] is True
    assert evidence["OffersApproved"] is True and evidence["OffersRejected"] is True
    assert evidence["OutboundVerified"] is True
    assert evidence["BatchesApproved"] is True and evidence["BatchesRejected"] is True
    assert evidence["RetryWithoutDuplicates"] is True and evidence["TransfersRendered"] is True
    assert evidence["ServerExitCode"] == 0
    assert not list((data / "Outbox").glob("*"))
    print("PASS: native desktop starts backend, approves/rejects batches, renders transfers, retries actual failed source without duplicating completed source, verifies saved receipts, stops gracefully and cleans owned outbox.")
