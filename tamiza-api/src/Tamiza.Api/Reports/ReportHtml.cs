using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace Tamiza.Api.Reports;

/// <summary>
/// The body of a scheduled email (design D2, D10): a header, the stat tile values, then a block per other widget with the first rows of
/// its table, and a footer. Plain tables with inline styles, no CSS classes, images or scripts, which every mail program shows. Every
/// value members or the data wrote is HTML-encoded. Blocks are added while the body stays within the size limit, so the text part,
/// which says the same, takes the same blocks.
/// </summary>
public static class ReportHtml
{
    private const string Font = "font-family:Arial,Helvetica,sans-serif;";
    private const string Muted = "color:#5b6270;";

    /// <summary>The HTML body and the widgets whose block it holds; every other table widget is only listed by title.</summary>
    public static (string Html, IReadOnlySet<int> Shown) Write(ReportModel model, bool attachmentIncluded, ReportOptions options)
    {
        var text = model.Text;
        var start = new StringBuilder();
        start.Append($"<!DOCTYPE html><html lang=\"{text.Culture.TwoLetterISOLanguageName}\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
            .Append($"<title>{E(model.Subject)}</title></head>")
            .Append($"<body style=\"margin:0;padding:0;background:#f3f4f6;{Font}\">")
            .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#f3f4f6;\"><tr><td align=\"center\" style=\"padding:16px 8px;\">")
            .Append("<table role=\"presentation\" width=\"640\" cellpadding=\"0\" cellspacing=\"0\" style=\"width:100%;max-width:640px;background:#ffffff;border:1px solid #e5e7eb;\">");
        Header(start, model);
        Stats(start, model);
        start.Append(Row($"<p style=\"margin:0;font-size:13px;{Muted}\">{E(text[attachmentIncluded ? "attached" : "tooLarge"])}</p>"));

        var end = new StringBuilder();
        Footer(end, model);
        end.Append("</table></td></tr></table></body></html>");

        var budget = options.MaxBodyKb * 1024;
        var used = Bytes(start) + Bytes(end);
        var blocks = new StringBuilder();
        var shown = new HashSet<int>();
        var left = new List<ReportWidget>();
        // The list of widgets left out takes room too: keep enough for it whatever stays out.
        var reserve = Bytes(NotShown(model, model.Tables.ToList()));
        foreach (var widget in model.Tables)
        {
            var block = Block(widget, model, options.RowsPerBlock);
            if (left.Count == 0 && used + Bytes(block) + reserve <= budget)
            {
                blocks.Append(block);
                used += Bytes(block);
                shown.Add(widget.Position);
            }
            else
            {
                left.Add(widget);
            }
        }

        if (left.Count > 0)
        {
            blocks.Append(NotShown(model, left));
        }

        return (start.Append(blocks).Append(end).ToString(), shown);
    }

    private static int Bytes(StringBuilder html) => Encoding.UTF8.GetByteCount(html.ToString());

    private static int Bytes(string html) => Encoding.UTF8.GetByteCount(html);

    /// <summary>Escapes what HTML gives a meaning to (&amp;, &lt;, &gt;, quotes) and keeps letters as they are: the body is UTF-8.</summary>
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    private static string E(string? value) => Encoder.Encode(value ?? "");

    private static string Row(string content, string padding = "8px 24px") => $"<tr><td style=\"padding:{padding};\">{content}</td></tr>";

    private static void Header(StringBuilder html, ReportModel model)
    {
        var text = model.Text;
        var format = model.Format;
        html.Append(Row(
            $"<p style=\"margin:0;font-size:13px;{Muted}\">{E(model.ProjectName)}</p>"
            + $"<h1 style=\"margin:4px 0 0;font-size:22px;color:#111827;\">{E(model.DashboardName)}</h1>", "24px 24px 8px"));
        var lines = new List<(string Label, string Value)>
        {
            (text["generated"], format.Moment(model.GeneratedAt)),
            (text["dataPublished"], model.DataPublishedAt is { } at ? format.Moment(at) : text["dataNotPublished"]),
            (text["filters"], model.Filters.Count == 0 ? text["noFilters"] : string.Join("; ", model.Filters.Select(f => $"{f.Label}: {f.Value}"))),
        };
        html.Append(Row(
            "<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" style=\"font-size:13px;color:#374151;\">"
            + string.Concat(lines.Select(l => $"<tr><td style=\"padding:2px 12px 2px 0;{Muted}vertical-align:top;\">{E(l.Label)}</td><td style=\"padding:2px 0;\">{E(l.Value)}</td></tr>"))
            + "</table>"));
    }

    /// <summary>The stat tiles, three to a row.</summary>
    private static void Stats(StringBuilder html, ReportModel model)
    {
        var stats = model.Stats.ToList();
        if (stats.Count == 0)
        {
            return;
        }

        var cells = new StringBuilder("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\">");
        foreach (var chunk in stats.Chunk(3))
        {
            cells.Append("<tr>");
            foreach (var stat in chunk)
            {
                var value = stat.StatValue is { } shown
                    ? $"<p style=\"margin:4px 0;font-size:24px;font-weight:bold;color:#111827;\">{E(shown)}</p>"
                    : $"<p style=\"margin:4px 0;font-size:13px;{Muted}\">{E(stat.Reason)}</p>";
                cells.Append("<td width=\"33%\" style=\"padding:8px;vertical-align:top;border:1px solid #e5e7eb;\">")
                    .Append($"<p style=\"margin:0;font-size:13px;color:#374151;\">{E(stat.Title)}</p>")
                    .Append(value)
                    .Append($"<p style=\"margin:0;font-size:12px;{Muted}\">{E(stat.Caption)}</p>")
                    .Append(Notes(stat.Notes))
                    .Append("</td>");
            }

            cells.Append("</tr>");
        }

        html.Append(Row(cells.Append("</table>").ToString()));
    }

    private static string Notes(IReadOnlyList<string> notes) =>
        string.Concat(notes.Select(n => $"<p style=\"margin:4px 0 0;font-size:12px;color:#92400e;\">{E(n)}</p>"));

    /// <summary>A widget's title, what it plots, its notes and its first rows, or the reason it shows none.</summary>
    private static string Block(ReportWidget widget, ReportModel model, int rowsPerBlock)
    {
        var html = new StringBuilder();
        html.Append($"<h2 style=\"margin:16px 0 2px;font-size:16px;color:#111827;\">{E(widget.Title)}</h2>")
            .Append($"<p style=\"margin:0 0 6px;font-size:12px;{Muted}\">{E(widget.Caption)}</p>")
            .Append(Notes(widget.Notes));
        if (widget.Reason is { } reason)
        {
            html.Append($"<p style=\"margin:4px 0;font-size:13px;color:#374151;\">{E(reason)}</p>");
            return Row(html.ToString(), "0 24px");
        }

        html.Append("<table cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse;width:100%;font-size:13px;\"><tr>");
        foreach (var column in widget.Columns)
        {
            html.Append($"<th align=\"{(column.Numeric ? "right" : "left")}\" style=\"padding:4px 8px;border-bottom:2px solid #d1d5db;color:#374151;\">{E(column.Header)}</th>");
        }

        html.Append("</tr>");
        foreach (var row in widget.Rows.Take(rowsPerBlock))
        {
            html.Append("<tr>");
            for (var i = 0; i < row.Count; i++)
            {
                html.Append($"<td align=\"{(widget.Columns[i].Numeric ? "right" : "left")}\" style=\"padding:4px 8px;border-bottom:1px solid #e5e7eb;\">{E(row[i].Text)}</td>");
            }

            html.Append("</tr>");
        }

        html.Append("</table>");
        if (widget.Rows.Count > rowsPerBlock)
        {
            html.Append($"<p style=\"margin:4px 0 0;font-size:12px;{Muted}\">{E(model.Text.MoreRows(widget.Rows.Count - rowsPerBlock))}</p>");
        }

        return Row(html.ToString(), "0 24px");
    }

    private static string NotShown(ReportModel model, IReadOnlyList<ReportWidget> left) =>
        left.Count == 0
            ? ""
            : Row($"<p style=\"margin:16px 0 4px;font-size:13px;color:#374151;\">{E(model.Text["notShown"])}</p>"
                  + "<ul style=\"margin:0;padding-left:20px;font-size:13px;color:#374151;\">"
                  + string.Concat(left.Select(w => $"<li>{E(w.Title)}</li>"))
                  + "</ul>");

    private static void Footer(StringBuilder html, ReportModel model)
    {
        var text = model.Text;
        html.Append(Row(
            $"<p style=\"margin:0;font-size:12px;{Muted}\">{E(text.Format("footer.scheduled", model.ProjectName))} {E(text["footer.stop"])}</p>"
            + $"<p style=\"margin:4px 0 0;font-size:12px;{Muted}\">{E(text.Format("footer.open", ""))}"
            + $"<a href=\"{E(model.DashboardAddress)}\" style=\"color:#1d4ed8;\">{E(model.DashboardAddress)}</a></p>",
            "24px 24px"));
    }
}

/// <summary>The plain-text part of a scheduled email: what the HTML body says, for mail programs that show only text.</summary>
public static class ReportPlainText
{
    public static string Write(ReportModel model, bool attachmentIncluded, IReadOnlySet<int> shown, ReportOptions options)
    {
        var text = model.Text;
        var format = model.Format;
        var body = new StringBuilder();
        body.AppendLine(model.ProjectName).AppendLine(model.DashboardName).AppendLine()
            .AppendLine($"{text["generated"]}: {format.Moment(model.GeneratedAt)}")
            .AppendLine($"{text["dataPublished"]}: {(model.DataPublishedAt is { } at ? format.Moment(at) : text["dataNotPublished"])}")
            .AppendLine($"{text["filters"]}: {(model.Filters.Count == 0 ? text["noFilters"] : string.Join("; ", model.Filters.Select(f => $"{f.Label}: {f.Value}")))}");

        var stats = model.Stats.ToList();
        if (stats.Count > 0)
        {
            body.AppendLine().AppendLine(text["indicators"]);
            foreach (var stat in stats)
            {
                body.AppendLine($"- {stat.Title}: {stat.StatValue ?? stat.Reason} ({stat.Caption})");
                foreach (var note in stat.Notes)
                {
                    body.AppendLine($"  {note}");
                }
            }
        }

        body.AppendLine().AppendLine(text[attachmentIncluded ? "attached" : "tooLarge"]);
        var left = new List<ReportWidget>();
        foreach (var widget in model.Tables)
        {
            if (!shown.Contains(widget.Position))
            {
                left.Add(widget);
                continue;
            }

            body.AppendLine().AppendLine(widget.Title).AppendLine(widget.Caption);
            foreach (var note in widget.Notes)
            {
                body.AppendLine(note);
            }

            if (widget.Reason is { } reason)
            {
                body.AppendLine(reason);
                continue;
            }

            body.AppendLine(string.Join(" | ", widget.Columns.Select(c => c.Header)));
            foreach (var row in widget.Rows.Take(options.RowsPerBlock))
            {
                body.AppendLine(string.Join(" | ", row.Select(c => c.Text)));
            }

            if (widget.Rows.Count > options.RowsPerBlock)
            {
                body.AppendLine(text.MoreRows(widget.Rows.Count - options.RowsPerBlock));
            }
        }

        if (left.Count > 0)
        {
            body.AppendLine().AppendLine(text["notShown"]);
            foreach (var widget in left)
            {
                body.AppendLine($"- {widget.Title}");
            }
        }

        body.AppendLine().AppendLine("--")
            .AppendLine($"{text.Format("footer.scheduled", model.ProjectName)} {text["footer.stop"]}")
            .AppendLine(text.Format("footer.open", model.DashboardAddress));
        return body.ToString();
    }
}
