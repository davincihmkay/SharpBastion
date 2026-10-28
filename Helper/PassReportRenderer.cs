using System.Globalization;
using System.Net;
using System.Text;
using SharpBastion.Domain;

namespace SharpBastion.Helper;

/// <summary>
/// Renders recorded passes as one self-contained HTML page: a timeline, a
/// file-by-pass matrix, per-file history with net change across passes, and
/// per-pass detail with diffs. In pending mode (passes still awaiting review)
/// the matrix and file history are left out, because they describe history.
///
/// Everything in a pass is untrusted text (review comments, model output, file
/// contents), so every value is HTML-encoded and the page carries no script and
/// no external reference. Expansion is done with details/summary.
/// </summary>
public static class PassReportRenderer
{
    private const int MaxDiffInputLines = 4000;
    private const int MaxRenderedDiffLines = 1500;

    private sealed record DiffOutcome(DiffResult? Result, string? Note);

    private sealed record FileView(PassSnapshot Pass, PassFileChange File, DiffOutcome Diff)
    {
        /// <summary>Approved and part of a commit that exists (locally at least).</summary>
        public bool Landed => File.Status == PassFileStatus.Approved && IsCommitted(Pass.State);
    }

    private sealed record FileRow(string Path, IReadOnlyList<FileView> Views);

    /// <summary>Passes must belong to one pull request. Throws when none are given.</summary>
    public static string Render(IReadOnlyList<PassSnapshot> passes, DateTimeOffset generatedAt, bool pendingOnly = false)
    {
        if (passes.Count == 0)
            throw new ArgumentException("At least one pass is required.", nameof(passes));

        var ordered = passes.OrderBy(p => p.Sequence).ToList();

        var views = ordered
            .SelectMany(p => p.Files.Select(f => new FileView(p, f, ComputeDiff(f))))
            .ToList();

        var rows = views
            .GroupBy(v => v.File.RelativePath, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new FileRow(g.Key, g.OrderBy(v => v.Pass.Sequence).ToList()))
            .ToList();

        var cells = views
            .GroupBy(v => (v.Pass.Sequence, v.File.RelativePath))
            .ToDictionary(g => g.Key, g => g.First());

        var viewsByPass = views.ToLookup(v => v.Pass.Sequence);

        var sb = new StringBuilder(64 * 1024);

        AppendHead(sb, ordered, pendingOnly);
        AppendSummary(sb, ordered, rows, generatedAt, pendingOnly);
        AppendTimeline(sb, ordered, viewsByPass);

        if (!pendingOnly)
        {
            AppendMatrix(sb, ordered, rows, cells);
            AppendFileHistory(sb, rows);
        }

        AppendPassDetails(sb, ordered, viewsByPass);
        AppendFoot(sb);

        return sb.ToString();
    }

    // ── Sections ─────────────────────────────────────────────────────────────

