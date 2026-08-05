using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using QRCoder;

namespace OmniMixPlayer.Module.Kugou
{
    public sealed class QRLoginManager
    {
        private readonly KugouBridge _bridge;
        private readonly ILogger _logger;
        private CancellationTokenSource _cts;
        private string _key;
        public byte[] QRCodeBytes { get; private set; }
        public string StatusMessage { get; private set; } = "未开始";
        public bool IsPolling { get; private set; }
        public event Action LoginSucceeded;
        public event Action Changed;

        public QRLoginManager(KugouBridge bridge, ILogger logger)
        {
            _bridge = bridge;
            _logger = logger;
        }

        public Task<bool> StartAsync()
        {
            Cancel();
            var qr = _bridge.CreateQR();
            _key = qr.key;
            var url = qr.url;
            if (string.IsNullOrWhiteSpace(url))
            {
                StatusMessage = "二维码获取失败：" + _bridge.GetLastError();
                Changed?.Invoke();
                return Task.FromResult(false);
            }
            using var data = QRCodeGenerator.GenerateQrCode(url, QRCodeGenerator.ECCLevel.Q);
            using var code = new PngByteQRCode(data);
            QRCodeBytes = code.GetGraphic(10);
            StatusMessage = "请使用酷狗音乐概念版 App 扫码";
            _cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            IsPolling = true;
            Changed?.Invoke();
            _ = PollAsync(_cts.Token);
            return Task.FromResult(true);
        }

        private async Task PollAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(1500, token);
                    var state = await Task.Run(() => _bridge.CheckQRStatus(_key), token);
                    if (state == null) continue;
                    StatusMessage = state.Message;
                    Changed?.Invoke();
                    if (state.IsSuccess)
                    {
                        IsPolling = false;
                        LoginSucceeded?.Invoke();
                        return;
                    }
                    if (state.IsExpired) return;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _logger?.LogError(ex, "[酷狗音乐] 二维码轮询失败"); }
            finally { IsPolling = false; }
        }

        public void Cancel()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            IsPolling = false;
            QRCodeBytes = null;
            _key = null;
        }
    }
}
