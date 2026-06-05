# UTO HTTP 使用指南

本文档说明如何直接通过 HTTP 调用当前仓库里的 UTO。

## 前置条件

1. Unity Editor 已打开
2. Unity 侧 MCP 服务已自动启动或可以手动启动
3. 已在 `Packages/cn.etetet.yiuimcp/UTO` 下执行过：

```bash
npm install
npm run build
```

## 启动 UTO HTTP

```bash
cd Packages/cn.etetet.yiuimcp/UTO
npm run start:http
```

默认情况下：

- UnityMCP 端口：`3212`
- UTO HTTP 端口：`3213`

如果 `UTO/.port` 存在，则优先按该文件中的 Unity 端口计算。

## 调用单个工具

### 写日志

```bash
curl -X POST http://localhost:3213/call \
  -H "Content-Type: application/json" \
  -d "{\"tool\":\"Log\",\"params\":{\"message\":\"Hello from AI\"}}"
```

### 执行菜单

```bash
curl -X POST http://localhost:3213/call \
  -H "Content-Type: application/json" \
  -d "{\"tool\":\"ExecuteMenu\",\"params\":{\"menuPath\":\"Assets/Refresh\"}}"
```

### 断言控制台包含关键词

```bash
curl -X POST http://localhost:3213/call \
  -H "Content-Type: application/json" \
  -d "{\"tool\":\"AssertConsoleContains\",\"params\":{\"keyword\":\"编译完毕\"}}"
```

## 批量调用

编译流程示例：

```bash
curl -X POST http://localhost:3213/batch \
  -H "Content-Type: application/json" \
  -d "{\"tools\":[{\"name\":\"StopPlayMode\",\"arguments\":{}},{\"name\":\"TriggerCompile\",\"arguments\":{\"Force\":false}},{\"name\":\"GetCompileResult\",\"arguments\":{}}]}"
```

## 常用工具与参数

- `Log`
  参数：`message`

- `LogError`
  参数：`message`

- `EnterPlayMode`
  参数：无

- `StopPlayMode`
  参数：无

- `TriggerCompile`
  参数：`Force`

- `GetCompileResult`
  参数：无

- `GetConsoleLog`
  参数：`logType`、`logMaxCount`、`removeStackTrace`

- `ExecuteMenu`
  参数：`menuPath`

- `AssertConsoleContains`
  参数：`keyword`、`keywordsJson`、`useRegex`、`ignoreCase`、`matchAll`、`tailCount`、`removeStackTrace`

## 返回格式

`/call` 返回：

```json
{
  "success": true,
  "result": "success",
  "isError": false,
  "duration": 120,
  "durationSeconds": "0.12"
}
```

`/batch` 返回：

```json
{
  "success": true,
  "results": [],
  "totalDuration": 1500,
  "totalDurationSeconds": "1.50"
}
```

## 故障排查

### UTO 启动失败

- 确认 `node --version` 可用
- 确认已执行 `npm install` 和 `npm run build`
- 直接在 `UTO/` 目录手动执行 `npm run start:http`

### Unity 连接失败

- 确认 Unity Editor 正在运行
- 查看 Unity Console 是否有 MCP 启动日志
- 检查 `UTO/.port` 是否与 Unity 端真实端口一致

### 编译后短暂断开

这是预期行为。UTO 会通过 `/health` 和 `serverId` 轮询等待 Unity 恢复。
