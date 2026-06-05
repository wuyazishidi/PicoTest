# 域重载后连接失败问题 - 深度修复完成报告

**完成时间**：2026-01-19  
**状态**：✅ 已完成，待测试

---

## 🐛 问题根源

### 旧逻辑的问题
```typescript
// waitForUnityReady() - 被动等待
while (!this.isUnityReady) {
    await new Promise(r => setTimeout(r, 100));
}
```

**问题**：
1. 只是被动等待 `isUnityReady` 标志
2. 依赖 `setInterval` (500ms) 中的异步检测
3. `setInterval` 中的 `await` 可能被打断或延迟
4. 导致 `waitForUnityReady()` 在心跳检测完成之前就返回

---

## ✅ 修复方案

### 1. 重构 `waitForUnityReady()` - 主动检测

```typescript
async waitForUnityReady(timeout: number = UTO_CONFIG.heartbeat.timeout): Promise<boolean> {
    while (Date.now() - startTime < timeout) {
        // 主动检测一次
        const response = await axios.get(`${this.unityUrl}/health`, ...);
        const currentId = response.data?.serverId;

        if (currentId) {
            // 检测到新实例（域重建后）
            if (this.lastInstanceId && currentId !== this.lastInstanceId) {
                console.log(`[Heartbeat] 检测到新实例 (旧: ${this.lastInstanceId}, 新: ${currentId})`);
                
                // 等待稳定 2 秒
                console.log(`[Heartbeat] 等待 Unity MCP 完全初始化 (2000ms)...`);
                await new Promise(r => setTimeout(r, 2000));
                
                // 再次验证
                const verifyResponse = await axios.get(`${this.unityUrl}/health`, ...);
                if (verifyResponse.data?.serverId === currentId) {
                    console.log(`[Heartbeat] Unity MCP 已完全就绪 (InstanceId: ${currentId})`);
                    return true;
                }
            }
            
            // 同一实例或首次连接
            if (this.lastInstanceId === currentId || this.lastInstanceId === null) {
                return true;
            }
        }
        
        await new Promise(r => setTimeout(r, 100));
    }
}
```

### 2. 简化 `startHeartbeat()` - 只做健康检查

```typescript
startHeartbeat() {
    this.heartbeatInterval = setInterval(async () => {
        const response = await axios.get(`${this.unityUrl}/health`, ...);
        const currentId = response.data?.serverId;
        
        if (currentId) {
            if (this.lastInstanceId === null) {
                // 首次连接
                this.isUnityReady = true;
            } else if (currentId !== this.lastInstanceId) {
                // 检测到新实例，标记为未就绪
                // 让 waitForUnityReady() 主动处理
                this.isUnityReady = false;
                this.lastInstanceId = currentId;
            } else {
                // 同一实例，保持就绪
                this.isUnityReady = true;
            }
        }
    }, 500);
}
```

---

## 🔧 关键改进

### 改进 1：主动检测
- **旧**：被动等待 `isUnityReady` 标志
- **新**：主动调用 `/health` 端点检测

### 改进 2：不依赖 setInterval
- **旧**：依赖 `setInterval` 的时机
- **新**：需要时立即检测，不会被打断

### 改进 3：完整的检测流程
- **旧**：检测逻辑分散在 `setInterval` 中
- **新**：`waitForUnityReady()` 负责完整的检测和等待流程

### 改进 4：避免竞态条件
- **旧**：多个异步检测可能同时运行
- **新**：逻辑清晰，不会有竞态问题

---

## 📊 修改的文件

### `UTO/src/heartbeat-manager.ts`

#### 修改 1：`waitForUnityReady()` 方法
- ✅ 从被动等待改为主动检测
- ✅ 检测到新实例后等待 2 秒稳定时间
- ✅ 再次验证连接
- ✅ 验证成功后返回 true

#### 修改 2：`startHeartbeat()` 方法
- ✅ 移除复杂的异步等待逻辑
- ✅ 只做简单的健康检查
- ✅ 检测到新实例时标记为未就绪
- ✅ 让 `waitForUnityReady()` 主动处理

---

## 🎯 预期效果

### 日志输出（域重载场景）

```
[HTTP] 调用工具: StopPlayMode
[HTTP] 检测到 Unknown error，等待 Unity 重连...
[Heartbeat] 等待 Unity 就绪...
[Heartbeat] 检测到新实例 (旧: abc123, 新: def456)
[Heartbeat] 等待 Unity MCP 完全初始化 (2000ms)...
[Heartbeat] Unity MCP 已完全就绪 (InstanceId: def456)
[Heartbeat] Unity 已就绪 (耗时: 2150ms)
[HTTP] 成功: StopPlayMode，耗时: 2200ms

[HTTP] [1/3] 执行: StopPlayMode
[HTTP] [1/3] 成功: StopPlayMode

[HTTP] [2/3] 执行: TriggerCompile
[HTTP] [2/3] 成功: TriggerCompile

[HTTP] [3/3] 执行: GetCompileStatus
[HTTP] [3/3] 成功: GetCompileStatus

编译流程完成!
总耗时: ~5 秒
```

### 对比

| 场景 | 修复前 | 修复后 |
|------|--------|--------|
| StopPlayMode | 成功 | 成功 |
| TriggerCompile | ❌ 失败（连接被拒绝） | ✅ 成功 |
| GetCompileStatus | ❌ 失败（连接被拒绝） | ✅ 成功 |
| 总耗时 | 0.51 秒（异常快） | ~5 秒（正常） |

---

## 🧪 测试步骤

### 1. 重启 UTO
```bash
cd D:\Unity\Project\IGG\ie\trunk\Client\UnityProject\Packages\cn.etetet.yiuimcp\UTO
npm run start:http
```

### 2. 启动 Unity Editor
- 进入 PlayMode

### 3. 运行编译流程
```powershell
cd D:\Unity\Project\IGG\ie\trunk\Client\UnityProject\Packages\cn.etetet.yiuimcp\Config
.\compile-unity-flow.ps1
```

### 4. 观察日志
- ✅ 看到 "检测到新实例"
- ✅ 看到 "等待 Unity MCP 完全初始化 (2000ms)..."
- ✅ 看到 "Unity MCP 已完全就绪"
- ✅ TriggerCompile 和 GetCompileStatus 成功

---

## 📝 配置说明

如果 2 秒稳定时间不够，可以调整：

```typescript
// config.ts
heartbeat: {
    stabilizationDelay: 3000  // 改为 3 秒
}
```

---

## ✅ 验证清单

- [x] 重构 `waitForUnityReady()` 为主动检测
- [x] 简化 `startHeartbeat()` 只做健康检查
- [x] 检测到新实例后等待稳定时间
- [x] 再次验证连接
- [x] TypeScript 编译成功
- [ ] 实际测试域重载场景（待用户测试）

---

## 🎉 总结

**核心改进**：
1. ✅ `waitForUnityReady()` 主动检测，不依赖 `setInterval`
2. ✅ 检测到新实例后等待 2 秒稳定时间
3. ✅ 再次验证连接确保 RPC 服务可用
4. ✅ 避免竞态条件，逻辑清晰可靠

**预期结果**：
- 域重载后的工具调用不再失败
- 编译流程正常完成
- 总耗时合理（~5 秒）

请重启 UTO 并测试域重载场景！🚀
