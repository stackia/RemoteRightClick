using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace RemoteRightClickServer
{
    public class Startup
    {
        private ConcurrentDictionary<string, WebSocket> ClientSockets { get; } = new ConcurrentDictionary<string, WebSocket>();

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseWebSockets();

            app.Use(async (context, next) =>
            {
                if (context.Request.Path == "/ws")
                {
                    if (context.WebSockets.IsWebSocketRequest)
                    {
                        var ct = context.RequestAborted;
                        var currentSocket = await context.WebSockets.AcceptWebSocketAsync();
                        var socketId = Guid.NewGuid().ToString();

                        ClientSockets.TryAdd(socketId, currentSocket);

                        while (true)
                        {
                            if (ct.IsCancellationRequested)
                            {
                                break;
                            }

                            try
                            {
                                var response = await ReceiveStringAsync(currentSocket, ct);
                                if (string.IsNullOrEmpty(response))
                                {
                                    if (currentSocket.State != WebSocketState.Open)
                                    {
                                        break;
                                    }

                                    continue;
                                }

                                foreach (var socket in ClientSockets)
                                {
                                    if (socket.Value.State != WebSocketState.Open)
                                    {
                                        continue;
                                    }

                                    await SendStringAsync(socket.Value, response, ct);
                                }
                            }
                            catch (Exception)
                            {
                                // ignore
                            }
                        }

                        ClientSockets.TryRemove(socketId, out _);

                        if (currentSocket.State == WebSocketState.Open)
                        {
                            await currentSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", ct);
                        }
                        currentSocket.Dispose();
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                    }
                }
                else
                {
                    await next();
                }

            });
        }

        private static Task SendStringAsync(WebSocket socket, string data, CancellationToken ct = default(CancellationToken))
        {
            var buffer = Encoding.UTF8.GetBytes(data);
            var segment = new ArraySegment<byte>(buffer);
            return socket.SendAsync(segment, WebSocketMessageType.Text, true, ct);
        }

        private static async Task<string> ReceiveStringAsync(WebSocket socket, CancellationToken ct = default(CancellationToken))
        {
            var buffer = new ArraySegment<byte>(new byte[8192]);
            using (var ms = new MemoryStream())
            {
                WebSocketReceiveResult result;
                do
                {
                    ct.ThrowIfCancellationRequested();
                    
                    result = await socket.ReceiveAsync(buffer, ct);
                    ms.Write(buffer.Array, buffer.Offset, result.Count);
                }
                while (!result.EndOfMessage);

                ms.Seek(0, SeekOrigin.Begin);
                if (result.MessageType != WebSocketMessageType.Text)
                {
                    return null;
                }

                // Encoding UTF8: https://tools.ietf.org/html/rfc6455#section-5.6
                using (var reader = new StreamReader(ms, Encoding.UTF8))
                {
                    return await reader.ReadToEndAsync();
                }
            }
        }
    }
}
