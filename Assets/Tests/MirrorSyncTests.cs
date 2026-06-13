using System;
using System.IO;
using System.Threading;
using NUnit.Framework;
using Works.Mmzk.Util.Musiderun.Editor;

namespace Works.Mmzk.Util.Musiderun.Tests
{
    /// <summary>
    /// <see cref="GitWorktreeMirrorSync.SyncMirrorToCommitAsync"/> の検証。
    ///
    /// 簡素化後の同期は「コミット済み(指定コミット = メインの HEAD)の内容のみを
    /// detached なミラー worktree へ reset --hard する」方式。検証観点:
    /// (1) ミラーが指定コミットの内容に一致する
    /// (2) HEAD 更新後の再同期で追加/変更/削除が追従する
    /// (3) 未コミットの変更はミラーに含まれず、メイン作業ツリーも変更されない
    /// </summary>
    public sealed class MirrorSyncTests
    {
        private string _baseDir;
        private string _repoRoot;
        private string _mirrorPath;
        private string _gitExecutable;

        [SetUp]
        public void SetUp()
        {
            // ログコールバックがバックグラウンドスレッドから EditorApplication.update に
            // 触れないよう、メインスレッドで先にディスパッチャを登録しておく。
            EditorMainThreadDispatcher.EnsureRegistered();

            if (!PlatformUtility.TryResolveGitExecutable(out _gitExecutable, out var error))
            {
                Assert.Ignore($"git が見つからないためテストをスキップします: {error}");
            }

            _baseDir = Path.Combine(Path.GetTempPath(), "musiderun_mirror_" + Guid.NewGuid().ToString("N"));
            _repoRoot = Path.Combine(_baseDir, "repo");
            _mirrorPath = Path.Combine(_baseDir, "mirror");
            Directory.CreateDirectory(_repoRoot);

            RunGit(_repoRoot, "init");
            RunGit(_repoRoot, "config", "user.email", "test@example.com");
            RunGit(_repoRoot, "config", "user.name", "musiderun-test");
            RunGit(_repoRoot, "config", "commit.gpgsign", "false");

            WriteRepoFile("tracked.txt", "line 1\n");
            WriteRepoFile("old.txt", "to be deleted\n");
            RunGit(_repoRoot, "add", "-A");
            RunGit(_repoRoot, "commit", "-m", "initial");
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(_gitExecutable) &&
                !string.IsNullOrEmpty(_mirrorPath) &&
                Directory.Exists(_mirrorPath))
            {
                PlatformUtility.RunGitSnapshotProcessAsync(
                        _gitExecutable,
                        new[] { "worktree", "remove", "--force", _mirrorPath },
                        _repoRoot)
                    .GetAwaiter()
                    .GetResult();
            }

            TryDeleteDirectory(_baseDir);
        }

        [Test]
        public void Mirror_MatchesHeadCommit_AndLeavesMainUntouched()
        {
            Sync(Head());

            Assert.That(File.Exists(MirrorFile("tracked.txt")), Is.True, "コミット済みファイルがミラーに存在するべき。");
            Assert.That(File.ReadAllText(MirrorFile("tracked.txt")), Is.EqualTo("line 1\n"));
            Assert.That(File.Exists(MirrorFile("old.txt")), Is.True);

            Assert.That(StatusShort(_repoRoot), Is.Empty, "メイン作業ツリーは変更されないべき。");
        }

        [Test]
        public void Resync_FollowsHeadChanges()
        {
            Sync(Head());

            WriteRepoFile("tracked.txt", "line 1 updated\n");
            WriteRepoFile("added.txt", "new file\n");
            File.Delete(Path.Combine(_repoRoot, "old.txt"));
            RunGit(_repoRoot, "add", "-A");
            RunGit(_repoRoot, "commit", "-m", "update");

            Sync(Head());

            Assert.That(File.ReadAllText(MirrorFile("tracked.txt")), Is.EqualTo("line 1 updated\n"), "変更が追従するべき。");
            Assert.That(File.Exists(MirrorFile("added.txt")), Is.True, "追加が追従するべき。");
            Assert.That(File.Exists(MirrorFile("old.txt")), Is.False, "削除が追従するべき。");
        }

        [Test]
        public void Mirror_DoesNotIncludeUncommittedChanges()
        {
            Sync(Head());

            WriteRepoFile("tracked.txt", "UNCOMMITTED\n");
            WriteRepoFile("untracked_new.txt", "not committed\n");

            Sync(Head());

            Assert.That(
                File.ReadAllText(MirrorFile("tracked.txt")),
                Is.EqualTo("line 1\n"),
                "未コミットの変更はミラーに含まれないべき。");
            Assert.That(
                File.Exists(MirrorFile("untracked_new.txt")),
                Is.False,
                "未コミットの未追跡ファイルはミラーに含まれないべき。");

            Assert.That(
                File.ReadAllText(Path.Combine(_repoRoot, "tracked.txt")),
                Is.EqualTo("UNCOMMITTED\n"),
                "メインの未コミット変更は保持されるべき。");
        }

        private void Sync(string commit)
        {
            GitWorktreeMirrorSync
                .SyncMirrorToCommitAsync(_gitExecutable, _repoRoot, _mirrorPath, commit, _ => { }, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        private string Head()
        {
            var result = PlatformUtility
                .RunGitSnapshotProcessAsync(_gitExecutable, new[] { "rev-parse", "HEAD" }, _repoRoot)
                .GetAwaiter()
                .GetResult();
            Assert.That(result.Succeeded, Is.True, $"git rev-parse HEAD が失敗: {result.StandardError}");
            return result.StandardOutput.Trim();
        }

        private string StatusShort(string workingDirectory)
        {
            var result = PlatformUtility
                .RunGitSnapshotProcessAsync(_gitExecutable, new[] { "status", "--short" }, workingDirectory)
                .GetAwaiter()
                .GetResult();
            return result.StandardOutput.Trim();
        }

        private void RunGit(string workingDirectory, params string[] arguments)
        {
            var result = PlatformUtility
                .RunGitSnapshotProcessAsync(_gitExecutable, arguments, workingDirectory)
                .GetAwaiter()
                .GetResult();
            Assert.That(
                result.Succeeded,
                Is.True,
                $"git {string.Join(" ", arguments)} が失敗 (exit {result.ExitCode}): {result.StandardError}");
        }

        private string MirrorFile(string relativePath) => Path.Combine(_mirrorPath, relativePath);

        private void WriteRepoFile(string relativePath, string content)
        {
            File.WriteAllText(Path.Combine(_repoRoot, relativePath), content);
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
                Console.WriteLine($"[musiderun-test] 一時ディレクトリ削除に失敗: {ex.Message}");
            }
        }
    }
}
