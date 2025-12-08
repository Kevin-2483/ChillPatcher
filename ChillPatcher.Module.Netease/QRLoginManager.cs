using System;
using System.Threading;
using System.Threading.Tasks;
using BepInEx.Logging;
using QRCoder;
using UnityEngine;

namespace ChillPatcher.Module.Netease
{
    /// <summary>
    /// 二维码登录管理器
    /// 负责生成登录二维码、轮询登录状态
    /// </summary>
    public class QRLoginManager
    {
        private readonly NeteaseBridge _bridge;
        private readonly ManualLogSource _logger;

        private NeteaseBridge.QRLoginState _currentState;
        private Sprite _qrCodeSprite;
        private byte[] _qrCodeBytes;
        private bool _isPolling;
        private CancellationTokenSource _pollingCts;

        /// <summary>
        /// 登录成功事件
        /// </summary>
        public event Action OnLoginSuccess;

        /// <summary>
        /// 二维码更新事件 (新二维码生成时触发)
        /// </summary>
        public event Action<Sprite> OnQRCodeUpdated;

        /// <summary>
        /// 状态变化事件
        /// </summary>
        public event Action<string> OnStatusChanged;

        /// <summary>
        /// 登录失败/取消事件
        /// </summary>
        public event Action<string> OnLoginFailed;

        /// <summary>
        /// 当前二维码 Sprite
        /// </summary>
        public Sprite QRCodeSprite => _qrCodeSprite;

        /// <summary>
        /// 当前二维码字节数据 (用于 SMTC)
        /// </summary>
        public byte[] QRCodeBytes => _qrCodeBytes;

        /// <summary>
        /// 是否正在等待登录
        /// </summary>
        public bool IsWaitingForLogin => _isPolling && _currentState != null;

        /// <summary>
        /// 当前状态消息
        /// </summary>
        public string StatusMessage => _currentState?.StatusMsg ?? "未开始";

        public QRLoginManager(NeteaseBridge bridge, ManualLogSource logger)
        {
            _bridge = bridge;
            _logger = logger;
        }

        /// <summary>
        /// 开始二维码登录流程
        /// </summary>
        public async Task<bool> StartLoginAsync()
        {
            try
            {
                // 取消之前的轮询
                CancelPolling();

                // 清理旧的二维码资源（每次重新开始时都清理）
                CleanupQRCodeResources();

                // 获取新的二维码
                _currentState = _bridge.StartQRLogin();
                if (_currentState == null || string.IsNullOrEmpty(_currentState.QRCodeURL))
                {
                    _logger.LogError("[QRLoginManager] 获取二维码失败");
                    OnLoginFailed?.Invoke("获取二维码失败");
                    return false;
                }

                // 生成二维码图片
                GenerateQRCodeSprite(_currentState.QRCodeURL);

                OnStatusChanged?.Invoke("请使用网易云音乐 APP 扫码");
                OnQRCodeUpdated?.Invoke(_qrCodeSprite);

                // 开始轮询
                _pollingCts = new CancellationTokenSource();
                _isPolling = true;

                // 启动轮询任务
                _ = PollLoginStatusAsync(_pollingCts.Token);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[QRLoginManager] StartLoginAsync exception: {ex}");
                OnLoginFailed?.Invoke("启动登录失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 轮询登录状态
        /// </summary>
        private async Task PollLoginStatusAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(1500, cancellationToken); // 每 1.5 秒检查一次

                    var status = _bridge.CheckQRLoginStatus();
                    if (status == null)
                    {
                        _logger.LogWarning("[QRLoginManager] 检查状态失败");
                        continue;
                    }

                    _currentState = status;
                    OnStatusChanged?.Invoke(status.StatusMsg);

                    if (status.IsSuccess)
                    {
                        _logger.LogInfo("[QRLoginManager] 登录成功！");
                        _isPolling = false;
                        OnLoginSuccess?.Invoke();
                        return;
                    }
                    else if (status.IsExpired)
                    {
                        _logger.LogInfo("[QRLoginManager] 二维码已失效，重新生成...");
                        // 重新开始登录
                        await StartLoginAsync();
                        return; // 新的轮询任务已启动
                    }
                    // IsWaitingScan 和 IsWaitingConfirm 继续轮询
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("[QRLoginManager] 轮询已取消");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[QRLoginManager] 轮询异常: {ex}");
                OnLoginFailed?.Invoke("登录过程出错: " + ex.Message);
            }
            finally
            {
                _isPolling = false;
            }
        }

        /// <summary>
        /// 取消登录
        /// </summary>
        public void CancelLogin()
        {
            CancelPolling();
            _bridge.CancelQRLogin();
            _currentState = null;
            
            CleanupQRCodeResources();
        }

        private void CancelPolling()
        {
            if (_pollingCts != null)
            {
                _pollingCts.Cancel();
                _pollingCts.Dispose();
                _pollingCts = null;
            }
            _isPolling = false;
        }

        /// <summary>
        /// 清理二维码资源
        /// </summary>
        private void CleanupQRCodeResources()
        {
            if (_qrCodeSprite != null && _qrCodeSprite.texture != null)
            {
                UnityEngine.Object.Destroy(_qrCodeSprite.texture);
                UnityEngine.Object.Destroy(_qrCodeSprite);
                _qrCodeSprite = null;
            }
            _qrCodeBytes = null;
        }

        /// <summary>
        /// 生成二维码 Sprite
        /// </summary>
        private void GenerateQRCodeSprite(string url)
        {
            try
            {
                using (var qrGenerator = new QRCodeGenerator())
                {
                    var qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
                    using (var qrCode = new PngByteQRCode(qrCodeData))
                    {
                        _qrCodeBytes = qrCode.GetGraphic(10); // 10 像素每模块
                    }
                }

                // 创建 Unity Texture 和 Sprite
                var texture = new Texture2D(2, 2);
                texture.LoadImage(_qrCodeBytes);
                texture.filterMode = FilterMode.Point; // 二维码需要锐利的边缘

                _qrCodeSprite = Sprite.Create(
                    texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f));

                _logger.LogInfo($"[QRLoginManager] 二维码生成成功: {texture.width}x{texture.height}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[QRLoginManager] 生成二维码失败: {ex}");
            }
        }
    }
}
