import express from 'express';
import cors from 'cors';
import { mcpClient } from './mcp-client';
import { HeartbeatManager } from './heartbeat-manager';
import { UTO_CONFIG } from './config';
import * as fs from 'fs';
import * as path from 'path';

const app = express();
app.use(cors());
app.use(express.json());

// 从 .port 文件读取 Unity 端口
function getUnityPort(): number {
    try {
        const candidates = [
            path.resolve(__dirname, "../.port"),
            path.resolve(__dirname, "../../.port"),
        ];

        for (const p of candidates) {
            if (fs.existsSync(p)) {
                const content = fs.readFileSync(p, "utf-8").trim();
                const port = parseInt(content, 10);
                if (!isNaN(port)) {
                    return port;
                }
            }
        }
    } catch (e) {
        // Ignore
    }
    return UTO_CONFIG.port.unityDefault;
}

// 全局心跳管理器（延迟初始化）
let heartbeatManager: HeartbeatManager | null = null;

/**
 * 健康检查端点
 */
app.get('/health', (req, res) => {
    res.json({ 
        status: 'ok', 
        service: UTO_CONFIG.http.serviceName,
        version: UTO_CONFIG.http.version,
        heartbeatReady: heartbeatManager?.isReady() || false,
        unityPort: getUnityPort()
    });
});

/**
 * 统一调用端点（带自动心跳检测）
 */
app.post('/call', async (req, res) => {
    const { tool, params } = req.body;

    if (!tool) {
        return res.status(400).json({
            success: false,
            error: '缺少必需字段: tool'
        });
    }

    const timestamp = new Date().toISOString().split('T')[1].split('.')[0];
    console.log(`[${timestamp}] [HTTP] 调用工具: ${tool}`);

    const startTime = Date.now();

    try {
        // 1. 调用前检查 Unity 是否就绪
        if (heartbeatManager && !heartbeatManager.isReady()) {
            console.log(`[${timestamp}] [HTTP] Unity 未就绪，等待恢复...`);
            const ready = await heartbeatManager.waitForUnityReady();
            if (!ready) {
                const timeoutMinutes = UTO_CONFIG.heartbeat.timeout / 60000;
                return res.json({
                    success: false,
                    error: `Unity 未就绪（超时 ${timeoutMinutes} 分钟）`,
                    duration: Date.now() - startTime
                });
            }
        }

        // 2. 记录调用前的 InstanceId
        const beforeInstanceId = heartbeatManager?.getCurrentInstanceId();

        // 3. 执行工具调用
        const result = await mcpClient.callTool(tool, params || {});

        // 4. 检查是否触发了域重建
        const afterInstanceId = heartbeatManager?.getCurrentInstanceId();
        
        if (beforeInstanceId && afterInstanceId && afterInstanceId !== beforeInstanceId) {
            console.log(`[${timestamp}] [HTTP] 检测到域重建 (${beforeInstanceId} -> ${afterInstanceId})`);
        }

        // 5. 提取结果
        let resultText = '';
        let isError = false;

        if (result.content && Array.isArray(result.content)) {
            resultText = result.content.map((c: any) => c.text).join('\n');
        } else {
            resultText = JSON.stringify(result);
        }

        if (result.isError !== undefined) {
            isError = result.isError;
        }

        // 6. 检测到 "Unknown error"（域重建导致）
        if (heartbeatManager && isError && resultText.includes("Unknown error")) {
            console.log(`[${timestamp}] [HTTP] 检测到 Unknown error，等待 Unity 重连...`);
            
            const ready = await heartbeatManager.waitForUnityReady();
            if (!ready) {
                const timeoutMinutes = UTO_CONFIG.heartbeat.timeout / 60000;
                return res.json({
                    success: false,
                    error: `Unity 域重建后未恢复（超时 ${timeoutMinutes} 分钟）`,
                    duration: Date.now() - startTime
                });
            }

            // 重连成功，返回成功（因为域重建是预期行为）
            resultText = `工具 ${tool} 已执行，Unity 已完成域重建`;
            isError = false;
        }

        const duration = Date.now() - startTime;
        console.log(`[${timestamp}] [HTTP] 成功: ${tool}，耗时: ${duration}ms`);

        res.json({
            success: true,
            result: resultText,
            isError: isError,
            duration: duration,
            durationSeconds: (duration / 1000).toFixed(2)
        });

    } catch (error: any) {
        const duration = Date.now() - startTime;
        console.error(`[${timestamp}] [HTTP] 失败: ${tool} - ${error.message}，耗时: ${duration}ms`);

        res.json({
            success: false,
            error: error.message,
            duration: duration,
            durationSeconds: (duration / 1000).toFixed(2)
        });
    }
});

/**
 * 批量调用端点（带自动心跳检测）
 */
