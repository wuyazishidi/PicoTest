# EnterPlayMode + StopPlayMode 连续域重建卡死问题 - 修复完成报告

**完成时间**：2026-01-19  
**状态**：✅ 已完成，待测试

---

## 🐛 问题描述

### 测试场景
```powershell
$tools = @(
    @{ name = "EnterPlayMode"; arguments = @{} },   # 第1次域重建
    @{ name = "StopPlayMode"; arguments = @{} },    # 第2次域重建
    @{ name = "TriggerCompile"; arguments = @{ forceCompilation = $Force } },
    @{ name = "GetCompileStatus"; arguments = @{} }
)
```

### 问题现象
- 运行 `test-unity-flow.ps1` 时**卡死**
- 无法完成编译流程

### 根本原因

**旧逻辑的问题**：
```typescript
if (currentId !== lastInstanceId) {
    lastInstanceId = currentId;  // ❌ 立即更新
    await sleep(2000);
    
    const verifyId = await get('/health');
    if (verifyId === currentId) {
        return true;  // ✅ 验证成功
    }
    // ⚠️ 验证失败后继续循环，但 lastInstanceId 已经变了
}
```

**问题**：
1. **过早更新 lastInstanceId**：检测到新实例后立即更新，验证失败时无法重试
2. **单次验证不够**：只验证 1 次，无法确保 InstanceId 真正稳定
3. **连续域重建**：EnterPlayMode (A→B) 还没稳定，StopPlayMode (B→C) 又触发了
4. **InstanceId 频繁变化**：A → B → C，逻辑无法跟上变化

---

## ✅ 修复方案

### 关键改进

#### 改进 1：延迟更新 lastInstanceId
```typescript
// 旧逻辑
if (currentId !== lastInstanceId) {
    lastInstanceId = currentId;  // ❌ 立即更新
    // 验证...
}

// 新逻辑
if (currentId !== lastInstanceId) {
    // 不要立即更新 lastInstanceId，先验证稳定性
    // 验证...
    if (stable) {
        lastInstanceId = currentId;  // ✅ 验证成功后才更新
        return true;
    }
    continue;  // 验证失败，继续循环
}
```

#### 改进 2：多次验证确保稳定
```typescript
// 旧逻辑：验证 1 次
const verifyId = await get('/health');
if (verifyId === currentId) {
    return true;
}

// 新逻辑：验证 3 次（每次间隔 500ms）
let stable = true;
for (let i = 0; i < 3; i++) {
    const verifyId = await get('/health');
    if (verifyId !== currentId) {
        console.log(`InstanceId 仍在变化，继续等待... (当前: ${verifyId})`);
        stable = false;
        break;
    }
    if (i < 2) await sleep(500);
}

if (stable) {
    return true;
}
```

#### 改进 3：验证失败后继续等待
```typescript
// 旧逻辑：验证失败后逻辑不清晰
if (verifyId === currentId) {
    return true;
}
// 继续循环，但 lastInstanceId 已经变了

// 新逻辑：验证失败后明确 continue
if (stable) {
    lastInstanceId = currentId;
    return true;
}
continue;  // 不更新 lastInstanceId，继续等待
```

---

## 🔧 完整的修复逻辑

```typescript
async waitForUnityReady(timeout: number = UTO_CONFIG.heartbeat.timeout): Promise<boolean> {
    const startTime = Date.now();
    console.log('[Heartbeat] 等待 Unity 就绪...');

    while (Date.now() - startTime < timeout) {
        try {
            const response = await axios.get(`${this.unityUrl}/health`, ...);
            const currentId = response.data?.serverId;

            if (currentId) {
                // 检测到新实例（域重建后）
                if (this.lastInstanceId && currentId !== this.lastInstanceId) {
                    console.log(`[Heartbeat] 检测到新实例 (旧: ${this.lastInstanceId}, 新: ${currentId})`);
                    
                    // 不要立即更新 lastInstanceId，先验证稳定性
                    
                    // 等待稳定 2 秒
                    console.log(`[Heartbeat] 等待 Unity MCP 完全初始化 (2000ms)...`);
                    await new Promise(r => setTimeout(r, 2000));
                    
                    // 多次验证确保稳定（3 次验证，每次间隔 500ms）
                    let stable = true;
                    for (let i = 0; i < 3; i++) {
                        const verifyResponse = await axios.get(`${this.unityUrl}/health`, ...);
                        
                        if (verifyResponse.data?.serverId !== currentId) {
                            console.log(`[Heartbeat] InstanceId 仍在变化，继续等待... (当前: ${verifyResponse.data?.serverId})`);
                            stable = false;
                            break;
                        }
                        
                        if (i < 2) await new Promise(r => setTimeout(r, 500));
                    }
                    
                    if (stable) {
                        // 验证成功，更新 lastInstanceId
                        this.lastInstanceId = currentId;
                        this.isUnityReady = true;
                        console.log(`[Heartbeat] Unity MCP 已完全就绪 (InstanceId: ${currentId})`);
                        console.log(`[Heartbeat] Unity 已就绪 (耗时: ${Date.now() - startTime}ms)`);
                        return true;
                    }
                    
                    // 验证失败，继续循环（不更新 lastInstanceId）
                    continue;
                }
                
                // 同一实例或首次连接
                if (this.lastInstanceId === currentId || this.lastInstanceId === null) {
                    if (this.lastInstanceId === null) {
                        this.lastInstanceId = currentId;
                    }
                    this.isUnityReady = true;
                    console.log(`[Heartbeat] Unity 已就绪 (耗时: ${Date.now() - startTime}ms)`);
                    return true;
                }
            }
        } catch (error) {
            // 连接失败，继续等待
        }

        await new Promise(r => setTimeout(r, 100));
    }

    console.error('[Heartbeat] 等待超时！Unity 可能已崩溃');
    return false;
}
```

