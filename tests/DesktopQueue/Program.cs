using System;
using LanDrop.Desktop;

static void Check(bool condition, string message) { if(!condition) throw new Exception(message); }
var queue = new RetainedTransferQueue();
string[] sources = ["completed.jpg","failed.jpg","queued.jpg"];
queue.Begin(sources,"recipient-1");
sources[0] = "external mutation";
Check(queue.Paths[0] == "completed.jpg","Queue must own source snapshot");
queue.ConfirmCurrent();
Check(queue.Paths.Length == 2 && queue.Paths[0] == "failed.jpg","Retry excludes confirmed receipt");
var retrySnapshot = queue.Paths;
retrySnapshot[0] = "external mutation";
Check(queue.Paths[0] == "failed.jpg","Exposed retry snapshot cannot mutate queue");
Check(queue.Recipient == "recipient-1","Retry stays tied to original recipient");
queue.ConfirmCurrent(); queue.ConfirmCurrent();
Check(queue.Paths.Length == 0,"All confirmed sources are omitted");
try { queue.ConfirmCurrent(); throw new Exception("Empty confirmation should fail"); } catch(InvalidOperationException) { }
queue.Begin(["new-file.txt"],"recipient-2");
Check(queue.Paths.Length == 1 && queue.Recipient == "recipient-2","New selection replaces prior queue");
queue.Clear();
Check(queue.Paths.Length == 0 && queue.Recipient is null,"Explicit clear releases source selection and recipient");
Console.WriteLine("PASS: desktop retry snapshots unfinished sources, isolates mutation, preserves recipient and excludes verified completions.");
