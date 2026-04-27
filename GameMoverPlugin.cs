using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Playnite.SDK;
using Playnite.SDK.Plugins;

namespace GameMover
{
    /// <summary>
    /// Playnite plugin: adds a "改变存储位置" (Move Game) item to the game context menu.
    /// </summary>
    public class GameMoverPlugin : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        public override Guid Id { get; } = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");

        public GameMoverPlugin(IPlayniteAPI api) : base(api)
        {
        }

        // ─── Context menu ──────────────────────────────────────────────────────────

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            yield return new GameMenuItem
            {
                Description = "改变存储位置",
                MenuSection = "",   // top-level item (no sub-menu)
                Action = menuArgs =>
                {
                    // Only handle single-game selection for safety
                    var game = menuArgs.Games?.FirstOrDefault();
                    if (game == null)
                    {
                        PlayniteApi.Dialogs.ShowErrorMessage("未选中任何游戏。", "游戏移动器");
                        return;
                    }

                    MoveGame(game);
                }
            };
        }

        // ─── Move logic ────────────────────────────────────────────────────────────

        private void MoveGame(Playnite.SDK.Models.Game game)
        {
            // ── 1. Determine current install folder ──────────────────────────────
            string? currentPath = game.InstallDirectory;

            if (string.IsNullOrWhiteSpace(currentPath) || !Directory.Exists(currentPath))
            {
                PlayniteApi.Dialogs.ShowErrorMessage(
                    "游戏 \"" + game.Name + "\" 的当前安装目录无效或不存在：\n" + (currentPath ?? "(未设置)"),
                    "游戏移动器");
                return;
            }

            // Normalise (remove trailing separator)
            currentPath = currentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // ── 2. Ask user to pick destination parent folder ─────────────────────
            string gameFolderName = Path.GetFileName(currentPath);

            var destinationParent = PlayniteApi.Dialogs.SelectFolder();
            if (string.IsNullOrWhiteSpace(destinationParent))
                return; // user cancelled

            string newPath = Path.Combine(destinationParent, gameFolderName);

            // Guard: same location
            if (string.Equals(currentPath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                PlayniteApi.Dialogs.ShowMessage("新位置与当前位置相同，无需移动。", "游戏移动器");
                return;
            }

            // Guard: destination already exists
            if (Directory.Exists(newPath))
            {
                var overwrite = PlayniteApi.Dialogs.ShowMessage(
                    $"目标目录已存在：\n{newPath}\n\n是否覆盖（合并）？",
                    "游戏移动器",
                    MessageBoxButton.YesNo);

                if (overwrite != MessageBoxResult.Yes)
                    return;
            }

            // ── 3. Confirm with user ──────────────────────────────────────────────
            var confirm = PlayniteApi.Dialogs.ShowMessage(
                $"即将移动游戏：{game.Name}\n\n" +
                $"从：{currentPath}\n" +
                $"到：{newPath}\n\n" +
                "这可能需要一些时间，请耐心等待。是否继续？",
                "游戏移动器",
                MessageBoxButton.YesNo);

            if (confirm != MessageBoxResult.Yes)
                return;

            // ── 4. Move in background with progress window ────────────────────────
            var cts = new CancellationTokenSource();

            var progress = PlayniteApi.Dialogs.ActivateGlobalProgress(progressArgs =>
            {
                progressArgs.ProgressMaxValue = 100;
                progressArgs.CurrentProgressValue = 0;

                try
                {
                    MoveDirectory(currentPath, newPath, progressArgs, cts.Token);

                    // ── 5. Update game record ─────────────────────────────────────
                    PlayniteApi.MainView.UIDispatcher.Invoke(() =>
                    {
                        game.InstallDirectory = newPath;

                        // Update ROM / exe paths that point into the old directory
                        if (game.GameActions != null)
                        {
                            foreach (var action in game.GameActions)
                            {
                                if (!string.IsNullOrWhiteSpace(action.Path) &&
                                    action.Path.StartsWith(currentPath, StringComparison.OrdinalIgnoreCase))
                                {
                                    action.Path = newPath + action.Path.Substring(currentPath.Length);
                                }

                                if (!string.IsNullOrWhiteSpace(action.WorkingDir) &&
                                    action.WorkingDir.StartsWith(currentPath, StringComparison.OrdinalIgnoreCase))
                                {
                                    action.WorkingDir = newPath + action.WorkingDir.Substring(currentPath.Length);
                                }
                            }
                        }

                        PlayniteApi.Database.Games.Update(game);
                    });

                    progressArgs.CurrentProgressValue = 100;
                }
                catch (OperationCanceledException)
                {
                    // User cancelled — try to recover by moving back
                    logger.Warn("Game move cancelled by user, attempting rollback.");
                    TryRollback(newPath, currentPath, progressArgs);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Failed to move game directory.");

                    PlayniteApi.MainView.UIDispatcher.Invoke(() =>
                    {
                        PlayniteApi.Dialogs.ShowErrorMessage(
                            $"移动失败：{ex.Message}\n\n请检查是否有足够的磁盘空间或权限，然后手动恢复。",
                            "游戏移动器");
                    });
                }
            },
            new GlobalProgressOptions("正在移动游戏文件，请稍候…", true)   // cancelable = true
            {
                IsIndeterminate = false
            });
        }

        // ─── Directory copy/move helpers ───────────────────────────────────────────

        /// <summary>
        /// Recursively moves <paramref name="source"/> to <paramref name="dest"/>.
        /// Uses File.Move for same-volume moves (instant), falls back to copy+delete
        /// when crossing drive boundaries.
        /// </summary>
        private void MoveDirectory(
            string source,
            string dest,
            GlobalProgressActionArgs progress,
            CancellationToken ct)
        {
            bool sameDrive = string.Equals(
                Path.GetPathRoot(source),
                Path.GetPathRoot(dest),
                StringComparison.OrdinalIgnoreCase);

            if (sameDrive)
            {
                // Fast path: atomic rename / move within the same volume
                progress.Text = "正在移动（同盘快速模式）…";
                Directory.Move(source, dest);
                return;
            }

            // Slow path: cross-drive copy then delete
            var allFiles = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
            int total = allFiles.Length;
            int done = 0;

            foreach (var file in allFiles)
            {
                ct.ThrowIfCancellationRequested();

                string relative = file.Substring(source.Length).TrimStart(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string target = Path.Combine(dest, relative);

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);

                done++;
                progress.CurrentProgressValue = (double)done / total * 90; // leave 10 % for cleanup
                progress.Text = $"正在复制 ({done}/{total}): {Path.GetFileName(file)}";
            }

            // Delete source after successful copy
            progress.Text = "正在删除原始文件夹…";
            Directory.Delete(source, recursive: true);
        }

        private static void TryRollback(string partialDest, string originalSource, GlobalProgressActionArgs progress)
        {
            try
            {
                if (Directory.Exists(partialDest) && !Directory.Exists(originalSource))
                {
                    progress.Text = "正在回滚…";
                    Directory.Move(partialDest, originalSource);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Rollback failed.");
            }
        }
    }
}
