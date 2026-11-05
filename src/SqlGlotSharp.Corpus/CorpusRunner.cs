namespace SqlGlotSharp.Corpus;

public enum CaseStatus { Passed, Failed, NotPorted }
public sealed record CaseResult(CorpusCase Case, CaseStatus Status, string? Detail);

/// <summary>Runs a batch on a dedicated thread with the stack required by deep SQL ASTs.</summary>
public static class CorpusRunner
{
    /// <remarks>
    /// threads &gt; 1 splits the batch across that many big-stack threads (each case uses fresh
    /// dialect/parser/generator instances). Results keep the input order.
    /// </remarks>
    public static IReadOnlyList<CaseResult> Run(IEnumerable<CorpusCase> cases, Action<CorpusCase> verify, int threads = 1)
    {
        var items = cases.ToList();
        var results = new CaseResult[items.Count];
        Exception? fatal = null;
        var next = -1;
        var workers = Enumerable.Range(0, Math.Max(1, threads)).Select(_ => new Thread(() =>
        {
            try
            {
                for (var i = Interlocked.Increment(ref next); i < items.Count; i = Interlocked.Increment(ref next))
                {
                    var item = items[i];
                    try { verify(item); results[i] = new(item, CaseStatus.Passed, null); }
                    catch (NotPortedException error) { results[i] = new(item, CaseStatus.NotPorted, error.Message); }
                    catch (Exception error) { results[i] = new(item, CaseStatus.Failed, error.ToString()); }
                }
            }
            catch (Exception error) { fatal = error; }
        }, 64 * 1024 * 1024)).ToList();
        workers.ForEach(w => w.Start());
        workers.ForEach(w => w.Join());
        if (fatal is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(fatal).Throw();
        return results;
    }

    // Explicit baseline: no SQL boundary is implemented yet. Never substitutes the oracle output.
    public static void VerifyBaseline(CorpusCase item) => throw new NotPortedException($"corpus:{item.Level}:{item.Id}");
}
