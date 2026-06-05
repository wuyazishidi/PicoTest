import axios from 'axios';
import { UTO_CONFIG } from './config';

/**
 * 心跳管理器
 * 负责检测 Unity 的健康状态，识别域重建（Domain Reload）
 */
export class HeartbeatManager {
    private isUnityReady: boolean = false;
    private lastInstanceId: string | null = null;
    private unityUrl: string;
    private heartbeatInterval: NodeJS.Timeout | null = null;

    constructor(unityUrl: string) {
        this.unityUrl = unityUrl;
    }

    /**
     * 启动心跳检测（简化版，只做健康检查）
     */
    startHeartbeat() {
        if (this.heartbeatInterval) return;

        this.heartbeatInterval = setInterval(async () => {
            try {
                const response = await axios.get(`${this.unityUrl}/health`, { 
                    timeout: UTO_CONFIG.heartbeat.healthCheckTimeout,
                    httpAgent: new (require('http').Agent)({ keepAlive: false })
                });

                const currentId = response.data?.serverId;

                if (currentId) {
                    if (this.lastInstanceId === null) {
                        // 首次连接
                        this.lastInstanceId = currentId;
                        this.isUnityReady = true;
                        console.log(`[Heartbeat] Unity 已连接 (InstanceId: ${currentId})`);
                    } else if (currentId !== this.lastInstanceId) {
                        // 检测到新实例，标记为未就绪
                        // 让 waitForUnityReady() 主动处理
                        console.log(`[Heartbeat] 检测到实例变化 (旧: ${this.lastInstanceId}, 新: ${currentId})`);
                        this.isUnityReady = false;
                        this.lastInstanceId = currentId;
                    } else {
                        // 同一实例，保持就绪
                        this.isUnityReady = true;
                    }
                }
            } catch (error) {
                // 连接失败，标记为未就绪
                if (this.isUnityReady) {
                    console.log(`[Heartbeat] Unity 连接丢失`);
                }
                this.isUnityReady = false;
            }
        }, UTO_CONFIG.heartbeat.interval);

        console.log('[Heartbeat] 心跳检测已启动');
    }

    /**
     * 停止心跳检测
     */
    stopHeartbeat() {
        if (this.heartbeatInterval) {
            clearInterval(this.heartbeatInterval);
            this.heartbeatInterval = null;
            console.log('[Heartbeat] 心跳检测已停止');
        }
    }

    /**
     * 等待 Unity 就绪（主动检测）
     * @param timeout 超时时间（毫秒），默认使用配置文件中的值
     * @returns 是否成功就绪
     */
    async waitForUnityReady(timeout: number = UTO_CONFIG.heartbeat.timeout): Promise<boolean> {
        const startTime = Date.now();
        console.log('[Heartbeat] 等待 Unity 就绪...');

        while (Date.now() - startTime < timeout) {
            // 主动检测一次
            try {
                const response = await axios.get(`${this.unityUrl}/health`, { 
                    timeout: UTO_CONFIG.heartbeat.healthCheckTimeout,
                    httpAgent: new (require('http').Agent)({ keepAlive: false })
                });

                const currentId = response.data?.serverId;

                if (currentId) {
                    // 检测到新实例（域重建后）
                    if (this.lastInstanceId && currentId !== this.lastInstanceId) {
                        console.log(`[Heartbeat] 检测到新实例 (旧: ${this.lastInstanceId}, 新: ${currentId})`);
                        
                        // 不要立即更新 lastInstanceId，先验证稳定性
                        
                        // 等待稳定
                        console.log(`[Heartbeat] 等待 Unity MCP 完全初始化 (${UTO_CONFIG.heartbeat.stabilizationDelay}ms)...`);
                        await new Promise(r => setTimeout(r, UTO_CONFIG.heartbeat.stabilizationDelay));
                        
                        // 多次验证确保稳定（3 次验证，每次间隔 500ms）
                        let stable = true;
                        for (let i = 0; i < 3; i++) {
                            try {
                                const verifyResponse = await axios.get(`${this.unityUrl}/health`, { 
                                    timeout: UTO_CONFIG.heartbeat.healthCheckTimeout,
                                    httpAgent: new (require('http').Agent)({ keepAlive: false })
                                });
                                
                                if (verifyResponse.data?.serverId !== currentId) {
                                    console.log(`[Heartbeat] InstanceId 仍在变化，继续等待... (当前: ${verifyResponse.data?.serverId})`);
                                    stable = false;
                                    break;
                                }
                            } catch (e) {
                                console.log(`[Heartbeat] 验证失败，继续等待...`);
                                stable = false;
                                break;
                            }
                            
                            if (i < 2) {
                                await new Promise(r => setTimeout(r, 500));
                            }
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

    /**
     * 检查 Unity 是否就绪
     */
    isReady(): boolean {
        return this.isUnityReady;
    }

    /**
     * 获取当前 InstanceId
     */
    getCurrentInstanceId(): string | null {
        return this.lastInstanceId;
    }
}
