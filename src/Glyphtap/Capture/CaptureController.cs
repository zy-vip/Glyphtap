using System.Windows.Media.Imaging;
using Glyphtap.Services;

namespace Glyphtap.Capture;

/// <summary>截图会话协调：防重入、捕获、打开窗口、完成/取消/失败处理。</summary>
public sealed class CaptureController
{
    private readonly Action<string, string> _notify;
    private CaptureWindow? _window;

    public CaptureController(Action<string, string> notify) => _notify = notify;

    public bool IsCapturing => _window != null && _window.IsOpen;

    public void StartCapture()
    {
        if (IsCapturing)
            return; // 截图会话中忽略重复触发

        ScreenCaptureResult capture;
        try
        {
            capture = ScreenCaptureService.Capture();
        }
        catch (Exception ex)
        {
            _notify("截图失败", $"无法捕获屏幕：{ex.Message}");
            return;
        }

        _window = CaptureWindow.Open(capture, OnComplete, OnCancel);
    }

    private void OnComplete(BitmapSource image)
    {
        try
        {
            ClipboardService.SetImage(image);
        }
        catch (Exception ex)
        {
            try
            {
                var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Glyphtap");
                System.IO.Directory.CreateDirectory(dir);
                var path = System.IO.Path.Combine(dir, $"截图_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                System.IO.File.WriteAllBytes(path, ClipboardService.EncodePng(image));
                _notify("复制失败", $"剪贴板写入失败（{ex.Message}），截图已保存到：\n{path}");
            }
            catch (Exception saveEx)
            {
                _notify("复制失败", $"剪贴板写入失败（{ex.Message}），且保存到临时文件也失败（{saveEx.Message}）");
            }
        }
        _window = null;
    }

    private void OnCancel()
    {
        _window = null;
    }
}