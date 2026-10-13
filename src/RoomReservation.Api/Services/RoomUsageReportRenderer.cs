using System.Globalization;
using System.Net;
using System.Text;
using RoomReservation.Api.Dtos;

namespace RoomReservation.Api.Services;

/// <summary>
/// Renders the room usage report as a standalone HTML document that carries its own print
/// stylesheet and calls window.print() on load. The browser's print dialog produces the PDF
/// ("Save as PDF"), which keeps the backend free of a native PDF dependency.
/// </summary>
public class RoomUsageReportRenderer : IRoomUsageReportRenderer
{
    public string Render(RoomUsageReportDto report)
    {
        var sb = new StringBuilder();
        var culture = CultureInfo.InvariantCulture;

        string Pct(double? value) => value is null ? "n/a" : (value.Value * 100).ToString("0.#", culture) + "%";
        string H(double value) => value.ToString("0.#", culture) + " h";
        string Enc(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

        sb.Append("""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8" />
            <title>Room usage report</title>
            <style>
              @page { size: A4 landscape; margin: 12mm; }
              * { box-sizing: border-box; }
              body { font-family: "Segoe UI", Arial, sans-serif; color: #16202c; margin: 0; padding: 24px; background: #fff; }
              h1 { font-size: 20px; margin: 0 0 4px; }
              .meta { color: #5a6b7d; font-size: 12px; margin-bottom: 18px; }
              .cards { display: flex; flex-wrap: wrap; gap: 12px; margin-bottom: 20px; }
              .card { border: 1px solid #d8e0e8; border-radius: 8px; padding: 10px 14px; min-width: 130px; }
              .card .label { font-size: 11px; text-transform: uppercase; letter-spacing: .04em; color: #6b7c8f; }
              .card .value { font-size: 18px; font-weight: 600; margin-top: 2px; }
              table { width: 100%; border-collapse: collapse; font-size: 12px; }
              th, td { border-bottom: 1px solid #e3e9ef; padding: 7px 8px; text-align: left; }
              th { background: #f4f7fa; font-size: 11px; text-transform: uppercase; letter-spacing: .03em; color: #4a5b6d; }
              td.num, th.num { text-align: right; }
              tr.underused { background: #fff5f5; }
              .tag { display: inline-block; padding: 2px 8px; border-radius: 999px; font-size: 11px; font-weight: 600; }
              .tag.bad { background: #ffe3e3; color: #a01515; }
              .tag.warn { background: #fff3d6; color: #8a5a00; }
              .tag.good { background: #e2f5e7; color: #1b6b33; }
              .tag.muted { background: #eef2f6; color: #5a6b7d; }
              .toolbar { margin-bottom: 16px; }
              button { font: inherit; padding: 8px 16px; border-radius: 6px; border: 1px solid #c3d0dc; background: #f7fafc; cursor: pointer; }
              footer { margin-top: 18px; font-size: 11px; color: #6b7c8f; }
              @media print { .toolbar { display: none; } body { padding: 0; } }
            </style>
            </head>
            <body>
            """);

        sb.Append("<div class=\"toolbar\"><button onclick=\"window.print()\">Save as PDF / Print</button></div>");
        sb.Append("<h1>Room usage report</h1>");
        sb.Append("<div class=\"meta\">Generated ")
          .Append(report.GeneratedAt.ToString("yyyy-MM-dd HH:mm", culture))
          .Append(" UTC");
        sb.Append(report.LastDays is null
            ? " &middot; all time"
            : $" &middot; last {report.LastDays} days");
        sb.Append(" &middot; a room counts as &quot;often booked, rarely used&quot; when utilization is below ")
          .Append((report.UnderusedThreshold * 100).ToString("0.#", culture))
          .Append("% over at least ")
          .Append(report.MinimumMeetingsForUsageVerdict)
          .Append(" finished meetings.</div>");

        var s = report.Summary;
        sb.Append("<div class=\"cards\">");
        sb.Append(Card("Rooms", s.Rooms.ToString(culture)));
        sb.Append(Card("Reservations", s.TotalReservations.ToString(culture)));
        sb.Append(Card("Finished meetings", s.CompletedMeetings.ToString(culture)));
        sb.Append(Card("Reserved hours", H(s.ReservedHours)));
        sb.Append(Card("Actually used", H(s.ActualHours)));
        sb.Append(Card("Wasted hours", H(s.WastedHours)));
        sb.Append(Card("Overall utilization", Pct(s.Utilization)));
        sb.Append(Card("Underused rooms", s.UnderusedRooms.ToString(culture)));
        sb.Append("</div>");

        sb.Append("""
            <table>
            <thead>
            <tr>
              <th>Room</th><th>Floor</th><th class="num">Capacity</th>
              <th class="num">Reservations</th><th class="num">Finished</th><th class="num">Cancelled</th>
              <th class="num">Reserved</th><th class="num">Used</th><th class="num">Wasted</th>
              <th class="num">Booked / actual people</th><th class="num">Utilization</th><th>Verdict</th>
            </tr>
            </thead>
            <tbody>
            """);

        foreach (var room in report.Rooms)
        {
            var css = room.IsUnderused ? " class=\"underused\"" : string.Empty;
            sb.Append("<tr").Append(css).Append('>');
            sb.Append("<td><strong>").Append(Enc(room.RoomName)).Append("</strong><br /><span style=\"color:#6b7c8f\">")
              .Append(Enc(room.RoomCode)).Append(room.IsActive ? string.Empty : " &middot; inactive").Append("</span></td>");
            sb.Append("<td>").Append(Enc(room.Floor)).Append("</td>");
            sb.Append("<td class=\"num\">").Append(room.Capacity).Append("</td>");
            sb.Append("<td class=\"num\">").Append(room.TotalReservations).Append("</td>");
            sb.Append("<td class=\"num\">").Append(room.CompletedMeetings).Append("</td>");
            sb.Append("<td class=\"num\">").Append(room.CancelledReservations).Append("</td>");
            sb.Append("<td class=\"num\">").Append(H(room.ReservedHours)).Append("</td>");
            sb.Append("<td class=\"num\">").Append(H(room.ActualHours)).Append("</td>");
            sb.Append("<td class=\"num\">").Append(H(room.WastedHours)).Append("</td>");
            sb.Append("<td class=\"num\">").Append(room.BookedAttendees).Append(" / ").Append(room.ActualAttendees).Append("</td>");
            sb.Append("<td class=\"num\">").Append(Pct(room.Utilization)).Append("</td>");
            sb.Append("<td>").Append(Tag(room)).Append("</td>");
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
        sb.Append("<footer>Utilization = people who actually attended divided by people booked, ")
          .Append("counted over finished meetings that recorded attendance.</footer>");
        sb.Append("<script>window.addEventListener('load',function(){setTimeout(function(){window.print();},300);});</script>");
        sb.Append("</body></html>");

        return sb.ToString();

        static string Card(string label, string value) =>
            $"<div class=\"card\"><div class=\"label\">{WebUtility.HtmlEncode(label)}</div><div class=\"value\">{WebUtility.HtmlEncode(value)}</div></div>";

        static string Tag(RoomUsageDto room)
        {
            var css = room.IsUnderused ? "bad" : room.Verdict switch
            {
                UsageVerdicts.WellUsed => "good",
                UsageVerdicts.Moderate => "warn",
                _ => "muted"
            };
            return $"<span class=\"tag {css}\">{WebUtility.HtmlEncode(room.Verdict)}</span>";
        }
    }
}
