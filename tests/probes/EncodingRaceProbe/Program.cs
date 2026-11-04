// Decodes the same cp1250 bytes on many threads at once, as the very first legacy-encoding use in
// this process, and reports whether every thread got the right text. A thread that saw the
// code-page provider as registered before it was would fall back to UTF-8 and get U+FFFD instead.
// Exit code 0: all decodes equal the expected text; 1: they differ (each distinct result printed).

using System.Text;
using Therion.Syntax;

int threads = args.Length > 0 && int.TryParse(args[0], out var n) ? n : 16;

// "encoding cp1250", then a title with š (0x9A) and č (0xE8) in cp1250.
var bytes = Encoding.ASCII.GetBytes("encoding cp1250\nsurvey a -title \"")
    .Concat(new byte[] { 0x9A, 0xE8 })
    .Concat(Encoding.ASCII.GetBytes("\"\nendsurvey\n"))
    .ToArray();
const string expected = "encoding cp1250\nsurvey a -title \"šč\"\nendsurvey\n";

var results = new string[threads];
using var start = new Barrier(threads);
var workers = Enumerable.Range(0, threads).Select(i => new Thread(() =>
{
    start.SignalAndWait();
    results[i] = EncodingResolver.Decode(bytes);
})).ToList();
workers.ForEach(t => t.Start());
workers.ForEach(t => t.Join());

var wrong = results.Where(r => r != expected).Distinct().ToList();
foreach (var r in wrong)
    Console.WriteLine("wrong decode: " + string.Concat(r.Select(c => c < 128 && c >= 32 ? c.ToString() : $"\\u{(int)c:X4}")));
return wrong.Count == 0 ? 0 : 1;
