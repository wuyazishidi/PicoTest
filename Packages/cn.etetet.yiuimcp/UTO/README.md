# YIU MCP - UTO (Unity Tool Orchestrator)

Unity MCP 的编排层，提供 HTTP API 和自动心跳检测功能。

## 🚀 快速开始

### 安装依赖
```bash
npm install
```

### 编译
```bash
npm run build
```

### 启动 HTTP Server
```bash
npm run start:http
```

## 📁 目录结构

```
src/
├── heartbeat-manager.ts    # 心跳检测管理器
├── http-server.ts           # HTTP Server（集成心跳）
├── index.ts                 # MCP Server 入口
├── mcp-client.ts            # MCP Client
├── bridge/                  # 桥接相关
└── workflows/               # 工作流相关
```

## 🔧 配置

### 端口配置

UTO 从 `.port` 文件读取 Unity MCP 端口，HTTP Server 端口自动计算：

```
Unity MCP 端口: 从 .port 文件读取（默认 3212）
UTO HTTP 端口: Unity 端口 + 1（例如 3213）
```

修改端口：
```bash
echo 4000 > .port
# Unity MCP: 4000
# UTO HTTP: 4001
```

## 🎯 核心特性

### 1. 自动心跳检测
- 每 500ms 检测 Unity 健康状态
- 通过 InstanceId 识别域重建
- 自动等待新实例就绪（最多 5分钟）

### 2. HTTP API

#### 健康检查
```bash
GET http://localhost:3213/health
```

#### 调用单个工具
```bash
POST http://localhost:3213/call
Content-Type: application/json

{
  "tool": "StopPlayMode",
  "params": {}
}
```

#### 批量调用工具
```bash
POST http://localhost:3213/batch
Content-Type: application/json

{
  "tools": [
    { "name": "StopPlayMode", "arguments": {} },
    { "name": "TriggerCompile", "arguments": { "forceCompilation": false } },
    { "name": "GetCompileStatus", "arguments": {} }
  ]
}
```

### 3. 透明域重建处理

UTO 自动检测并处理 Unity 域重建：
- 检测 "Unknown error"（域重建标志）
- 自动等待新实例就绪
- 无需手动配置 Wait 节点

## 📝 使用脚本

项目提供了 2 个 PowerShell 脚本（位于 `../Config/` 目录）：

### 1. invoke-uto-tool.ps1
调用单个 Unity MCP 工具

```powershell
# 示例：停止 PlayMode
.\invoke-uto-tool.ps1 -Tool "StopPlayMode"

# 示例：触发编译
.\invoke-uto-tool.ps1 -Tool "TriggerCompile" -ParamsBase64 "eyJmb3JjZUNvbXBpbGF0aW9uIjp0cnVlfQ=="
```

### 2. compile-unity-flow.ps1
执行完整的编译流程

```powershell
# 普通编译
.\compile-unity-flow.ps1

# 强制编译
.\compile-unity-flow.ps1 -Force 1
```

## 📚 文档

详细文档请查看 `Docs/` 目录：
- `IMPLEMENTATION_REPORT.md` - 心跳检测优化实施报告
- `README_SCRIPTS.md` - 脚本使用说明

## 🔍 日志示例

```
[HTTP] 正在启动 UTO HTTP Server...
[HTTP] Unity MCP 端口: 3212
[HTTP] UTO HTTP 端口: 3213
[HTTP] MCP Client 已启动
[Heartbeat] 心跳检测已启动
[Heartbeat] Unity 已连接 (InstanceId: 12345678)
[HTTP] ✅ UTO HTTP Server 运行在 http://localhost:3213
```

## ⚙️ 开发

### 监听模式
```bash
npm run dev
```

### 构建
```bash
npm run build
```

## 📦 依赖

- `@modelcontextprotocol/sdk` - MCP SDK
- `express` - HTTP Server
- `axios` - HTTP Client
- `cors` - CORS 支持
- `zod` - 数据验证

## 📄 许可证

MIT
