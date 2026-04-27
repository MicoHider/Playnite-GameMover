# GameMover — Playnite 游戏移动插件

在 Playnite 游戏库中右键任意游戏，即可看到 **「改变存储位置」** 选项，一键把整个游戏文件夹搬到新磁盘 / 新目录，并自动更新 Playnite 的记录。

---

## 功能亮点

| 功能 | 说明 |
|------|------|
| 右键菜单集成 | 在游戏库右键菜单顶层直接显示「改变存储位置」 |
| 同盘瞬间移动 | 同一磁盘内使用系统级 `Directory.Move`，几乎瞬间完成 |
| 跨盘带进度 | 跨磁盘时逐文件复制并显示进度条（可取消） |
| 自动更新路径 | 移动完成后自动更新 `InstallDirectory` 及所有 `GameActions` 路径 |
| 操作可取消 | 进度窗口中点"取消"后自动尝试回滚 |
| 安全确认流程 | 移动前弹窗展示源路径与目标路径，需二次确认 |

---

## 目录结构

```
GameMover/
├── GameMover.csproj        # C# 项目文件（目标框架：net462）
├── GameMoverPlugin.cs      # 插件主逻辑
├── src/
│   └── extension.yaml      # Playnite 扩展清单
├── build_and_install.ps1   # 一键构建 + 安装脚本
└── README.md               # 本说明
```

---

## 环境要求

| 依赖 | 版本 |
|------|------|
| Playnite | 9 或 10（SDK v1/v2 均可） |
| .NET SDK | 6.0+ 或 Visual Studio 2022（含 .NET Framework 4.6.2 工具） |
| Windows | 7 / 10 / 11 |

---

## 构建 & 安装（一键脚本）

打开 **PowerShell**，进入本项目目录，运行：

```powershell
# 如果 Playnite 安装在默认位置，直接运行：
.\build_and_install.ps1

# 如果 Playnite 在非默认位置，指定路径：
.\build_and_install.ps1 -PlaynitePath "D:\Games\Playnite"
```

脚本会自动：
1. 从 Playnite 安装目录复制 `Playnite.SDK.dll` 到 `lib\`
2. 使用 `dotnet build` 或 `msbuild` 编译插件
3. 将 `GameMover.dll` + `extension.yaml` 复制到 `%APPDATA%\Playnite\Extensions\GameMover\`

**重启 Playnite** 即可生效。

---

## 手动安装

如果不想用脚本：

1. 把 `Playnite.SDK.dll`（从 Playnite 安装目录复制）放到 `lib\` 文件夹。
2. 用 Visual Studio / Rider / `dotnet build` 编译，得到 `bin\Release\GameMover.dll`。
3. 在 `%APPDATA%\Playnite\Extensions\` 下新建文件夹 `GameMover`。
4. 把以下两个文件复制进去：
   - `GameMover.dll`
   - `src\extension.yaml`
5. 重启 Playnite。

---

## 使用方法

1. 在 Playnite 游戏库中，**右键**想要移动的游戏。
2. 点击菜单中的 **「改变存储位置」**。
3. 在弹出的文件夹选择框中，选择**目标父目录**（游戏文件夹本身会被放在这个目录下）。
4. 确认对话框中核对路径，点击 **「是」** 开始移动。
5. 等待进度条完成（同盘几乎瞬间，跨盘视文件大小而定）。
6. 完成后 Playnite 数据库自动更新，游戏可正常启动。

> **提示**：若游戏正在运行，请先退出游戏再执行移动操作。

---

## 常见问题

**Q：移动中途断电 / 取消了怎么办？**  
A：插件会自动尝试回滚（仅限跨盘复制场景）。若回滚失败，源目录通常仍完整；目标目录中已复制的文件需手动清理。建议移动大型游戏前确认磁盘剩余空间充足。

**Q：同盘移动为什么瞬间完成？**  
A：同盘移动使用 `Directory.Move`（操作系统级重命名），不需要实际复制任何字节。

**Q：移动后游戏启动器（Steam / GOG 等）会不会出问题？**  
A：Playnite 自身的启动路径已更新。但若你同时使用原生 Steam / GOG 客户端，它们的记录不会被本插件修改，可能需要在对应客户端中重新扫描游戏位置。

---

## 许可证

MIT License — 自由使用与修改。
