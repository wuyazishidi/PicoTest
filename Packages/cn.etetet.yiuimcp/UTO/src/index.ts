import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import {
  CallToolRequestSchema,
  ListToolsRequestSchema,
} from "@modelcontextprotocol/sdk/types.js";
import axios from "axios";
import * as fs from "fs";
import * as path from "path";
import * as http from "http";

// 端口默认 3212，为支持动态端口，从 UTO 目录下的 .port 读取
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
    return 3212;
}

function getUnityUrl(): string {
    const port = getUnityPort();
    return `http://127.0.0.1:${port}`;
}

// 辅助函数：获取 InstanceId
async function getInstanceId(): Promise<string | null> {
    try {
        const agent = new http.Agent({ keepAlive: false, maxSockets: 1 });
        const url = getUnityUrl();
        const response = await axios.get(`${url}/health`, { 
            timeout: 3000,
            httpAgent: agent
        });
        agent.destroy();
        
        return response.data?.serverId || null;
    } catch (e) {
        return null;
    }
}

// 健康检查
async function healthCheck(): Promise<boolean> {
    // 为每次请求创建新的 Agent
    const agent = new http.Agent({
        keepAlive: false,
        maxSockets: 1
    });

    try {
        const url = getUnityUrl();
        const response = await axios.get(`${url}/health`, { 
            timeout: 3000,
            httpAgent: agent
        });
        
        // 立即销毁 Agent
        agent.destroy();
        
        return response.status === 200;
    } catch (e) {
        // 确保 Agent 被销毁
        agent.destroy();
        return false;
    }
}

// Unity RPC 调用
async function callUnityRpc(method: string, params: any = {}): Promise<any> {
    const url = getUnityUrl();
    const port = getUnityPort();

    // 为每次请求创建新的 Agent
    const agent = new http.Agent({
        keepAlive: false,
        maxSockets: 1
    });

    try {
        const response = await axios.post(`${url}/rpc`, {
            jsonrpc: "2.0",
            method: method,
            params: params,
            id: Date.now(),
        }, { 
            timeout: 30000,
            httpAgent: agent,
            headers: {
                'Connection': 'close'  // 强制关闭连接
            }
        });

        // 立即销毁 Agent
        agent.destroy();

        if (response.data.error) {
            throw new Error(response.data.error.message);
        }

        return response.data.result;
    } catch (error: any) {
        // 确保 Agent 被销毁
        agent.destroy();
        
        if (error.code === 'ECONNREFUSED') {
            throw new Error(`Unity 连接被拒绝 (端口: ${port}, Unity 是否运行?)`);
        }
        if (error.code === 'ETIMEDOUT' || error.code === 'ECONNABORTED') {
            throw new Error(`Unity 响应超时 (端口: ${port})`);
        }
        throw error;
    }
}

// MCP Server
const server = new Server(
  {
    name: "uto",
    version: "1.0.0",
  },
  {
    capabilities: {
      tools: {},
    },
  }
);

// ListTools - 尝试从 Unity MCP 获取，否则返回空列表
server.setRequestHandler(ListToolsRequestSchema, async () => {
    try {
        const result = await callUnityRpc("ListTools", {});
        if (result && result.tools) {
            return { tools: result.tools };
        }
    } catch (error) {
        // Unity MCP 不支持 ListTools，返回空列表
    }
    
    return { tools: [] };
});

// CallTool - 直接转发到 Unity MCP（心跳检测在 http-server 层处理）
server.setRequestHandler(CallToolRequestSchema, async (request) => {
    const { name, arguments: args } = request.params;
    
    // 直接转发到 Unity MCP
    try {
        const result = await callUnityRpc(name, args || {});
        
        if (result && result.success) {
            return {
                content: [{ type: "text", text: result.message || "Success" }]
            };
        } else {
            return {
                content: [{ type: "text", text: result?.message || "Unknown error" }],
                isError: true
            };
        }
    } catch (error: any) {
        return {
            content: [{ type: "text", text: `RPC Failed: ${error.message}` }],
            isError: true
        };
    }
});

// Start server
async function main() {
    const transport = new StdioServerTransport();
    await server.connect(transport);
    console.error("UTO Server running on stdio (pure proxy mode)");
}

// 根据启动参数决定运行模式
if (require.main === module) {
    const args = process.argv.slice(2);
    
    const isStdioMode = process.env.MCP_STDIO_MODE === '1';
    
    if (args.includes('--http') && !isStdioMode) {
        // HTTP 模式（无需传递端口，自动从 .port 文件读取）
        import('./http-server.js').then(({ startHttpServer }) => {
            startHttpServer().catch((error: any) => {
                console.error("HTTP Server 启动失败:", error);
                process.exit(1);
            });
        });
    } else {
        // Stdio 模式
        main().catch((error) => {
            console.error("Fatal error:", error);
            process.exit(1);
        });
    }
}
