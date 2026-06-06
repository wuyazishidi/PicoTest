// Assets/Main/Core/Transport/HttpClientTransport.cs
using System;
using System.Net.Http;
using System.Text;

namespace PicoTest.Core.Transport
{
    /// <summary>IHttpTransport 的真实实现。HEAD 的 offset 从 Content-Length 头转为 Body（对齐 FakeIngestServer 语义）。</summary>
    public sealed class HttpClientTransport : IHttpTransport, IDisposable
    {
        private readonly HttpClient _client;
        private readonly string _baseUrl;

        public HttpClientTransport(string baseUrl, string apiToken = null)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            if (!string.IsNullOrEmpty(apiToken))
                _client.DefaultRequestHeaders.Add("X-Api-Token", apiToken);
        }

        public HttpResult Send(string method, string path, byte[] body)
        {
            try
            {
                var req = new HttpRequestMessage(new HttpMethod(method), _baseUrl + path);
                if (body != null) req.Content = new ByteArrayContent(body);
                var resp = _client.SendAsync(req).GetAwaiter().GetResult();

                byte[] respBody;
                if (method == "HEAD")
                {
                    long len = resp.Content?.Headers?.ContentLength ?? 0;
                    respBody = Encoding.UTF8.GetBytes(len.ToString());
                }
                else
                {
                    respBody = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                }
                return new HttpResult((int)resp.StatusCode, respBody);
            }
            catch (Exception)
            {
                return new HttpResult(0, null); // 网络异常 = 不 Ok，交给重试
            }
        }

        public void Dispose() => _client.Dispose();
    }
}
