# 域重载后连接失败问题修复 - 完成报告

**完成时间**：2026-01-19  
**状态**：✅ 已修复，待测试

---

## 🐛 问题描述

### 现象
- **不触发域重载时**：编译流程正常（3.87 秒，所有步骤成功）
- **触发域重载时**：
  - StopPlayMode 成功（显示 "工具 StopPlayMode 已执行，Unity 已完成域重建"）
  - TriggerCompile 失败（"RPC Failed: Unity 连接被拒绝 (端口: 3212, Unity 是否运行?)"）
  - GetCompileStatus 失败（同样的连接被拒绝错误）
  - 总耗时仅 0.51 秒（异常快）

### 根本原因

心跳检测逻辑过于乐观：

```typescript
// 旧逻辑（有问题）
} else if (currentId !== this.lastInstanceId) {
    console.log(`[Heartbeat] 检测到新实例 (旧: ${this.lastInstanceId}, 新: ${currentId})`);
    this.lastInstanceId = currentId;
    this.isUnityReady = true;  // ❌ 立即标记为就绪
}
```

**问题**：
1. 检测到新的 InstanceId 后，立即标记 `isUnityReady = true`
2. 虽然 `/health` 端点能返回新的 InstanceId，但 Unity MCP 的 RPC 服务可能还没有完全初始化
3. 后续的工具调用（TriggerCompile、GetCompileStatus）会失败

---

## ✅ 修复方案

### 1. 添加稳定等待时间

在 `config.ts` 中添加新配置项：

```typescript
heartbeat: {
    // ...
    /** 检测到新实例后的稳定等待时间（毫秒）
     * 确保 Unity MCP 的 RPC 服务完全初始化
     */
    stabilizationDelay: 2000  // 2 秒
}
```

### 2. 改进心跳检测逻辑

```typescript
// 新逻辑（已修复）
} else if (currentId !== this.lastInstanceId) {
    // 检测到新实例（域重建完成）
    console.log(`[Heartbeat] 检测到新实例 (旧: ${this.lastInstanceId}, 新: ${currentId})`);
    this.lastInstanceId = currentId;
    
    // 标记为未就绪，等待稳定
    this.isUnityReady = false;
    console.log(`[Heartbeat] 等待 Unity MCP 完全初始化 (${UTO_CONFIG.heartbeat.stabilizationDelay}ms)...`);
    
    // 等待稳定时间
    await new Promise(r => setTimeout(r, UTO_CONFIG.heartbeat.stabilizationDelay));
    
    // 再次验证连接
    try {
        const verifyResponse = await axios.get(`${this.unityUrl}/health`, { 
            timeout: UTO_CONFIG.heartbeat.healthCheckTimeout,
            httpAgent: new (require('http').Agent)({ keepAlive: false })
        });
        
        if (verifyResponse.data?.serverId === currentId) {
            this.isUnityReady = true;
            console.log(`[Heartbeat] Unity MCP 已完全就绪 (InstanceId: ${currentId})`);
        }
    } catch (e) {
        console.log(`[Heartbeat] 验证失败，继续等待...`);
    }
}
```

---

## 🔧 修复流程

### 步骤 1：检测到新实例
```
[Heartbeat] 检测到新实例 (旧: abc123, 新: def456)
```

### 步骤 2：标记为未就绪
```typescript
this.isUnityReady = false;
```

### 步骤 3：等待稳定时间（2 秒）
```
[Heartbeat] 等待 Unity MCP 完全初始化 (2000ms)...
```

### 步骤 4：验证连接
```typescript
// 再次调用 /health 端点
// 确认 InstanceId 仍然是新的
```

### 步骤 5：标记为就绪
```
[Heartbeat] Unity MCP 已完全就绪 (InstanceId: def456)
```

---

## 📊 修改的文件

### 1. `UTO/src/config.ts`
- ✅ 添加 `stabilizationDelay: 2000` 配置项
- ✅ 更新 `unityDefault` 注释（说明是保底值）

### 2. `UTO/src/heartbeat-manager.ts`
- ✅ 检测到新实例后，先标记为未就绪
- ✅ 等待 `stabilizationDelay` 时间（2 秒）
- ✅ 再次验证连接
- ✅ 验证成功后才标记为就绪

---

## 🎯 预期效果

### 修复前
```
总耗时: 0.51 秒

✓ StopPlayMode
  工具 StopPlayMode 已执行，Unity 已完成域重建
✓ TriggerCompile
  RPC Failed: Unity 连接被拒绝  ❌
✓ GetCompileStatus
  RPC Failed: Unity 连接被拒绝  ❌
```

### 修复后（预期）
```
总耗时: ~5 秒

✓ StopPlayMode
  工具 StopPlayMode 已执行，Unity 已完成域重建
  [Heartbeat] 检测到新实例 (旧: xxx, 新: yyy)
  [Heartbeat] 等待 Unity MCP 完全初始化 (2000ms)...
  [Heartbeat] Unity MCP 已完全就绪 (InstanceId: yyy)
✓ TriggerCompile
  TriggerCompile, TRIGGERED  ✅
✓ GetCompileStatus
  GetCompileStatus, Status: IDLE  ✅
```

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
- 检查是否有 "等待 Unity MCP 完全初始化" 日志
- 检查是否有 "Unity MCP 已完全就绪" 日志
- 验证 TriggerCompile 和 GetCompileStatus 是否成功

---

## 📝 配置调整

如果 2 秒不够，可以调整稳定等待时间：

```typescript
// config.ts
heartbeat: {
    stabilizationDelay: 3000  // 改为 3 秒
}
```

---

## ✅ 验证清单

- [x] 添加 `stabilizationDelay` 配置项
- [x] 修改心跳检测逻辑
- [x] 检测到新实例后等待稳定时间
- [x] 再次验证连接
- [x] TypeScript 编译成功
- [ ] 实际测试域重载场景（待用户测试）

---

## 🎉 总结

**问题**：心跳检测过于乐观，检测到新 InstanceId 就认为 Unity 完全就绪

**修复**：
1. 检测到新实例后，先标记为未就绪
2. 等待 2 秒稳定时间
3. 再次验证连接
4. 验证成功后才标记为就绪

**预期**：域重载后的工具调用不再失败，编译流程正常完成

请测试并反馈结果！🚀