    private static void AppendHead(StringBuilder sb, IReadOnlyList<PassSnapshot> passes, bool pendingOnly)
    {
        var pr = passes[0].PullRequest.ToString();
        var title = pendingOnly
            ? $"SharpBastion pending review — {pr}"
            : $"SharpBastion passes — {pr}";

        sb.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n");
        sb.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'\">\n");
        sb.Append("<meta name=\"referrer\" content=\"no-referrer\">\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        sb.Append($"<title>{E(title)}</title>\n");
        sb.Append("<style>").Append(Css).Append("</style>\n</head>\n<body>\n<main>\n");
    }

    private static void AppendSummary(
        StringBuilder sb,
        IReadOnlyList<PassSnapshot> passes,
        IReadOnlyList<FileRow> rows,
        DateTimeOffset generatedAt,
        bool pendingOnly)
    {
        var pr = passes[0].PullRequest.ToString();
        var committed = passes.Count(p => IsCommitted(p.State));
        var multi = rows.Count(r => r.Views.Select(v => v.Pass.Sequence).Distinct().Count() > 1);

        sb.Append(pendingOnly
            ? $"<h1>Awaiting review: {E(pr)}</h1>\n"
            : $"<h1>Passes for {E(pr)}</h1>\n");
        sb.Append($"<p class=\"sub\">Branch <code>{E(passes[^1].Branch)}</code> · generated {E(Format(generatedAt))}</p>\n");

        if (pendingOnly)
            sb.Append("<div class=\"warnbox\">Snapshot of the passes waiting for review when this file was generated. Approving or rejecting afterwards does not update it.</div>\n");

        sb.Append("<div class=\"warnbox\">This report contains source code and review comments from the session. Treat it as sensitive.</div>\n");

        sb.Append("<div class=\"stats\">");
        Stat(sb, passes.Count, pendingOnly ? "awaiting review" : "passes");
        if (!pendingOnly) Stat(sb, committed, "committed");
        Stat(sb, rows.Count, "files touched");
        if (!pendingOnly) Stat(sb, multi, "files in 2+ passes");
        sb.Append("</div>\n");
    }

    private static void AppendTimeline(
        StringBuilder sb, IReadOnlyList<PassSnapshot> passes, ILookup<int, FileView> viewsByPass)
    {
        sb.Append("<h2>Timeline</h2>\n<ol class=\"timeline\">\n");

        foreach (var pass in passes)
        {
            var (label, css) = Describe(pass);
            var files = viewsByPass[pass.Sequence].ToList();
            var (added, removed) = Totals(files);

            sb.Append($"<li class=\"{css}\"><a href=\"#pass-{pass.Sequence}\">#{pass.Sequence}</a> ");
            sb.Append($"<span class=\"badge {css}\">{E(label)}</span> ");
            sb.Append($"<code>{E(pass.Comment.Path ?? "(no path)")}</code>");
            sb.Append($" <span class=\"sub\">by {E(pass.Comment.Author ?? "unknown")} · {files.Count} file(s)");
            if (files.Count > 0) sb.Append($" · +{added} −{removed}");
            if (pass.CommitSha is not null) sb.Append($" · <code>{E(Short(pass.CommitSha))}</code>");
            sb.Append($" · {E(Format(pass.StartedAt))}</span></li>\n");
        }

        sb.Append("</ol>\n");
    }

    private static void AppendMatrix(
        StringBuilder sb,
        IReadOnlyList<PassSnapshot> passes,
        IReadOnlyList<FileRow> rows,
        IReadOnlyDictionary<(int, string), FileView> cells)
    {
        sb.Append("<h2>Files across passes</h2>\n");

        if (rows.Count == 0)
        {
            sb.Append("<p class=\"sub\">No pass proposed a file change.</p>\n");
            return;
        }

        sb.Append("<div class=\"scroll\"><table class=\"matrix\">\n<thead><tr><th>File</th>");
        foreach (var pass in passes)
        {
            var (label, css) = Describe(pass);
            sb.Append($"<th><a href=\"#pass-{pass.Sequence}\">#{pass.Sequence}</a><br><span class=\"badge {css}\">{E(label)}</span></th>");
        }
        sb.Append("</tr></thead>\n<tbody>\n");

        foreach (var row in rows)
        {
            var multi = row.Views.Select(v => v.Pass.Sequence).Distinct().Count() > 1;
            sb.Append(multi ? "<tr class=\"multi\">" : "<tr>");
            sb.Append($"<th scope=\"row\"><code>{E(row.Path)}</code></th>");

            foreach (var pass in passes)
            {
                if (!cells.TryGetValue((pass.Sequence, row.Path), out var view))
                {
                    sb.Append("<td class=\"empty\"></td>");
                    continue;
                }

                var status = view.File.Status.ToString().ToLowerInvariant();
                var stat = view.Diff.Result is null
                    ? "n/a"
                    : $"+{view.Diff.Result.Added} −{view.Diff.Result.Removed}";
                var title = view.Landed ? $"{status}, committed" : status;

                sb.Append($"<td class=\"{status}{(view.Landed ? " landed" : string.Empty)}\" title=\"{E(title)}\">");
                sb.Append($"<a href=\"#pass-{pass.Sequence}\">{E(stat)}</a></td>");
            }

            sb.Append("</tr>\n");
        }

        sb.Append("</tbody></table></div>\n");
        sb.Append("<p class=\"sub\">Green: approved. Red: rejected or discarded. Blue: awaiting review. Bold: part of a commit. ");
        sb.Append("A marked row was touched by more than one pass.</p>\n");
    }

    private static void AppendFileHistory(StringBuilder sb, IReadOnlyList<FileRow> rows)
    {
        if (rows.Count == 0) return;

        sb.Append("<h2>File history</h2>\n");

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var passCount = row.Views.Select(v => v.Pass.Sequence).Distinct().Count();
            var landed = row.Views.Where(v => v.Landed).ToList();

            sb.Append($"<section id=\"file-{i}\"><h3><code>{E(row.Path)}</code> ");
            sb.Append($"<span class=\"sub\">{passCount} pass(es), {landed.Count} committed</span></h3>\n");

            sb.Append("<table class=\"plain\"><thead><tr><th>Pass</th><th>Status</th><th>Change</th><th>Outcome</th></tr></thead><tbody>");
            foreach (var view in row.Views)
            {
                var (label, css) = Describe(view.Pass);
                var stat = view.Diff.Result is null
                    ? "n/a"
                    : $"+{view.Diff.Result.Added} −{view.Diff.Result.Removed}";

                sb.Append($"<tr><td><a href=\"#pass-{view.Pass.Sequence}\">#{view.Pass.Sequence}</a></td>");
                sb.Append($"<td>{E(view.File.Status.ToString())}</td><td>{E(stat)}</td>");
                sb.Append($"<td><span class=\"badge {css}\">{E(label)}</span></td></tr>");
            }
            sb.Append("</tbody></table>\n");

            AppendDriftWarnings(sb, landed);

            if (landed.Count >= 2)
            {
                var net = ComputeNet(landed[0], landed[^1]);
                var summary = net.Result is null
                    ? "Net change across committed passes"
                    : $"Net change across {landed.Count} committed passes (+{net.Result.Added} −{net.Result.Removed})";

                sb.Append($"<details><summary>{E(summary)}</summary>");
                AppendDiff(sb, net);
                sb.Append("</details>\n");
            }

            sb.Append("</section>\n");
        }
    }

