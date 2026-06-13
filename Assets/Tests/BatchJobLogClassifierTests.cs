using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Works.Mmzk.Util.Musiderun.Editor;

namespace Works.Mmzk.Util.Musiderun.Tests
{
    public sealed class BatchJobLogClassifierTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "musiderun_log_classifier_" + Guid.NewGuid().ToString("N"));
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
        public void Classify_MirrorStderr_PreparingWorktree_IsInfo()
        {
            var entries = ClassifyMirror("[stderr] Preparing worktree (detached HEAD abc12345)");

            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0].Severity, Is.EqualTo(BatchJobLogLineSeverity.Info));
            Assert.That(entries[0].SectionId, Is.EqualTo(BatchJobLogClassifier.SectionMusiderun));
        }

        [Test]
        public void Classify_MirrorStderr_Fatal_IsError()
        {
            var entries = ClassifyMirror("[stderr] fatal: not a git repository");

            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0].Severity, Is.EqualTo(BatchJobLogLineSeverity.Error));
        }

        [Test]
        public void Classify_UnityImport_VisualScriptingExceptionsPath_IsInfo()
        {
            var line =
                "Start importing Packages/com.unity.visualscripting/Editor/VisualScripting.Core/Exceptions " +
                "using Guid(7261815fb7b1c4aeb899c8f9cc18d5d8) Importer(-1,00000000000000000000000000000000)";
            var entries = ClassifyUnity(line);

            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0].Severity, Is.EqualTo(BatchJobLogLineSeverity.Info));
            Assert.That(entries[0].SectionId, Is.EqualTo(BatchJobLogClassifier.SectionPackageManager));
        }

        [Test]
        public void Classify_UnityImport_UnityWebRequestExceptionFile_IsInfo()
        {
            var line =
                "Start importing Packages/com.cysharp.unitask/Runtime/UnityWebRequestException.cs " +
                "using Guid(013a499e522703a42962a779b4d9850c) Importer(-1,00000000000000000000000000000000)";
            var entries = ClassifyUnity(line);

            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0].Severity, Is.EqualTo(BatchJobLogLineSeverity.Info));
            Assert.That(entries[0].SectionId, Is.EqualTo(BatchJobLogClassifier.SectionPackageManager));
        }

        [Test]
        public void Classify_Unity_NullReferenceException_IsError()
        {
            var entries = ClassifyUnity("NullReferenceException: Object reference not set to an instance of an object");

            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0].Severity, Is.EqualTo(BatchJobLogLineSeverity.Error));
        }

        [Test]
        public void Classify_Unity_CompileError_IsError()
        {
            var entries = ClassifyUnity("error CS1002: ; expected");

            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0].Severity, Is.EqualTo(BatchJobLogLineSeverity.Error));
        }

        [Test]
        public void Classify_Unity_CompilingScriptsThenImport_UsesExpectedSections()
        {
            var entries = ClassifyUnity(
                "Compiling Scripts for Editor",
                "Start importing Packages/com.example/Sample.cs using Guid(abc) Importer(-1,00000000000000000000000000000000)");

            Assert.That(entries, Has.Count.EqualTo(2));
            Assert.That(entries[0].Severity, Is.EqualTo(BatchJobLogLineSeverity.Info));
            Assert.That(entries[0].SectionId, Is.EqualTo(BatchJobLogClassifier.SectionScriptCompilation));
            Assert.That(entries[1].Severity, Is.EqualTo(BatchJobLogLineSeverity.Info));
            Assert.That(entries[1].SectionId, Is.EqualTo(BatchJobLogClassifier.SectionPackageManager));
        }

        [Test]
        public void Classify_Unity_ShaderFallbackNotFound_IsWarning()
        {
            var line =
                "Shader 'VRM10/Universal Render Pipeline/MToon10': fallback shader " +
                "'Hidden/Universal Render Pipeline/FallbackError' not found";
            var entries = ClassifyUnity(line);

            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0].Severity, Is.EqualTo(BatchJobLogLineSeverity.Warning));
        }

        [Test]
        public void Classify_Unity_ShaderNotFound_IsWarning()
        {
            var entries = ClassifyUnity("Shader 'Custom/Foo' not found");

            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0].Severity, Is.EqualTo(BatchJobLogLineSeverity.Warning));
        }

        [Test]
        public void Classify_Unity_CompilerWarning_IsWarning()
        {
            var entries = ClassifyUnity("warning CS0618: 'LegacyApi' is obsolete");

            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0].Severity, Is.EqualTo(BatchJobLogLineSeverity.Warning));
        }

        [Test]
        public void Classify_Unity_MissingScript_IsWarning()
        {
            var entries = ClassifyUnity("The referenced script on this Behaviour is missing!");

            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0].Severity, Is.EqualTo(BatchJobLogLineSeverity.Warning));
        }

        [Test]
        public void Classify_Unity_ImportProblem_IsWarning()
        {
            var entries = ClassifyUnity("Problem detected while importing the asset 'Assets/Broken.fbx'");

            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0].Severity, Is.EqualTo(BatchJobLogLineSeverity.Warning));
        }

        private List<BatchJobLogLineEntry> ClassifyMirror(params string[] lines)
        {
            var logPath = WriteLog("mirror.log", lines);
            return BatchJobLogClassifier.Classify(new BatchJobLogHtmlRequest
            {
                MirrorLogFilePath = logPath
            });
        }

        private List<BatchJobLogLineEntry> ClassifyUnity(params string[] lines)
        {
            var logPath = WriteLog("unity.log", lines);
            return BatchJobLogClassifier.Classify(new BatchJobLogHtmlRequest
            {
                UnityLogFilePath = logPath
            });
        }

        private string WriteLog(string fileName, IEnumerable<string> lines)
        {
            var logPath = Path.Combine(_tempDir, fileName);
            File.WriteAllLines(logPath, lines);
            return logPath;
        }
    }
}