---

## 📊 修改的文件

### `UTO/src/heartbeat-manager.ts`

**修改内容**：
1. ✅ 延迟更新 `lastInstanceId`（验证成功后才更新）
2. ✅ 多次验证确保稳定（3 次验证，每次间隔 500ms）
3. ✅ 验证失败后 `continue`，不更新 `lastInstanceId`
4. ✅ 添加详细的日志输出（"InstanceId 仍在变化"）

---

## 🎯 预期效果

### 日志输出（EnterPlayMode + StopPlayMode）

```
[HTTP] [1/4] 执行: EnterPlayMode
[Heartbeat] 等待 Unity 就绪...
[Heartbeat] 检测到新实例 (旧: A, 新: B)
[Heartbeat] 等待 Unity MCP 完全初始化 (2000ms)...
[Heartbeat] InstanceId 仍在变化，继续等待... (当前: C)
[Heartbeat] 检测到新实例 (旧: A, 新: C)
[Heartbeat] 等待 Unity MCP 完全初始化 (2000ms)...
[Heartbeat] Unity MCP 已完全就绪 (InstanceId: C)
[Heartbeat] Unity 已就绪 (耗时: 5500ms)
[HTTP] [1/4] 成功: EnterPlayMode

[HTTP] [2/4] 执行: StopPlayMode
[Heartbeat] 等待 Unity 就绪...
[Heartbeat] 检测到新实例 (旧: C, 新: D)
[Heartbeat] 等待 Unity MCP 完全初始化 (2000ms)...
[Heartbeat] Unity MCP 已完全就绪 (InstanceId: D)
[Heartbeat] Unity 已就绪 (耗时: 3200ms)
[HTTP] [2/4] 成功: StopPlayMode

[HTTP] [3/4] 执行: TriggerCompile
[HTTP] [3/4] 成功: TriggerCompile

[HTTP] [4/4] 执行: GetCompileStatus
[HTTP] [4/4] 成功: GetCompileStatus

编译流程完成!
总耗时: ~15 秒
```

---

## 🧪 测试步骤

### 1. 重启 UTO
```bash
cd D:\Unity\Project\IGG\ie\trunk\Client\UnityProject\Packages\cn.etetet.yiuimcp\UTO
npm run start:http
```

### 2. 启动 Unity Editor
- 确保 Unity 正常运行

### 3. 运行测试脚本
```powershell
cd D:\Unity\Project\IGG\ie\trunk\Client\UnityProject\Packages\cn.etetet.yiuimcp\Config
.\test-unity-flow.ps1
```

### 4. 观察日志
- ✅ 看到 "检测到新实例"（可能多次）
- ✅ 看到 "InstanceId 仍在变化，继续等待..."（如果有连续域重建）
- ✅ 看到 "Unity MCP 已完全就绪"
- ✅ 所有 4 个工具都成功执行
- ✅ 不再卡死

---

## 📝 验证时间说明

### 单次域重建
- 等待稳定：2 秒
- 验证 3 次：0 + 0.5 + 0.5 = 1 秒
- **总计**：约 3 秒

### 连续域重建（EnterPlayMode + StopPlayMode）
- 第一次检测到变化，验证失败
- 第二次检测到新实例，验证成功
- **总计**：约 5-6 秒

---

## ✅ 验证清单

- [x] 延迟更新 `lastInstanceId`
- [x] 多次验证确保稳定（3 次）
- [x] 验证失败后 `continue`
- [x] 添加详细日志
- [x] TypeScript 编译成功
- [ ] 实际测试 `test-unity-flow.ps1`（待用户测试）

---

## 🎉 总结

**核心改进**：
1. ✅ 延迟更新 `lastInstanceId`，验证成功后才更新
2. ✅ 多次验证（3 次），确保 InstanceId 真正稳定
3. ✅ 验证失败后继续等待，不会混乱
4. ✅ 可以正确处理连续域重建（EnterPlayMode + StopPlayMode）

**预期结果**：
- 不再卡死
- 可以正确处理连续域重建
- 所有工具都能成功执行
- 总耗时合理（约 15 秒）

请重启 UTO 并运行 `test-unity-flow.ps1` 测试！🚀