    /// <summary>
    /// A later pass should start from what the previous one produced. When it does
    /// not, the file was edited outside SharpBastion in between.
    /// </summary>
    private static void AppendDriftWarnings(StringBuilder sb, IReadOnlyList<FileView> landed)
    {
        for (var i = 1; i < landed.Count; i++)
        {
            var previous = landed[i - 1];
            var current = landed[i];

            if (current.File.Baseline is null) continue;
            if (Normalize(previous.File.Proposed) == Normalize(current.File.Baseline)) continue;

            sb.Append($"<p class=\"warnbox\">Pass #{current.Pass.Sequence} started from content that differs from what pass ");
            sb.Append($"#{previous.Pass.Sequence} produced. The file changed outside SharpBastion in between, ");
            sb.Append("so the net change below includes those edits.</p>\n");
        }
    }

    private static void AppendPassDetails(
        StringBuilder sb, IReadOnlyList<PassSnapshot> passes, ILookup<int, FileView> viewsByPass)
    {
        sb.Append("<h2>Pass detail</h2>\n");

        foreach (var pass in passes)
        {
            var (label, css) = Describe(pass);
            var comment = pass.Comment;

            sb.Append($"<article class=\"passcard\" id=\"pass-{pass.Sequence}\">");
            sb.Append($"<h3>Pass #{pass.Sequence} <span class=\"badge {css}\">{E(label)}</span></h3>\n<dl class=\"meta\">");

            Meta(sb, "Pull request", E(pass.PullRequest.ToString()));
            Meta(sb, "Branch", $"<code>{E(pass.Branch)}</code>");
            Meta(sb, "Started", E(Format(pass.StartedAt)));
            Meta(sb, "Last update", E(Format(pass.UpdatedAt)));

            var commentRef = $"#{comment.Id}";
            var link = SafeGitHubUrl(comment.Url);
            Meta(sb, "Comment", link is null
                ? E(commentRef)
                : $"<a href=\"{E(link)}\" rel=\"noopener noreferrer\">{E(commentRef)}</a>");
            Meta(sb, "Author", E(comment.Author ?? "unknown"));
            Meta(sb, "Anchored on", $"<code>{E(comment.Path ?? "(none)")}</code>");

            if (pass.CommitSha is not null)
                Meta(sb, "Commit", $"<code>{E(pass.CommitSha)}</code>");

            sb.Append("</dl>\n");

            sb.Append("<h4>Review comment</h4>\n");
            sb.Append($"<pre class=\"text\">{E(comment.Body)}</pre>\n");

            if (pass.ModelSummary is not null)
            {
                sb.Append("<h4>Model summary</h4>\n");
                sb.Append($"<p>{E(pass.ModelSummary)}</p>\n");
            }

            if (pass.Detail is not null)
            {
                sb.Append("<h4>Outcome detail</h4>\n");
                sb.Append($"<p>{E(pass.Detail)}</p>\n");
            }

            var files = viewsByPass[pass.Sequence].ToList();
            if (files.Count > 0)
            {
                sb.Append("<h4>Files</h4>\n");
                foreach (var view in files)
                {
                    var stat = view.Diff.Result is null
                        ? "n/a"
                        : $"+{view.Diff.Result.Added} −{view.Diff.Result.Removed}";

                    sb.Append("<details><summary>");
                    sb.Append($"<code>{E(view.File.RelativePath)}</code> · {E(view.File.Status.ToString())} · {E(stat)}");
                    if (!view.File.ExistedBefore) sb.Append(" · new file");
                    sb.Append("</summary>");
                    AppendDiff(sb, view.Diff);
                    sb.Append("</details>\n");
                }
            }

            if (pass.PostedReply is not null)
            {
                sb.Append("<h4>Reply posted</h4>\n");
                sb.Append($"<pre class=\"text\">{E(pass.PostedReply)}</pre>\n");
            }

            sb.Append("</article>\n");
        }
    }

