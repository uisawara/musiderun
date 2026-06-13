using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Works.Mmzk.Util.Musiderun.Editor;

namespace Works.Mmzk.Util.Musiderun.Tests
{
    /// <summary>
    /// <see cref="GitWorktreeMirrorSync.CaptureWorkingTreeStateAsync"/> が
    /// 作業ツリーの状態を「内容ベースで決定的」に指紋化することを検証する。
    ///
    /// 過去に git stash create のコミット SHA をそのまま指紋に使っていたため、
    /// 内容が同一でも呼び出すたびに値が変わり（コミット日時が埋め込まれるため）、
    /// ミラー同期前後比較が偽陽性で Job を中断していた。
    /// </summary>
    public sealed class GitWorktreeMirrorStateFingerprintTests
    {
        private string _repoRoot;
        private string _gitExecutable;

        [SetUp]
        public void SetUp()
        {
            if (!PlatformUtility.TryResolveGitExecutable(out _gitExecutable, out var error))
            {
                Assert.Ignore($"git が見つからないためテストをスキップします: {error}");
            }

            _repoRoot = Path.Combine(
                Path.GetTempPath(),
                "musiderun_fp_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_repoRoot);

            RunGit("init");
            RunGit("config", "user.email", "test@example.com");
            RunGit("config", "user.name", "musiderun-test");
            RunGit("config", "commit.gpgsign", "false");

            WriteFile("tracked.txt", "line 1\nline 2\n");
            RunGit("add", "-A");
            RunGit("commit", "-m", "initial");
        }

        [TearDown]
        public void TearDown()
        {
            TryDeleteDirectory(_repoRoot);
        }

        [Test]
        public void ReturnsStableFingerprint_WhenWorkingTreeClean()
        {
            var first = Capture();
            Thread.Sleep(1200);
            var second = Capture();

            Assert.That(
                first,
                Is.Not.Empty,
                "クリーンな作業ツリーでも内容ベースの決定的な指紋を返すべき。");
            Assert.That(
                second,
                Is.EqualTo(first),
                "クリーン時の指紋は呼び出すたびに同じであるべき（決定的）。");
        }

        [Test]
        public void ReturnsStableFingerprint_WhenTrackedFileModified()
        {
            WriteFile("tracked.txt", "line 1\nMODIFIED\nline 2\n");

            var first = Capture();

            // git のコミット日時は秒単位。古い実装（コミット SHA を指紋に使用）では
            // この待機を挟むと 1 回目と 2 回目で別ハッシュになり、テストが失敗する。
            Thread.Sleep(1200);

            var second = Capture();

            Assert.That(first, Is.Not.Empty, "変更があるなら指紋は空であってはならない。");
            Assert.That(
                second,
                Is.EqualTo(first),
                "内容が変わっていなければ、呼び出すたびに同じ指紋を返すべき（決定的）。");
        }

        [Test]
        public void ReturnsStableFingerprint_WithUntrackedFile()
        {
            WriteFile("tracked.txt", "line 1\nchanged\nline 2\n");
            WriteFile("untracked.txt", "new untracked content\n");

            var first = Capture();
            Thread.Sleep(1200);
            var second = Capture();

            Assert.That(first, Is.Not.Empty);
            Assert.That(
                second,
                Is.EqualTo(first),
                "未追跡ファイルを含む場合も指紋は決定的であるべき。");
        }

        [Test]
        public void ReturnsDifferentFingerprint_ForDifferentContent()
        {
            WriteFile("tracked.txt", "variant A\n");
            var variantA = Capture();

            WriteFile("tracked.txt", "variant B\n");
            var variantB = Capture();

            Assert.That(variantA, Is.Not.Empty);
            Assert.That(variantB, Is.Not.Empty);
            Assert.That(
                variantB,
                Is.Not.EqualTo(variantA),
                "内容が異なる場合は異なる指紋を返すべき。");
        }

        private string Capture()
        {
            return GitWorktreeMirrorSync
                .CaptureWorkingTreeStateAsync(_gitExecutable, _repoRoot, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        private void RunGit(params string[] arguments)
        {
            var result = PlatformUtility
                .RunGitSnapshotProcessAsync(_gitExecutable, arguments, _repoRoot)
                .GetAwaiter()
                .GetResult();

            Assert.That(
                result.Succeeded,
                Is.True,
                $"git {string.Join(" ", arguments)} が失敗 (exit {result.ExitCode}): {result.StandardError}");
        }

        private void WriteFile(string relativePath, string content)
        {
            var fullPath = Path.Combine(_repoRoot, relativePath);
            File.WriteAllText(fullPath, content);
        }

        private static void TryDeleteDirectory(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                return;
            }

            try
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }
                    catch
                    {
                        // 属性変更に失敗しても削除を試みる。
                    }
                }

                Directory.Delete(path, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[musiderun-test] 一時リポジトリ削除に失敗: {ex.Message}");
            }
        }
    }
}
