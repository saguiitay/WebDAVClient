using System.Xml.Linq;

namespace WebDAVClient.InteropTests;

internal static class SyncReportEvidence
{
    private static readonly XNamespace s_dav = "DAV:";

    private static readonly XNamespace s_sabre = "http://sabredav.org/ns";

    internal static bool IsConfirmedEmptyReportSet(XElement property) =>
        property.Name == s_dav + "supported-report-set" && !property.HasElements && string.IsNullOrWhiteSpace(property.Value);

    internal static Observation Classify(Observation rejection, string server, string version, bool emptyReportSetConfirmed,
        IReadOnlyList<string> advertisedReports, XElement? error)
    {
        if (rejection.HttpStatus != 415 || server != "sabredav" || version != "4.7.0+php-8.3.25" ||
            !emptyReportSetConfirmed || advertisedReports.Contains("{DAV:}sync-collection", StringComparer.Ordinal) ||
            error?.Name != s_dav + "error")
        {
            return rejection;
        }
        var supportedReport = error.Elements(s_dav + "supported-report").ToArray();
        var exception = error.Elements(s_sabre + "exception").ToArray();
        if (supportedReport.Length != 1 || supportedReport[0].HasElements || !string.IsNullOrWhiteSpace(supportedReport[0].Value) ||
            exception.Length != 1 || exception[0].HasElements ||
            exception[0].Value != @"Sabre\DAV\Exception\ReportNotSupported")
        {
            return rejection;
        }
        return rejection with
        {
            Outcome = "unsupported",
            Detail = "optional-sync-profile-unavailable:SabreDAV-ReportNotSupported; DAV:supported-report; " +
                "confirmed-empty-supported-report-set; pinned-filesystem-backend-not-ISyncCollection. HTTP 415 retained; no RFC6578 coverage."
        };
    }
}