    private static void AppendFoot(StringBuilder sb) =>
        sb.Append("</main>\n<footer>Generated by SharpBastion. Passes are recorded in memory for the current session only.</footer>\n</body>\n</html>\n");

    // ── Diffs ────────────────────────────────────────────────────────────────

    private static DiffOutcome ComputeDiff(PassFileChange file)
    {
        if (!file.ExistedBefore)
            return Compute(string.Empty, file.Proposed);

        if (file.Baseline is null)
            return new DiffOutcome(null, "The original content was not recorded (the file was too large or unreadable).");

        return Compute(file.Baseline, file.Proposed);
    }

    private static DiffOutcome ComputeNet(FileView first, FileView last)
    {
        if (first.File.ExistedBefore && first.File.Baseline is null)
            return new DiffOutcome(null, "The original content was not recorded, so the net change cannot be computed.");

        var before = first.File.ExistedBefore ? first.File.Baseline! : string.Empty;
        return Compute(before, last.File.Proposed);
    }

    private static DiffOutcome Compute(string before, string after)
    {
        // The diff is quadratic in line count: refuse inputs that would be expensive.
        if (CountLines(before) > MaxDiffInputLines || CountLines(after) > MaxDiffInputLines)
            return new DiffOutcome(null, $"Too large to diff here (more than {MaxDiffInputLines} lines).");

        return new DiffOutcome(DiffFormatter.Compare(before, after), null);
    }

