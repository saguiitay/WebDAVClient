using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WebDAVClient.InteropTests;

[TestClass]
public sealed class SyncReportEvidenceTests
{
    [TestMethod]
    public void ConfirmedEmptyDiscoveryIsNotMissingMalformedOrAdvertisedDiscovery()
    {
        Assert.IsTrue(SyncReportEvidence.IsConfirmedEmptyReportSet(XElement.Parse("<d:supported-report-set xmlns:d='DAV:'/>")));
        Assert.IsFalse(SyncReportEvidence.IsConfirmedEmptyReportSet(XElement.Parse("<d:other xmlns:d='DAV:'/>")));
        Assert.IsFalse(SyncReportEvidence.IsConfirmedEmptyReportSet(XElement.Parse("<d:supported-report-set xmlns:d='DAV:'>unknown</d:supported-report-set>")));
        Assert.IsFalse(SyncReportEvidence.IsConfirmedEmptyReportSet(XElement.Parse(
            "<d:supported-report-set xmlns:d='DAV:'><d:supported-report><d:report><d:sync-collection/></d:report></d:supported-report></d:supported-report-set>")));
    }

    [TestMethod]
    public void PinnedBackendRejectionRetains415AndCauseWithoutClaimingSyncSupport()
    {
        var rejected = new Observation("sync-report", "failed", 415, [], []);
        var observed = Classify(rejected, KnownError());
        Assert.AreEqual("unsupported", observed.Outcome);
        Assert.AreEqual(415, observed.HttpStatus);
        Assert.AreEqual("sync-report", observed.Operation);
        Assert.IsTrue(observed.Detail!.Contains("SabreDAV-ReportNotSupported", StringComparison.Ordinal));
        Assert.IsTrue(observed.Detail.Contains("no RFC6578 coverage", StringComparison.Ordinal));
        CollectionAssert.AreEqual(rejected.MemberStatuses, observed.MemberStatuses);
    }

    [TestMethod]
    public void ActualIndependentServerErrorFixtureConfirmsUnsupportedReportNotInvalidMediaType()
    {
        var body = XElement.Load(Path.Combine(AppContext.BaseDirectory, "sabredav-report-response.xml"));
        var response = Classify(new Observation("sync-report", "failed", 415), body);
        Assert.AreEqual("unsupported", response.Outcome);
        Assert.AreEqual(415, response.HttpStatus);
    }

    [DataTestMethod]
    [DataRow(400)]
    [DataRow(403)]
    [DataRow(405)]
    [DataRow(500)]
    public void OtherStatusesAreNotBlessedByAnErrorElement(int status)
    {
        var rejected = new Observation("sync-report", "failed", status);
        Assert.AreSame(rejected, Classify(rejected, KnownError()));
    }

    [TestMethod]
    public void Generic415OrUnsupportedMediaTypeIsStillAFailure()
    {
        var rejected = new Observation("sync-report", "failed", 415);
        Assert.AreSame(rejected, Classify(rejected, null));
        Assert.AreSame(rejected, Classify(rejected, XElement.Parse("<error/>")));
        var wrongCause = KnownError();
        wrongCause.Element(XName.Get("exception", "http://sabredav.org/ns"))!.Value = @"Sabre\DAV\Exception\UnsupportedMediaType";
        Assert.AreSame(rejected, Classify(rejected, wrongCause));
    }

    [TestMethod]
    public void BodyCauseAndDavPreconditionAreBothRequiredAndUnambiguous()
    {
        var rejected = new Observation("sync-report", "failed", 415);
        var missingCondition = KnownError();
        missingCondition.Element(XName.Get("supported-report", "DAV:"))!.Remove();
        Assert.AreSame(rejected, Classify(rejected, missingCondition));
        var missingException = KnownError();
        missingException.Element(XName.Get("exception", "http://sabredav.org/ns"))!.Remove();
        Assert.AreSame(rejected, Classify(rejected, missingException));
        var duplicate = KnownError();
        duplicate.Add(new XElement(duplicate.Element(XName.Get("exception", "http://sabredav.org/ns"))!));
        Assert.AreSame(rejected, Classify(rejected, duplicate));
    }

    [TestMethod]
    public void MissingDiscoveryAdvertisedSyncOrDifferentProfileRemainFailures()
    {
        var rejected = new Observation("sync-report", "failed", 415);
        var error = KnownError();
        Assert.AreSame(rejected, SyncReportEvidence.Classify(rejected, "sabredav", "4.7.0+php-8.3.25", false, [], error));
        Assert.AreSame(rejected, SyncReportEvidence.Classify(rejected, "sabredav", "4.7.0+php-8.3.25", true, ["{DAV:}sync-collection"], error));
        Assert.AreSame(rejected, SyncReportEvidence.Classify(rejected, "other", "4.7.0+php-8.3.25", true, [], error));
        Assert.AreSame(rejected, SyncReportEvidence.Classify(rejected, "sabredav", "different-version", true, [], error));
    }

    private static Observation Classify(Observation response, XElement? error) =>
        SyncReportEvidence.Classify(response, "sabredav", "4.7.0+php-8.3.25", true, [], error);

    private static XElement KnownError() => XElement.Parse(
        "<d:error xmlns:d='DAV:' xmlns:s='http://sabredav.org/ns'>" +
        @"<s:exception>Sabre\DAV\Exception\ReportNotSupported</s:exception>" +
        "<s:message>Not supported on this node</s:message><d:supported-report/></d:error>");
}
