// Assets/Main/Core/Transport/IHttpTransport.cs
namespace PicoTest.Core.Transport
{
    public sealed class HttpResult
    {
        public int Status;
        public byte[] Body;
        public HttpResult(int status, byte[] body) { Status = status; Body = body; }
        public bool Ok => Status >= 200 && Status < 300;
    }

    /// <summary>同步 HTTP 抽象。测试给内存假服务器，真栈给 HttpClientTransport。</summary>
    public interface IHttpTransport
    {
        HttpResult Send(string method, string path, byte[] body);
    }
}
