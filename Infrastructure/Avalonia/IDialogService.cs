using System.Threading.Tasks;

namespace AvaloniaAppMPV.Infrastructure.Avalonia;

/// <summary>
/// 文件选择等对话框能力抽象。
/// </summary>
public interface IDialogService
{
    Task<string?> OpenVideoFileAsync();
}
