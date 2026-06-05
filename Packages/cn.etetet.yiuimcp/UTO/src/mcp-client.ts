import { spawn, ChildProcess } from 'child_process';
import * as path from 'path';

/**
 * MCP Stdio 客户端
 * 用于启动 MCP Server 子进程并通过 Stdio 通信
 */
class MCPClient {
    private process: ChildProcess | null = null;
    private requestId = 0;
    private pendingRequests = new Map<number, { resolve: Function; reject: Function; timeout: NodeJS.Timeout }>();
    private buffer = '';
    private initialized = false;

    /**
     * 启动 MCP Server 子进程
     */
    async start() {
        if (this.process) {
            console.log('[MCP Client] 已经启动，跳过');
            return;
        }

        console.log('[MCP Client] 正在启动 MCP Server 子进程...');

        // 启动当前 index.js 作为子进程（Stdio 模式）
        const indexPath = path.join(__dirname, 'index.js');
        
        this.process = spawn('node', [indexPath], {
            cwd: __dirname,
            stdio: ['pipe', 'pipe', 'inherit'], // stdin/stdout 用于 MCP，stderr 显示日志
            env: { ...process.env, MCP_STDIO_MODE: '1' } // 标记为 Stdio 模式
        });

        // 监听响应
        this.process.stdout!.on('data', (data) => {
            this.handleResponse(data);
        });

        // 错误处理
        this.process.on('error', (err) => {
            console.error('[MCP Client] 子进程错误:', err);
        });

        this.process.on('exit', (code) => {
            console.log(`[MCP Client] 子进程退出，代码: ${code}`);
            this.process = null;
            this.initialized = false;
        });

        // 初始化握手
        await this.initialize();
        console.log('[MCP Client] MCP Server 子进程已启动并初始化完成');
    }

    /**
     * 调用 MCP 工具
     */
    async callTool(name: string, params: any): Promise<any> {
        if (!this.initialized) {
            throw new Error('MCP Client 未初始化');
        }

        const id = ++this.requestId;

        const request = {
            jsonrpc: "2.0",
            id,
            method: "tools/call",
            params: { name, arguments: params }
        };

        return new Promise((resolve, reject) => {
            // 超时保护（10 分钟）
            const timeout = setTimeout(() => {
                if (this.pendingRequests.has(id)) {
                    this.pendingRequests.delete(id);
                    reject(new Error(`请求超时: ${name}`));
                }
            }, 600000);

            this.pendingRequests.set(id, { resolve, reject, timeout });

            // 发送请求
            const jsonStr = JSON.stringify(request) + '\n';
            this.process!.stdin!.write(jsonStr);
        });
    }

    /**
     * 处理响应数据
     */
    private handleResponse(data: Buffer) {
        this.buffer += data.toString();
        const lines = this.buffer.split('\n');
        this.buffer = lines.pop() || ''; // 保留不完整的行

        for (const line of lines) {
            if (!line.trim()) continue;

            try {
                const response = JSON.parse(line);
                const pending = this.pendingRequests.get(response.id);

                if (pending) {
                    clearTimeout(pending.timeout);
                    this.pendingRequests.delete(response.id);

                    if (response.error) {
                        pending.reject(new Error(response.error.message || JSON.stringify(response.error)));
                    } else {
                        pending.resolve(response.result);
                    }
                }
            } catch (e) {
                // 忽略解析错误（可能是不完整的 JSON 或日志输出）
            }
        }
    }

    /**
     * 初始化握手
     */
    private async initialize() {
        const id = ++this.requestId;

        const request = {
            jsonrpc: "2.0",
            id,
            method: "initialize",
            params: {
                protocolVersion: "2024-11-05",
                capabilities: {},
                clientInfo: { name: "uto-http-server", version: "1.0.0" }
            }
        };

        return new Promise((resolve, reject) => {
            const timeout = setTimeout(() => {
                if (this.pendingRequests.has(id)) {
                    this.pendingRequests.delete(id);
                    reject(new Error('初始化超时'));
                }
            }, 10000);

            this.pendingRequests.set(id, { 
                resolve: (result: any) => {
                    this.initialized = true;
                    resolve(result);
                }, 
                reject, 
                timeout 
            });

            // 发送初始化请求
            const jsonStr = JSON.stringify(request) + '\n';
            this.process!.stdin!.write(jsonStr);
        });
    }

    /**
     * 停止 MCP Client
     */
    stop() {
        if (this.process) {
            this.process.kill();
            this.process = null;
            this.initialized = false;
            console.log('[MCP Client] 已停止');
        }
    }
}

// 导出单例
export const mcpClient = new MCPClient();
