using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Works.Mmzk.Util.Musiderun;
using Works.Mmzk.Util.Musiderun.Editor;

namespace Works.Mmzk.Util.Musiderun.Tests
{
    public sealed class TestResultParserTests
    {
        private const string SampleXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<test-run id=""2"" testcasecount=""3"" result=""Failed"" total=""3"" passed=""1"" failed=""1"" inconclusive=""0"" skipped=""1"">
  <test-suite name=""SampleTests"" fullname=""SampleTests"" type=""TestFixture"" result=""Failed"" total=""3"" passed=""1"" failed=""1"" skipped=""1"">
    <test-suite name=""NestedGroup"" fullname=""SampleTests+NestedGroup"" type=""TestSuite"" result=""Failed"" total=""2"" passed=""0"" failed=""1"" skipped=""1"">
      <test-case id=""1002"" name=""FailingTest"" fullname=""SampleTests.NestedGroup.FailingTest"" classname=""SampleTests+NestedGroup"" result=""Failed"" duration=""0.034"">
        <failure>
          <message><![CDATA[Expected: True
  But was: False]]></message>
          <stack-trace><![CDATA[at SampleTests.FailingTest () [0x00001] in SampleTests.cs:line 10]]></stack-trace>
        </failure>
      </test-case>
      <test-case id=""1003"" name=""SkippedTest"" fullname=""SampleTests.NestedGroup.SkippedTest"" classname=""SampleTests+NestedGroup"" result=""Skipped"" duration=""0.000"" />
    </test-suite>
    <test-case id=""1001"" name=""PassingTest"" fullname=""SampleTests.PassingTest"" classname=""SampleTests"" result=""Passed"" duration=""0.012"" />
  </test-suite>
</test-run>";

        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "musiderun_test_results_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }

        [Test]
        public void Parse_ReturnsSummaryCounts()
        {
            var xmlPath = WriteSampleXml();

            var summary = TestResultParser.Parse(xmlPath);

            Assert.That(summary.Parsed, Is.True);
            Assert.That(summary.Total, Is.EqualTo(3));
            Assert.That(summary.Passed, Is.EqualTo(1));
            Assert.That(summary.Failed, Is.EqualTo(1));
            Assert.That(summary.Skipped, Is.EqualTo(1));
        }

        [Test]
        public void ParseTestCases_ReturnsCasesWithFailureDetails()
        {
            var xmlPath = WriteSampleXml();

            var testCases = TestResultParser.ParseTestCases(xmlPath);

            Assert.That(testCases, Has.Count.EqualTo(3));
            var failingCase = testCases.First(testCase =>
                testCase.FullName == "SampleTests.NestedGroup.FailingTest");
            Assert.That(failingCase.FailureMessage, Does.Contain("Expected: True"));
            Assert.That(failingCase.StackTrace, Does.Contain("SampleTests.FailingTest"));
        }

        [Test]
        public void ParseTestTree_PreservesSuiteHierarchy()
        {
            var xmlPath = WriteSampleXml();

            var root = TestResultParser.ParseTestTree(xmlPath);

            Assert.That(root, Is.Not.Null);
            Assert.That(root.Suites, Has.Count.EqualTo(1));
            Assert.That(root.Suites[0].Name, Is.EqualTo("SampleTests"));
            Assert.That(root.Suites[0].Suites, Has.Count.EqualTo(1));
            Assert.That(root.Suites[0].Suites[0].Name, Is.EqualTo("NestedGroup"));
            Assert.That(root.Suites[0].Suites[0].Cases, Has.Count.EqualTo(2));
            Assert.That(root.Suites[0].Cases, Has.Count.EqualTo(1));
            Assert.That(root.Suites[0].Cases[0].FullName, Is.EqualTo("SampleTests.PassingTest"));
        }

        [Test]
        public void TryRender_WritesHtmlWithTestCaseNames()
        {
            var xmlPath = WriteSampleXml();
            var htmlPath = Path.Combine(_tempDir, "results.html");
            var request = new TestResultHtmlRequest
            {
                JobId = "editmode-tests",
                DisplayName = "EditMode Tests",
                FinalState = BatchJobState.Failed,
                StartedAt = DateTime.Now.AddMinutes(-1),
                FinishedAt = DateTime.Now,
                ResultsXmlPath = xmlPath,
                OutputHtmlPath = htmlPath,
                TestSummary = TestResultParser.Parse(xmlPath)
            };

            var rendered = TestResultHtmlRenderer.TryRender(request, out var error);

            Assert.That(rendered, Is.True, error);
            Assert.That(File.Exists(htmlPath), Is.True);
            var html = File.ReadAllText(htmlPath);
            Assert.That(html, Does.Contain("SampleTests.PassingTest"));
            Assert.That(html, Does.Contain("SampleTests.NestedGroup.FailingTest"));
            Assert.That(html, Does.Contain("NestedGroup"));
            Assert.That(html, Does.Contain("details class=\"test-suite\""));
            Assert.That(html, Does.Contain("Expected: True"));
            Assert.That(html, Does.Contain("results.xml"));
        }

        private string WriteSampleXml()
        {
            var xmlPath = Path.Combine(_tempDir, "sample-results.xml");
            File.WriteAllText(xmlPath, SampleXml);
            return xmlPath;
        }
    }
}