app.post('/batch', async (req, res) => {
    const { tools } = req.body;

    if (!Array.isArray(tools)) {
        return res.status(400).json({
            success: false,
            error: '缺少必需字段: tools (array)'
        });
    }

    const timestamp = new Date().toISOString().split('T')[1].split('.')[0];
    console.log(`[${timestamp}] [HTTP] 批量调用: ${tools.length} 个工具`);

    const startTime = Date.now();
    const results = [];

    for (let i = 0; i < tools.length; i++) {
        const { name, arguments: args } = tools[i];
        const stepStart = Date.now();

        if (!name) {
            const failedDuration = Date.now() - startTime;
            return res.json({
                success: false,
                results: results,
                failedAt: i,
                error: `第 ${i + 1} 个工具缺少 name 字段`,
                totalDuration: failedDuration,
                totalDurationSeconds: (failedDuration / 1000).toFixed(2)
            });
        }

        console.log(`[${timestamp}] [HTTP] [${i + 1}/${tools.length}] 执行: ${name}`);

        try {
            // 自动心跳检测（每个工具调用前）
            if (heartbeatManager && !heartbeatManager.isReady()) {
                console.log(`[${timestamp}] [HTTP] Unity 未就绪，等待恢复...`);
                const ready = await heartbeatManager.waitForUnityReady();
                if (!ready) {
                    const failedDuration = Date.now() - startTime;
                    const timeoutMinutes = UTO_CONFIG.heartbeat.timeout / 60000;
                    return res.json({
                        success: false,
                        results: results,
                        failedAt: i,
                        error: `Unity 未就绪（超时 ${timeoutMinutes} 分钟）`,
                        totalDuration: failedDuration,
                        totalDurationSeconds: (failedDuration / 1000).toFixed(2)
                    });
                }
            }

            const result = await mcpClient.callTool(name, args || {});

            let resultText = '';
            let isError = false;

            if (result.content && Array.isArray(result.content)) {
                resultText = result.content.map((c: any) => c.text).join('\n');
            } else {
                resultText = JSON.stringify(result);
            }

            if (result.isError !== undefined) {
                isError = result.isError;
            }

            // 自动处理域重建
            if (heartbeatManager && isError && resultText.includes("Unknown error")) {
                console.log(`[${timestamp}] [HTTP] 检测到域重建，等待恢复...`);
                
                const ready = await heartbeatManager.waitForUnityReady();
                if (!ready) {
                    const failedDuration = Date.now() - startTime;
                    const timeoutMinutes = UTO_CONFIG.heartbeat.timeout / 60000;
                    return res.json({
                        success: false,
                        results: results,
                        failedAt: i,
                        error: `Unity 域重建后未恢复（超时 ${timeoutMinutes} 分钟）`,
                        totalDuration: failedDuration,
                        totalDurationSeconds: (failedDuration / 1000).toFixed(2)
                    });
                }

                // 重连成功
                resultText = `工具 ${name} 已执行，Unity 已完成域重建`;
                isError = false;
            }

            const stepDuration = Date.now() - stepStart;

            results.push({
                tool: name,
                success: true,
                result: resultText,
                isError: isError,
                duration: stepDuration,
                durationSeconds: (stepDuration / 1000).toFixed(2)
            });

            console.log(`[${timestamp}] [HTTP] [${i + 1}/${tools.length}] 成功: ${name}`);

        } catch (error: any) {
            console.error(`[${timestamp}] [HTTP] [${i + 1}/${tools.length}] 失败: ${name} - ${error.message}`);
            const stepDuration = Date.now() - stepStart;

            results.push({
                tool: name,
                success: false,
                error: error.message,
                duration: stepDuration,
                durationSeconds: (stepDuration / 1000).toFixed(2)
            });

            const failedDuration = Date.now() - startTime;
            return res.json({
                success: false,
                results: results,
                failedAt: i,
                error: `工具 ${name} 执行失败: ${error.message}`,
                totalDuration: failedDuration,
                totalDurationSeconds: (failedDuration / 1000).toFixed(2)
            });
        }
    }

    const endTime = Date.now();
    const totalDuration = endTime - startTime;

    console.log(`[${timestamp}] [HTTP] 批量调用完成: ${tools.length} 个工具全部成功，总耗时: ${totalDuration}ms`);
    res.json({
        success: true,
        results: results,
        totalDuration: totalDuration,
        totalDurationSeconds: (totalDuration / 1000).toFixed(2)
    });
});

/**
 * 获取可用工具列表
 */
app.get('/tools', async (req, res) => {
    try {
        res.json({
            success: true,
            tools: [
                { name: 'Compile', description: '智能编译 Unity 项目' },
                { name: 'Log', description: '写入日志到 Unity Console' },
                { name: 'LogError', description: '写入错误日志到 Unity Console' }
            ]
        });
    } catch (error: any) {
        res.json({
            success: false,
            error: error.message
        });
    }
});

/**
 * 启动 HTTP Server
 * 端口规则：UTO HTTP 端口 = Unity 端口 + 1
 */
export async function startHttpServer() {
    console.log('[HTTP] 正在启动 UTO HTTP Server...');
    
    // 从 .port 文件读取 Unity 端口
    const unityPort = getUnityPort();
    const httpPort = unityPort + UTO_CONFIG.port.httpOffset;
    
    console.log(`[HTTP] Unity MCP 端口: ${unityPort}`);
    console.log(`[HTTP] UTO HTTP 端口: ${httpPort}`);

    // 先启动 MCP Client
    await mcpClient.start();
    console.log('[HTTP] MCP Client 已启动');

    // 初始化并启动心跳检测
    const unityUrl = `http://127.0.0.1:${unityPort}`;
    heartbeatManager = new HeartbeatManager(unityUrl);
    heartbeatManager.startHeartbeat();
    console.log(`[HTTP] 心跳检测已启动 (监控 ${unityUrl})`);

    // 启动 HTTP Server
    return new Promise<void>((resolve) => {
        app.listen(httpPort, () => {
            console.log(`[HTTP] ✅ UTO HTTP Server 运行在 http://localhost:${httpPort}`);
            console.log(`[HTTP] 可用端点:`);
            console.log(`[HTTP]   - GET  /health  (健康检查)`);
            console.log(`[HTTP]   - POST /call    (调用单个工具)`);
            console.log(`[HTTP]   - POST /batch   (批量调用工具)`);
            console.log(`[HTTP]   - GET  /tools   (工具列表)`);
            resolve();
        });
    });
}
