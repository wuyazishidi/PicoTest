/**
 * UTO 配置文件
 * 统一管理所有配置项
 */

export const UTO_CONFIG = {
    /**
     * 心跳检测配置
     */
    heartbeat: {
        /** 心跳检测间隔（毫秒） */
        interval: 500,
        
        /** 等待 Unity 就绪的超时时间（毫秒）
         * 默认 5 分钟，适应 Unity 长时间编译
         */
        timeout: 5 * 60 * 1000,  // 5 分钟
        
        /** 单次健康检查的超时时间（毫秒） */
        healthCheckTimeout: 1000,
        
        /** 检测到新实例后的稳定等待时间（毫秒）
         * 确保 Unity MCP 的 RPC 服务完全初始化
         */
        stabilizationDelay: 2000  // 2 秒
    },

    /**
     * 端口配置
     */
    port: {
        /** Unity MCP 默认端口（保底值，当 .port 文件不存在时使用） */
        unityDefault: 3212,
        
        /** UTO HTTP 端口偏移量（Unity 端口 + offset） */
        httpOffset: 1
    },

    /**
     * HTTP Server 配置
     */
    http: {
        /** 服务名称 */
        serviceName: 'uto-http-server',
        
        /** 版本号 */
        version: '1.0.0'
    }
};
