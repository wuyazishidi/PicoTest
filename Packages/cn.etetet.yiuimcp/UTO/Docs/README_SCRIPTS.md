# UTO 测试脚本 - 完成总结

## 已完成的工作

### 1. PowerShell 脚本
- ✅ `invoke-uto-tool.ps1` - 通用工具调用脚本
- ✅ `compile-unity.ps1` - Unity 编译专用脚本

### 2. 文档
- ✅ `invoke-uto-tool.md` - 通用脚本使用说明
- ✅ `compile-unity.md` - 编译脚本使用说明

### 3. Unity 代码
- ✅ 修改 `YIUIMCPUnityToolsData.cs` - 调用 PowerShell 脚本

---

## 使用方式

### Unity Editor 测试
1. 打开 Unity Editor
2. 打开 YIUI MCP 工具面板
3. 选择工具（如 Log）
4. 点击执行按钮
5. 会弹出 PowerShell 窗口执行脚本

### 命令行测试（AI 调用方式）

**调用单个工具**：
```powershell
cd Packages/cn.etetet.yiuimcp/UTO
.\invoke-uto-tool.ps1 -Tool Log -Params '{"message":"测试"}'
```

**编译项目**：
```powershell
cd Packages/cn.etetet.yiuimcp/UTO
.\compile-unity.ps1 -Force $false
```

---

## 下一步

1. **在 Unity 中编译**（需要关闭 Rider）
2. **测试 Unity 按钮调用**
3. **测试命令行调用**

---

## 关键改动

- Unity 不再启动任何服务，只调用外部脚本
- 脚本负责启动 UTO、发送请求、显示结果
- AI 和 Unity 使用完全相同的脚本
- 窗口保持打开，方便查看日志
