"""Run the native desktop's real server start/stop hook with isolated settings."""
from pathlib import Path
import argparse
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument("--executable", type=Path, help="Check a published desktop package instead of the source build")
args = parser.parse_args()
with tempfile.TemporaryDirectory(prefix="landrop-desktop-") as directory:
    data = Path(directory)
    command = ([str(args.executable.resolve())] if args.executable else
               ["dotnet", "run", "--project", str(ROOT / "src/LanDrop.Desktop"),
                "-c", "Release", "--no-build", "--"])
    result = subprocess.run(
        command + ["--lifecycle-test", f"--TestDataDirectory={data}"],
        cwd=ROOT, timeout=45, capture_output=True, text=True, errors="replace")
    assert result.returncode == 0, (result.returncode, result.stdout, result.stderr)
    # Hook must prove it ran; a normal window smoke test cannot count as lifecycle coverage.
    proof = data / "lifecycle-test.json"
    assert proof.exists(), "Desktop must write lifecycle-test.json after real start and stop"
    import json
    evidence = json.loads(proof.read_text(encoding="utf-8-sig"))
    assert evidence["Started"] is True and evidence["Stopped"] is True
    assert evidence["OffersApproved"] is True and evidence["OffersRejected"] is True
    assert evidence["OutboundVerified"] is True
    assert evidence["ServerExitCode"] == 0
    assert not list((data / "Outbox").glob("*"))
    print("PASS: native desktop starts backend, approves/rejects through UI handlers, verifies actual outbound uploads/receipts, stops gracefully and cleans owned outbox.")