    private static void AppendDiff(StringBuilder sb, DiffOutcome outcome)
    {
        if (outcome.Result is null)
        {
            sb.Append($"<p class=\"note\">{E(outcome.Note)}</p>");
            return;
        }

        if (outcome.Result.Hunks.Count == 0)
        {
            sb.Append("<p class=\"note\">No changes.</p>");
            return;
        }

        sb.Append("<div class=\"diffscroll\"><table class=\"diff\"><tbody>");

        var rendered = 0;
        foreach (var hunk in outcome.Result.Hunks)
        {
            sb.Append($"<tr class=\"hunk\"><td colspan=\"4\">@@ -{hunk.OldStart},{hunk.OldCount} +{hunk.NewStart},{hunk.NewCount} @@</td></tr>");

            foreach (var line in hunk.Lines)
            {
                if (rendered++ >= MaxRenderedDiffLines)
                {
                    sb.Append("<tr class=\"hunk\"><td colspan=\"4\">… diff truncated …</td></tr></tbody></table></div>");
                    return;
                }

                var (css, sign) = line.Kind switch
                {
                    DiffLineKind.Added => ("add", "+"),
                    DiffLineKind.Removed => ("del", "−"),
                    _ => ("ctx", " ")
                };

                sb.Append($"<tr class=\"{css}\"><td class=\"ln\">{line.OldNumber}</td><td class=\"ln\">{line.NewNumber}</td>");
                sb.Append($"<td class=\"sg\">{sign}</td><td class=\"tx\">{E(line.Text)}</td></tr>");
            }
        }

        sb.Append("</tbody></table></div>");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool IsCommitted(PassState state) =>
        state is PassState.Pushed or PassState.Replied or PassState.PushFailed;

    private static (string Label, string Css) Describe(PassSnapshot pass) => pass.State switch
    {
        PassState.Replied => ("Replied", "ok"),
        PassState.Pushed => pass.Detail is null ? ("Pushed", "ok") : ("Pushed, reply failed", "warn"),
        PassState.PushFailed => ("Committed, push failed", "bad"),
        PassState.CommitFailed => ("Commit failed", "bad"),
        PassState.AskFailed => ("Model call failed", "bad"),
        PassState.Proposed => ("Awaiting review", "warn"),
        PassState.Started => ("Interrupted or in progress", "warn"),
        PassState.Rejected => ("Rejected", "muted"),
        PassState.NothingToCommit => ("Nothing to commit", "muted"),
        PassState.Discarded => ("Proposal discarded", "muted"),
        PassState.NoChange => ("No change proposed", "muted"),
        PassState.Skipped => ("Skipped", "muted"),
        _ => (pass.State.ToString(), "muted")
    };

    private static (int Added, int Removed) Totals(IEnumerable<FileView> views)
    {
        var added = 0;
        var removed = 0;

        foreach (var view in views)
        {
            if (view.Diff.Result is null) continue;
            added += view.Diff.Result.Added;
            removed += view.Diff.Result.Removed;
        }

        return (added, removed);
    }

    private static void Stat(StringBuilder sb, int value, string label) =>
        sb.Append($"<div class=\"stat\"><b>{value}</b>{E(label)}</div>");

    private static void Meta(StringBuilder sb, string label, string htmlValue) =>
        sb.Append($"<dt>{E(label)}</dt><dd>{htmlValue}</dd>");

    private static string? SafeGitHubUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            ? uri.AbsoluteUri
            : null;

    private static int CountLines(string text) => text.AsSpan().Count('\n') + 1;

    private static string Normalize(string text) => text.Replace("\r\n", "\n").Trim();

    private static string Short(string sha) => sha.Length > 7 ? sha[..7] : sha;

    private static string Format(DateTimeOffset time) =>
        time.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

    private static string E(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

    private const string Css = """
:root{--bg:#fff;--fg:#1b1f24;--muted:#656d76;--line:#d0d7de;--panel:#f6f8fa;--link:#0969da;--ok:#1a7f37;--warn:#9a6700;--bad:#cf222e;--add:#e6ffec;--del:#ffebe9;--hunk:#ddf4ff}
@media (prefers-color-scheme:dark){:root{--bg:#0d1117;--fg:#e6edf3;--muted:#8d96a0;--line:#30363d;--panel:#161b22;--link:#58a6ff;--ok:#3fb950;--warn:#d29922;--bad:#f85149;--add:#12261e;--del:#2d1214;--hunk:#10243a}}
*{box-sizing:border-box}
body{margin:0;padding:24px;background:var(--bg);color:var(--fg);font:14px/1.5 system-ui,-apple-system,"Segoe UI",sans-serif}
main{max-width:1200px;margin:0 auto}
h1{font-size:22px;margin:0 0 4px}
h2{font-size:18px;margin:32px 0 12px;padding-bottom:6px;border-bottom:1px solid var(--line)}
h3{font-size:15px;margin:0 0 8px}
h4{font-size:12px;margin:16px 0 6px;color:var(--muted);text-transform:uppercase;letter-spacing:.04em}
code,pre{font-family:ui-monospace,SFMono-Regular,Menlo,Consolas,monospace;font-size:12.5px}
a{color:var(--link)}
.sub{color:var(--muted)}
.note{color:var(--muted);font-style:italic}
.warnbox{border:1px solid var(--warn);background:var(--panel);padding:8px 12px;border-radius:6px;margin:12px 0}
.stats{display:flex;gap:12px;flex-wrap:wrap;margin:16px 0}
.stat{background:var(--panel);border:1px solid var(--line);border-radius:6px;padding:8px 14px;color:var(--muted)}
.stat b{display:block;font-size:20px;color:var(--fg)}
.badge{display:inline-block;padding:1px 8px;border-radius:10px;font-size:12px;border:1px solid currentColor;white-space:nowrap}
.ok{color:var(--ok)}.warn{color:var(--warn)}.bad{color:var(--bad)}.muted{color:var(--muted)}
ol.timeline{list-style:none;margin:0;padding:0}
ol.timeline li{padding:8px 12px;border:1px solid var(--line);border-left-width:4px;border-radius:6px;margin-bottom:8px;background:var(--panel);color:var(--fg)}
ol.timeline li.ok{border-left-color:var(--ok)}
ol.timeline li.warn{border-left-color:var(--warn)}
ol.timeline li.bad{border-left-color:var(--bad)}
ol.timeline li.muted{border-left-color:var(--muted)}
.scroll,.diffscroll{overflow-x:auto}
table{border-collapse:collapse}
table.matrix th,table.matrix td{border:1px solid var(--line);padding:4px 8px;text-align:center;white-space:nowrap}
table.matrix th[scope=row]{text-align:left;position:sticky;left:0;background:var(--bg)}
table.matrix tr.multi th[scope=row]{border-left:4px solid var(--warn)}
td.approved{background:var(--add)}
td.rejected,td.discarded{background:var(--del)}
td.proposed{background:var(--hunk)}
td.landed{font-weight:700}
table.plain th,table.plain td{border-bottom:1px solid var(--line);padding:4px 12px;text-align:left}
section{margin-bottom:24px}
article.passcard{border:1px solid var(--line);border-radius:8px;padding:16px;margin-bottom:20px}
pre.text{white-space:pre-wrap;word-break:break-word;background:var(--panel);border:1px solid var(--line);border-radius:6px;padding:10px;margin:0}
details{margin:6px 0}
summary{cursor:pointer}
table.diff{width:100%;font-family:ui-monospace,SFMono-Regular,Menlo,Consolas,monospace;font-size:12px;border:1px solid var(--line);margin:6px 0}
table.diff td{padding:0 6px;vertical-align:top;white-space:pre}
table.diff td.ln{color:var(--muted);text-align:right;user-select:none;width:1%}
table.diff td.sg{width:1%}
table.diff td.tx{width:100%}
table.diff tr.add{background:var(--add)}
table.diff tr.del{background:var(--del)}
table.diff tr.hunk td{background:var(--hunk);color:var(--muted)}
dl.meta{display:grid;grid-template-columns:max-content 1fr;gap:2px 16px;margin:0}
dl.meta dt{color:var(--muted)}
dl.meta dd{margin:0}
footer{max-width:1200px;margin:40px auto 0;color:var(--muted);font-size:12px}
""";
}