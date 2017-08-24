using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using Gma.System.MouseKeyHook;
using Application = System.Windows.Application;

namespace RemoteRightClickClient
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private IKeyboardMouseEvents GlobalHook { get; } = Hook.GlobalEvents();
        private ClientWebSocket ClientWebSocket { get; } = new ClientWebSocket();
        private CancellationTokenSource ClientWebSocketCts { get; } = new CancellationTokenSource();

        public MainWindow()
        {
            InitializeComponent();
            Task.Run(async () =>
            {
                await ClientWebSocket.ConnectAsync(new Uri("ws://remote-right-click.steamcn.com/ws"), ClientWebSocketCts.Token);
                while (!ClientWebSocketCts.IsCancellationRequested)
                {
                    var response = await ReceiveStringAsync(ClientWebSocket, ClientWebSocketCts.Token);
                    if (string.IsNullOrEmpty(response))
                    {
                        if (ClientWebSocket.State != WebSocketState.Open)
                        {
                            break;
                        }

                        continue;
                    }
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (response == "RightClick" && (!ModeToggleButton.IsChecked ?? false))
                        {
                            MouseSimulator.ClickRightMouseButton();
                        }
                    });
                }
            }, ClientWebSocketCts.Token);
        }

        private void ModeToggleButton_Checked(object sender, RoutedEventArgs e)
        {
            GlobalHook.MouseDown += GlobalHookMouseDown;
        }
        
        private void ModeToggleButton_Unchecked(object sender, RoutedEventArgs e)
        {
            GlobalHook.MouseDown -= GlobalHookMouseDown;
        }

        private void GlobalHookMouseDown(object sender, MouseEventArgs mouseEventArgs)
        {
            if (mouseEventArgs.Button != MouseButtons.Right) return;
            ClientWebSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("RightClick")),
                WebSocketMessageType.Text, true, CancellationToken.None);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            ClientWebSocketCts.Cancel();
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

                    if (socket.CloseStatus.HasValue) return null;
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
